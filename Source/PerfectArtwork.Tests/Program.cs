using Playhub.Integrations;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Security.Cryptography;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
ArtworkPixels Solid(int width, int height, byte b, byte g, byte r)
{
    var pixels = new byte[width * height * 4];
    for (int n = 0; n < pixels.Length; n += 4) { pixels[n] = b; pixels[n + 1] = g; pixels[n + 2] = r; pixels[n + 3] = 255; }
    return new(width,height,pixels);
}
ArtworkPixels Logo(byte g, byte r, bool tall)
{
    var pixels = new byte[100 * 100 * 4];
    for (int y = tall ? 10 : 40; y < (tall ? 90 : 60); y++)
        for (int x = tall ? 40 : 10; x < (tall ? 60 : 90); x++) { int at = (y * 100 + x) * 4; pixels[at + 1] = g; pixels[at + 2] = r; pixels[at + 3] = 255; }
    return new(100,100,pixels);
}
async Task<byte[]> Png(ArtworkPixels pixels)
{
    using var stream = new InMemoryRandomAccessStream();
    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
    encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,(uint)pixels.Width,(uint)pixels.Height,96,96,pixels.Bgra);
    await encoder.FlushAsync(); stream.Seek(0); using var reader = new DataReader(stream); await reader.LoadAsync((uint)stream.Size);
    byte[] bytes = new byte[stream.Size]; reader.ReadBytes(bytes); return bytes;
}
async Task<ArtworkPixels> Decode(string path)
{
    byte[] bytes = await File.ReadAllBytesAsync(path); using var stream = new InMemoryRandomAccessStream();
    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
    var decoder = await BitmapDecoder.CreateAsync(stream);
    var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
    return new((int)decoder.PixelWidth,(int)decoder.PixelHeight,data.DetachPixelData());
}
string root = Path.Combine(Path.GetTempPath(),"Playhub-perfect-fixture-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    string background = Path.Combine(root,"source.png"), logo = Path.Combine(root,"logo.png"), nextLogo = Path.Combine(root,"new-logo.png");
    byte[] original = await Png(Solid(300,100,210,50,30));
    await File.WriteAllBytesAsync(background,original); await File.WriteAllBytesAsync(logo,await Png(Logo(0,240,false))); await File.WriteAllBytesAsync(nextLogo,await Png(Logo(240,0,true)));
    var compose = new PerfectArtworkCompositor(Path.Combine(root,"owned"));
    string identity = "pc:xbox:package!app";
    var before = Process.GetCurrentProcess().TotalProcessorTime; var watch = Stopwatch.StartNew();
    var hero = await compose.ComposeAsync(identity,"hero",background,logo);
    watch.Stop(); var cpu = Process.GetCurrentProcess().TotalProcessorTime-before;
    Console.WriteLine($"Explicit hero composition wall={watch.Elapsed.TotalMilliseconds:F1}ms CPU={cpu.TotalMilliseconds:F1}ms");
    Check(hero.Width == 3840 && hero.Height == 1240 && File.Exists(hero.Path),"actual native JPG hero dimensions");
    Check((await File.ReadAllBytesAsync(hero.PristineSourcePath)).SequenceEqual(original),"original background is preserved byte for byte");
    Check((await File.ReadAllBytesAsync(background)).SequenceEqual(original),"source artwork is never overwritten");
    var decoded = await Decode(hero.Path);
    int center = (decoded.Height/2*decoded.Width + decoded.Width/4)*4;
    Check(decoded.Bgra[center+2]>200 && decoded.Bgra[center+1]<50,"transparent logo padding is trimmed and visible logo sits at left quarter");
    Check(decoded.Bgra[(decoded.Width*decoded.Height-1)*4]>180,"background covers target without blank border");
    var banner = await compose.ComposeAsync("emu:system/game","banner",background,logo);
    Check(banner.Width == 1926 && banner.Height == 900 && (await Decode(banner.Path)).Width == 1926,"actual native banner matches Steam proportions at high resolution");
    var edited = await compose.ComposeAsync(identity,"hero",hero.Path,nextLogo);
    Check(edited.PristineSourcePath == hero.PristineSourcePath && edited.SourceSha256 == hero.SourceSha256,"editing a composition reuses its intact pristine background");
    var replacement = await Decode(edited.Path);
    int oldOutside = (replacement.Height/2*replacement.Width + (int)(replacement.Width*.35))*4;
    Check(replacement.Bgra[oldOutside]>150 && replacement.Bgra[oldOutside+2]<80,"old baked logo is absent after re-edit with a narrower logo");
    var historic = await compose.ComposeAsync(identity,"hero",hero.Path,nextLogo);
    Check(historic.SourceSha256 == hero.SourceSha256 && historic.Sha256 == edited.Sha256,"historical A input after A to B resolves per-output pristine instead of re-baking A");
    var customLayout=new PerfectArtworkLayout(LogoX:75,LogoY:50,LogoScale:150,ShadowOpacity:0);
    var placed=await compose.ComposeAsync(identity,"hero",hero.Path,logo,layout:customLayout);
    var placedPixels=await Decode(placed.Path);
    Check(placed.SourceSha256==hero.SourceSha256 && placedPixels.Bgra[(placedPixels.Height/2*placedPixels.Width+placedPixels.Width*3/4)*4+2]>200,"edited native composition places the logo at requested center using pristine source");
    Check(placedPixels.Bgra[(placedPixels.Height/2*placedPixels.Width+placedPixels.Width/4)*4]>150,"moving logo does not retain the old baked logo");
    var repeatedPlacement=await compose.ComposeAsync(identity,"hero",hero.Path,logo,layout:customLayout);
    Check(repeatedPlacement.Sha256==placed.Sha256 && repeatedPlacement.SourceSha256==hero.SourceSha256,"historical input preserves custom layout without re-baking");
    var resetLayout=await compose.ComposeAsync(identity,"hero",placed.Path,logo,layout:new PerfectArtworkLayout());
    Check(resetLayout.Sha256==hero.Sha256 && resetLayout.SourceSha256==hero.SourceSha256,"resetting custom placement restores exact original default pixels");
    var hiddenLogo=await compose.ComposeAsync(identity,"hero",hero.Path,logo,layout:new PerfectArtworkLayout(ShowLogo:false));
    var hiddenPixels=await Decode(hiddenLogo.Path);
    Check(hiddenPixels.Bgra[(hiddenPixels.Height/2*hiddenPixels.Width+hiddenPixels.Width/4)*4]>150 && hiddenPixels.Bgra[(hiddenPixels.Height/2*hiddenPixels.Width+hiddenPixels.Width/4)*4+2]<80,"hidden composition logo leaves the pristine background without logo or shadow");
    var visibleAgain=await compose.ComposeAsync(identity,"hero",hiddenLogo.Path,logo,layout:new PerfectArtworkLayout());
    Check(visibleAgain.Sha256==hero.Sha256 && visibleAgain.SourceSha256==hero.SourceSha256,"re-enabling composition logo restores exact composed output from pristine source");
    Check(hero.Sha256 == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(hero.Path))),"result hash describes physical completed artwork");
    var legacy = await compose.ComposeAsync("pc:legacy-game","hero",hero.Path,logo,pristineSourcePath:background);
    Check(legacy.SourceSha256 == hero.SourceSha256 && legacy.Sha256 == hero.Sha256,"verified legacy pristine input avoids adopting a composed source");
    int beforeFiles = Directory.EnumerateFiles(root,"*.jpg",SearchOption.AllDirectories).Count();
    using var inFlight = new CancellationTokenSource(10); bool aborted = false;
    try { await compose.ComposeAsync("pc:abort-fixture","hero",background,logo,inFlight.Token); } catch(OperationCanceledException) { aborted=true; }
    Check(aborted && Directory.EnumerateFiles(root,"*.jpg",SearchOption.AllDirectories).Count() == beforeFiles,"in-flight cancellation publishes no partial composition");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool cancelled = false;
    try { await compose.ComposeAsync(identity,"hero",background,logo,cancellation.Token); } catch(OperationCanceledException) { cancelled=true; }
    Check(cancelled,"cancelled composition starts no image job");
    string transparent = Path.Combine(root,"transparent.png"); await File.WriteAllBytesAsync(transparent,await Png(new(100,100,new byte[40000])));
    bool empty = false; try { await compose.ComposeAsync(identity,"hero",background,transparent); } catch(InvalidDataException) { empty=true; }
    Check(empty,"empty alpha placeholder cannot complete a composition");
    await File.WriteAllBytesAsync(edited.PristineSourcePath,new byte[100]);
    bool changed = false; try { await compose.ComposeAsync(identity,"hero",edited.Path,logo); } catch(IOException) { changed=true; }
    Check(changed,"modified pristine source is refused instead of baking old composition again");
    Check(!Directory.EnumerateFiles(root,"*.tmp",SearchOption.AllDirectories).Any(),"no unfinished image file remains");
    if (args.Length == 3 && args[0] == "--junction-roots")
    {
        bool rootGuard = false, childGuard = false;
        try { await new PerfectArtworkCompositor(args[1]).ComposeAsync(identity,"hero",background,logo); } catch(IOException) { rootGuard=true; }
        Check(rootGuard,"actual redirected compositor root is refused");
        try { await new PerfectArtworkCompositor(args[2]).ComposeAsync(identity,"hero",background,logo); } catch(IOException) { childGuard=true; }
        Check(childGuard,"actual redirected compositions child is refused before publishing");
    }
    Console.WriteLine($"Perfect artwork checks: {passed} passed. Only owned temporary physical image files.");
}
finally { Directory.Delete(root,true); }
