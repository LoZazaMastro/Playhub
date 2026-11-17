using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Playhub.Models;
using Playhub.Services;

var directory = Path.Combine(Path.GetTempPath(), "Playhub-import-artwork-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory); AppPaths.LocalDataRoot = directory;
int passed = 0, failed = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; }
async Task Case(string name, Func<Task> action) { try { await action(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + " " + e.Message); } }
byte[] Png(Color color, int width=8, int height=12) { using var image = new Bitmap(width, height); using var graphics = Graphics.FromImage(image); graphics.Clear(color); using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
var green = Png(Color.Green); var blue = Png(Color.Blue);
HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
HttpResponseMessage Image(byte[]? bytes = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? green) };
UwpGameEntry Game(string suffix = "one") => new() { Name = "Fixture Game", Aumid = "Fixture!" + suffix, SteamGridDbGameId = 101 };
string Kind(Uri uri) => uri.AbsolutePath.Contains("/grids/") ? uri.Query.Contains("460x215") ? "banner" : "cover"
    : uri.AbsolutePath.Contains("/heroes/") ? "hero" : uri.AbsolutePath.Contains("/logos/") ? "logo" : "icon";
object Options(string type, params string[] names) => new { data = names.Select(name => new { url = "https://image.invalid/" + type + "/" + name + ".png", thumb = "https://image.invalid/thumb.png", width = 8, height = type=="cover"?12:8 }).ToArray() };
try
{
 await Case("transient per-category failure retries and other categories still complete", async () => {
  var game = Game("transient"); var hits = new Dictionary<string,int>(); int cdnHeaders = 0;
  var service = new UwpXboxService(new Handler(request => {
   var uri = request.RequestUri!;
   if (uri.Host == "image.invalid") { if (request.Headers.Authorization is not null) cdnHeaders++; return Image(); }
   string kind = Kind(uri); hits[kind] = hits.GetValueOrDefault(kind) + 1;
   return kind == "banner" && hits[kind] == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(Options(kind, "valid"));
  }));
  await service.RunMissing([game], "inert-key");
  Check(new[]{game.SteamGridDbCoverPath,game.SteamGridDbBannerPath,game.SteamGridDbHeroPath,game.SteamGridDbLogoPath,game.SteamGridDbIconPath}.All(File.Exists), "each available category must have a complete saved file");
  Check(hits["banner"] == 2, "one transient banner failure must recover"); Check(cdnHeaders == 0, "credentials cannot reach CDN");
 });
 await Case("a missing first candidate does not discard a valid alternative of the same game", async () => {
  var game=Game("alternative");
  var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="image.invalid"
   ? request.RequestUri.AbsolutePath.Contains("missing")?new(HttpStatusCode.NotFound):Image()
   :Json(Options(Kind(request.RequestUri),"missing","valid"))));
  await service.RunMissing([game],"inert-key"); Check(File.Exists(game.SteamGridDbLogoPath),"valid second logo must be saved");
 });
 await Case("absent category stays absent while independent categories and user choices survive", async()=>{
  var game=Game("absent"); string chosen=Path.Combine(directory,"chosen.png");await File.WriteAllBytesAsync(chosen,blue);game.ArtworkChoices["cover"]=chosen;game.SteamGridDbCoverPath=chosen;
  var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="image.invalid"?Image():Kind(request.RequestUri)=="logo"?new(HttpStatusCode.NotFound):Json(Options(Kind(request.RequestUri),"valid"))));
  await service.RunMissing([game],"inert-key"); Check(game.SteamGridDbCoverPath==chosen&&File.ReadAllBytes(chosen).SequenceEqual(blue),"existing manual cover must survive");Check(game.SteamGridDbLogoPath=="","nonexistent logo must not be invented");Check(File.Exists(game.SteamGridDbBannerPath)&&File.Exists(game.SteamGridDbIconPath),"other categories must continue");
 });
 await Case("failed refresh preserves the previous file and selection",async()=>{
  var game=Game("refresh");string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(game.Aumid)))[..24].ToLowerInvariant();
  string covers=Path.Combine(directory,"cache","steamgriddb","covers");Directory.CreateDirectory(covers);string old=Path.Combine(covers,key+".png");await File.WriteAllBytesAsync(old,blue);game.SteamGridDbCoverPath=old;
  var service=new UwpXboxService(new Handler(_=>new(HttpStatusCode.ServiceUnavailable)));
  Check(!await service.RefreshSteamGridDbCoverAsync(game,"inert-key"),"failed refresh cannot claim application");Check(game.SteamGridDbCoverPath==old&&File.Exists(old)&&File.ReadAllBytes(old).SequenceEqual(blue),"failure must not delete or clear old cover");
 });
 await Case("executable-only identities never share automatic category files",async()=>{
  var one=Game("unused");var two=Game("unused2");one.Aumid=two.Aumid="";one.LocalExecutablePath=@"C:\fixture\one.exe";two.LocalExecutablePath=@"C:\fixture\two.exe";one.IsLocalExecutable=two.IsLocalExecutable=true;
  var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="image.invalid"?Image():Json(Options(Kind(request.RequestUri),"valid"))));
  await service.RunMissing([one,two],"inert-key");Check(one.SteamGridDbBannerPath!=two.SteamGridDbBannerPath,"two executable sources must not alias one category cache path");
 });
 await Case("late selected download cannot become another game's selection",async()=>{
  var game=Game("current"); string previous=Path.Combine(directory,"previous.png");await File.WriteAllBytesAsync(previous,blue);game.SteamGridDbLogoPath=previous;
  var service=new UwpXboxService(new Handler(_=>{game.Aumid="Successor!game";return Image();}));
  Check(!await service.DownloadAndApplySteamGridDbArtworkAsync(game,"logo",new("https://image.invalid/logo.png","",8,8)),"stale game must refuse delivery");Check(game.SteamGridDbLogoPath==previous&&!game.ArtworkChoices.ContainsKey("logo"),"old response cannot choose on successor");
 });
 await Case("removed manual category cannot be resurrected after metadata lookup",async()=>{
  var game=Game("removal");var service=new UwpXboxService(new Handler(request=>{if(request.RequestUri!.Host=="image.invalid")return Image();string kind=Kind(request.RequestUri);if(kind=="hero")game.ArtworkChoices["hero"]=null;return Json(Options(kind,"valid"));}));
  await service.RunMissing([game],"inert-key");Check(game.SteamGridDbHeroPath==""&&game.ArtworkChoices["hero"] is null,"explicit removal during lookup must win");
 });
 await Case("Steam transient search does not permanently cache no-match",async()=>{
  var game=Game("steam-retry");game.SteamGridDbGameId=0;int searches=0;
  var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="store.steampowered.com"
   ? ++searches==1?new(HttpStatusCode.ServiceUnavailable):Json(new{items=new[]{new{id=42,name="Fixture Game"}}})
   :new(HttpStatusCode.OK)));
  await service.GetOfficialSteamArtworkAsync(game,"cover");await service.GetOfficialSteamArtworkAsync(game,"cover");Check(searches==2&&game.SteamAppId==42,"temporary lookup failure must remain retryable");
 });
 await Case("unrelated first Steam search result is not artwork identity",async()=>{
  var game=Game("unrelated");game.SteamGridDbGameId=0;int heads=0;
  var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="store.steampowered.com"?Json(new{items=new[]{new{id=99,name="Unrelated sequel"}}}):CountHead()));
  HttpResponseMessage CountHead(){heads++;return new(HttpStatusCode.OK);}
  Check((await service.GetOfficialSteamArtworkAsync(game,"cover")).Count==0&&heads==0,"must not offer another title's cover");
 });
 await Case("transport attempts are bounded and authorization failures are not retried",async()=>{
  int hits=0;var handler=new Handler(_=>{hits++;return new(HttpStatusCode.Unauthorized);});
  var game=Game("auth");var service=new UwpXboxService(handler);try{await service.GetSteamGridDbArtworkAsync(game,"logo","inert-key");}catch(HttpRequestException){}
  Check(hits==1,"401 must not retry repeatedly");
 });
 await Case("invalid image bytes never become artwork or abort independent categories",async()=>{
  var game=Game("invalid-image");var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="image.invalid"
   ?request.RequestUri.AbsolutePath.Contains("/hero/")?new(HttpStatusCode.OK){Content=new StringContent("<html>not artwork</html>")}:Image()
   :Json(Options(Kind(request.RequestUri),"valid"))));
  await service.RunMissing([game],"inert-key");Check(game.SteamGridDbHeroPath=="","HTML must not be published as an image");Check(File.Exists(game.SteamGridDbLogoPath)&&File.Exists(game.SteamGridDbIconPath),"invalid hero must not abort later categories");
 });
 await Case("malformed provider category stays isolated",async()=>{
  var game=Game("invalid-provider");var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="image.invalid"?Image()
   :Kind(request.RequestUri)=="hero"?Json(new{data=(object?)null}):Json(Options(Kind(request.RequestUri),"valid"))));
  await service.RunMissing([game],"inert-key");Check(game.SteamGridDbHeroPath==""&&File.Exists(game.SteamGridDbIconPath),"malformed hero response cannot discard icon recovery");
 });
 await Case("card cover loader cannot resurrect a choice removed during its download",async()=>{
  var game=Game("cover-removal");game.IsLocalExecutable=true;
  var service=new UwpXboxService(new Handler(request=>request.RequestUri!.Host=="image.invalid"
   ? Remove():Json(Options("cover","valid"))));
  HttpResponseMessage Remove(){game.ArtworkChoices["cover"]=null;return Image();}
  await service.RunCovers([game],"inert-key");Check(game.SteamGridDbCoverPath==""&&game.ArtworkChoices["cover"] is null,"card cover must respect newer removal");
 });
 await Case("cache is bound to the selected provider game ID, not only its executable identity",async()=>{
  var one=Game("same-cache");var two=Game("same-cache");two.SteamGridDbGameId=777;byte[] current=green;
  var service=new UwpXboxService(new Handler(request=>{if(request.RequestUri!.Host=="image.invalid")return Image(current);current=request.RequestUri.AbsolutePath.Contains("777")?blue:green;return Json(Options(Kind(request.RequestUri),"valid"));}));
  await service.RunMissing([one],"inert-key");await service.RunMissing([two],"inert-key");
  Check(one.SteamGridDbLogoPath!=two.SteamGridDbLogoPath&&File.ReadAllBytes(two.SteamGridDbLogoPath).SequenceEqual(blue),"provider ID switch must not reuse former provider artwork");
 });
 await Case("same game square preference filters provider candidates and cannot reuse portrait automatic cache",async()=>{
  var game=Game("shape");var square=Png(Color.Yellow,8,8);int requests=0;
  var service=new UwpXboxService(new Handler(request=>{requests++;var uri=request.RequestUri!;
   if(uri.Host=="image.invalid")return Image(uri.AbsolutePath.Contains("square")?square:green);
   string kind=Kind(uri);if(kind=="cover"&&uri.Query.Contains("1024x1024"))return Json(new{data=new[]{new{url="https://image.invalid/cover/portrait.png",width=8,height=12},new{url="https://image.invalid/cover/square.png",width=8,height=8}}});
   return Json(Options(kind,"valid"));}));
  await service.RunMissing([game],"inert-key");string portrait=game.SteamGridDbCoverPath;Check(File.Exists(portrait),"initial portrait must exist");
  service.CoverFormat=CoverFormat.Square;var options=await service.GetSteamGridDbArtworkAsync(game,"cover","inert-key");Check(options.Count==1&&options[0].Width==options[0].Height,"square query must not offer portrait metadata");
  await service.RunMissing([game],"inert-key");Check(game.SteamGridDbCoverPath!=portrait&&File.ReadAllBytes(game.SteamGridDbCoverPath).SequenceEqual(square),"square import must use a real square payload");Check(File.Exists(portrait),"changing automatic format must preserve old file");
  game.ArtworkChoices["cover"]=portrait;game.SteamGridDbCoverPath=portrait;int before=requests;await service.RunMissing([game],"inert-key");Check(game.SteamGridDbCoverPath==portrait&&requests==before,"manual portrait choice must not be overwritten by global square preference");
 });
 await Case("a provider's claimed square dimensions do not substitute for real image dimensions",async()=>{
  var game=Game("shape-payload");var square=Png(Color.Orange,8,8);var service=new UwpXboxService(new Handler(request=>{
   var uri=request.RequestUri!;if(uri.Host=="image.invalid")return Image(uri.AbsolutePath.Contains("actual-square")?square:green);
   string kind=Kind(uri);return kind=="cover"?Json(new{data=new[]{new{url="https://image.invalid/cover/wrong-bytes.png",width=8,height=8},new{url="https://image.invalid/cover/actual-square.png",width=8,height=8}}}):Json(Options(kind,"valid"));}));
  service.CoverFormat=CoverFormat.Square;await service.RunMissing([game],"inert-key");Check(File.ReadAllBytes(game.SteamGridDbCoverPath).SequenceEqual(square),"real portrait bytes must be refused even if metadata says square");
 });
 await Case("peer format change during metadata cannot use the previous format cache",async()=>{
  var seed=Game("metadata-format");seed.IsLocalExecutable=true;
  UwpXboxService? service=null;
  service=new UwpXboxService(new Handler(request=>{
   if(request.RequestUri!.AbsolutePath.Contains("/search/autocomplete/")){
    service!.CoverFormat=CoverFormat.Square;
    return Json(new{data=new[]{new{id=101,name="Fixture Game"}}});
   }
   return request.RequestUri.Host=="image.invalid"?Image():Json(Options("cover","valid"));
  }));
  await service.RunCovers([seed],"inert-key");string portrait=seed.SteamGridDbCoverPath;Check(File.Exists(portrait),"peer seed portrait must exist");
  var current=Game("metadata-format");current.SteamGridDbGameId=0;current.IsLocalExecutable=false;
  await service.RunCovers([current],"inert-key");
  Check(current.SteamGridDbCoverPath!=portrait,"metadata-wait format change must not publish the old portrait cache as square");
 });

}
finally { Directory.Delete(directory,true); }
Console.WriteLine($"{passed} assertions passed; {failed} cases failed");return failed==0?0:1;

sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(run(request));} }
