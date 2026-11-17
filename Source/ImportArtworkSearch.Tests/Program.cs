using Playhub.Importing;

int checks=0,calls=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
TaskCompletionSource<IReadOnlyList<string>> Pending()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
async Task Canceled(Task task,string label){try{await task;throw new Exception(label);}catch(OperationCanceledException){Check(true,label);}}
var clock=DateTimeOffset.UtcNow;
using var search=new ImportArtworkSearchSession<string>(()=>clock);
var cover=ImportArtworkSearchKey.Create("SteamGridDB","  Game  ","cover",false);
var hero=ImportArtworkSearchKey.Create("SteamGridDB","Game","hero",false);
Check(ImportArtworkSearchKey.MatchingSteamAppId(" game ","Game",400)==400,"matching original query can retain its known Steam identity");
Check(ImportArtworkSearchKey.MatchingSteamAppId("Other game","Game",400)==0,"typing another game cannot search the original Steam app ID");
Check(ImportArtworkSearchKey.MatchingSteamAppId("Game","Game",-1)==0,"a cached failed Steam lookup is not a verified app identity");
Check(cover.Provider=="steamgriddb"&&cover.Query=="Game","query/provider normalized without changing the category");
Check(ImportArtworkSearchKey.Create("Steam","Game","hero",true)==ImportArtworkSearchKey.Create("steam","Game","hero",false),"cover format does not invalidate a background search");
Check(cover!=ImportArtworkSearchKey.Create("SteamGridDB","Game","cover",true),"square and portrait covers have separate results");
var waiting=Pending();CancellationToken firstToken=default;
var first=search.SearchAsync(cover,ct=>{calls++;firstToken=ct;return waiting.Task;},default);
var duplicate=search.SearchAsync(cover,_=>throw new Exception("duplicate fetch"),default,refresh:true);
Check(calls==1,"repeated Enter while the same request is pending shares one provider call");
var next=search.SearchAsync(hero,_=>{calls++;return Task.FromResult<IReadOnlyList<string>>(["background"]);},default);
Check(firstToken.IsCancellationRequested,"quick category switch cancels the previous provider request");
Check((await next).Single()=="background","new category gets its own results");
waiting.SetResult(["obsolete cover"]);
await Canceled(first,"provider ignoring cancellation cannot publish obsolete results");
await Canceled(duplicate,"all waiters reject the obsolete request");
var recovered=await search.SearchAsync(cover,_=>{calls++;return Task.FromResult<IReadOnlyList<string>>(["fresh cover"]);},default);
Check(recovered.Single()=="fresh cover"&&calls==3,"returning to a canceled category fetches fresh results, never the stale response");
Check((await search.SearchAsync(hero,_=>throw new Exception("cache miss"),default)).Single()=="background","returning to a completed category uses its valid local cache");
Check((await search.SearchAsync(cover,_=>Task.FromResult<IReadOnlyList<string>>(["explicit Enter"]),default,refresh:true)).Single()=="explicit Enter","Enter refreshes an already completed query");
var providerKey=ImportArtworkSearchKey.Create("Steam","Game","cover",false);
Check((await search.SearchAsync(providerKey,_=>Task.FromResult<IReadOnlyList<string>>(["official"]),default)).Single()=="official","provider changes cannot reuse another provider's results");
var queryKey=ImportArtworkSearchKey.Create("Steam","Other game","cover",false);
Check((await search.SearchAsync(queryKey,_=>Task.FromResult<IReadOnlyList<string>>(["other game"]),default)).Single()=="other game","new game query has independent results");
var square=ImportArtworkSearchKey.Create("SteamGridDB","Game","cover",true);
Check((await search.SearchAsync(square,_=>Task.FromResult<IReadOnlyList<string>>(["square"]),default)).Single()=="square","cover format reaches a distinct provider request");
search.Invalidate(clearCache:true);
Check((await search.SearchAsync(cover,_=>Task.FromResult<IReadOnlyList<string>>(["new selected identity"]),default)).Single()=="new selected identity","Search Again identity/credential change invalidates cached results even for the same title");
clock+=TimeSpan.FromMinutes(6);
Check((await search.SearchAsync(cover,_=>Task.FromResult<IReadOnlyList<string>>(["expired cache refreshed"]),default)).Single()=="expired cache refreshed","expired local cache is refreshed automatically");
try{await search.SearchAsync(hero,_=>throw new IOException("inert provider failure"),default,refresh:true);throw new Exception("missing failure");}catch(IOException){Check(true,"provider failure remains a failure, not a successful empty cache");}
Check((await search.SearchAsync(hero,_=>Task.FromResult<IReadOnlyList<string>>([]),default)).Count==0,"a failed request can retry normally");
var editing=Pending();var editingTask=search.SearchAsync(queryKey,_=>editing.Task,default,refresh:true);
search.Invalidate();editing.SetResult(["typed old query"]);
await Canceled(editingTask,"typing invalidates a pending result before Enter submits the new query");
using var cancellation=new CancellationTokenSource();cancellation.Cancel();
try{await search.SearchAsync(cover,_=>throw new Exception("canceled fetch"),cancellation.Token);throw new Exception("missing cancellation");}catch(OperationCanceledException){Check(true,"closed dialog cannot launch another provider call");}
var closing=Pending();var closingTask=search.SearchAsync(hero,_=>closing.Task,default,refresh:true);
search.Dispose();closing.SetResult(["late dialog response"]);
await Canceled(closingTask,"closing the dialog discards a late provider response");
try{await search.SearchAsync(cover,_=>throw new Exception(),default);throw new Exception("missing disposed guard");}catch(ObjectDisposedException){Check(true,"disposed dialog cannot be reused by a later game");}
Console.WriteLine($"PASS {checks} checks (inert; no provider or artwork network calls)");
