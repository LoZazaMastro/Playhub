using System.Text.Json.Nodes;
using Playhub.Integrations;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
string root = Path.Combine(Path.GetTempPath(), "Playhub-native-integrations-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int calls = 0, downloads = 0;
bool corruptProbe = false;
var commands = new List<IntegrationToolCommand>();
Task<IntegrationToolResult> Runner(IntegrationToolCommand command, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested(); calls++;
    commands.Add(command);
    if (command.Tool == "ffprobe.exe" && corruptProbe) return Task.FromResult(new IntegrationToolResult(1, "corrupt cache"));
    if (command.Tool == "ffprobe.exe")
        return Task.FromResult(new IntegrationToolResult(0, "{\"format\":{\"duration\":\"30.5\"},\"streams\":[{\"codec_type\":\"audio\",\"codec_name\":\"aac\"},{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":1920,\"height\":1080}]}"));
    string path;
    if (command.Tool == "yt-dlp.exe")
    {
        downloads++;
        path = Path.Combine(command.WorkingDirectory, command.Arguments.Contains("--extract-audio") ? "payload.m4a" : "payload.mp4");
    }
    else path = command.Arguments[^1];
    File.WriteAllBytes(path, [0,0,0,20,102,116,121,112,105,115,111,109,0,0,0,0,1,2,3,4]);
    return Task.FromResult(new IntegrationToolResult(0, "fixture tool output"));
}
string Resolve(string name) => Path.Combine(root, "own-tools", name);
try
{
    await ArtworkProviderChecks.Run(Check);
    string identity = ApplicationIntegrationIdentity.Create("pc", "xbox:package!app");
    var store = new ApplicationIntegrationDataStore(Path.Combine(root, "drafts"));
    await store.PatchAsync(identity, "themedeck", new JsonObject { ["volume"] = 0.4, ["foreign"] = "preserve" });
    await store.PatchAsync(identity, "themedeck", new JsonObject { ["volume"] = 0.6 });
    Check((await store.ReadCategoryAsync(identity, "themedeck"))["foreign"]!.ToString() == "preserve", "draft edits preserve unrelated consumer fields");
    var parallelStore = new ApplicationIntegrationDataStore(Path.Combine(root, "drafts"));
    await Task.WhenAll(store.PatchAsync(identity, "themedeck", new JsonObject { ["first"] = true }), parallelStore.PatchAsync(identity, "themedeck", new JsonObject { ["second"] = true }));
    var parallel = await store.ReadCategoryAsync(identity, "themedeck");
    Check(parallel["first"]!.GetValue<bool>() && parallel["second"]!.GetValue<bool>(), "concurrent app instances cannot lose unrelated fields");
    Check(await store.ResolveSteamAppIdAsync(42) is null, "draft creation invents no Steam binding");
    await store.BindSteamAppIdAsync(identity, 42);
    Check(await store.ResolveSteamAppIdAsync(42) == identity, "explicit real shortcut binding resolves stable draft");
    bool conflict = false;
    try { await store.BindSteamAppIdAsync(ApplicationIntegrationIdentity.Create("emu", "system/game"), 42); } catch (InvalidOperationException) { conflict = true; }
    Check(conflict && await store.ResolveSteamAppIdAsync(42) == identity, "ambiguous binding is refused without replacing identity");
    using var cancelledWrite = new CancellationTokenSource(); cancelledWrite.Cancel();
    try { await store.PatchAsync(identity, "themedeck", new JsonObject { ["foreign"] = "lost" }, cancelledWrite.Token); } catch (OperationCanceledException) { }
    Check((await store.ReadCategoryAsync(identity, "themedeck"))["foreign"]!.ToString() == "preserve", "cancelled edit leaves previous physical draft intact");
    bool fakeId = false;
    try { await store.BindSteamAppIdAsync(identity, 0); } catch (ArgumentException) { fakeId = true; }
    Check(fakeId, "zero is not a real Steam shortcut binding");
    await using (var media = new ApplicationMediaAcquisition(Path.Combine(root, "data"), Resolve, Runner))
    {
        var music = media.Start(new(42, "themedeck", "AbCdEf012_-", NormalizeAudio: true, UpmixAudio: true));
        string id = music["jobId"]!.GetValue<string>();
        Check(music["status"]!.ToString() == "queued" && music["delivered"]!.ToString() == "false", "queued work never claims acquisition or plugin delivery");
        await media.WaitAsync(id);
        var done = media.Get(id);
        Check(done["status"]!.ToString() == "done" && done["acquired"]!.GetValue<bool>() && !done["delivered"]!.GetValue<bool>(), "completed acquisition remains separate from plugin delivery");
        Check(File.Exists(done["path"]!.ToString()), "completed acquisition requires a physical published artifact");
        Check(commands.Any(c => c.Tool == "ffmpeg.exe" && c.Arguments.Contains("loudnorm=I=-16:TP=-1.5:LRA=11")), "normalization uses app-owned FFmpeg");
        Check(commands.Count(c => c.Tool == "ffprobe.exe") == 1, "published artifact is checked through actual probe contract");
        Check(!Directory.EnumerateDirectories(Path.Combine(root, "data", "pending")).Any(), "job removes only its temporary workspace");
        int previous = calls;
        var repeat = media.Start(new(51, "themedeck", "AbCdEf012_-", NormalizeAudio: true));
        await media.WaitAsync(repeat["jobId"]!.ToString());
        Check(calls == previous+1 && downloads == 1, "same media cache is probed and reused without another download");
        Check(commands.All(c => c.Arguments.Contains("--ignore-config") || c.Tool != "yt-dlp.exe"), "user downloader configuration cannot change acquisition");
        Check(commands.All(c => c.Arguments.Contains("--no-plugin-dirs") || c.Tool != "yt-dlp.exe"), "downloader extensions from installed plugins are not loaded");
        bool invalid = false;
        try { media.Start(new(42, "themedeck", "--exec cmd.exe")); } catch (ArgumentException) { invalid = true; }
        Check(invalid && calls == previous+1, "invalid remote identity cannot schedule a command");
        corruptProbe = true;
        var corrupted = media.Start(new(52, "themedeck", "AbCdEf012_-", NormalizeAudio: true));
        await media.WaitAsync(corrupted["jobId"]!.ToString());
        var rejected = media.Get(corrupted["jobId"]!.ToString());
        Check(rejected["status"]!.ToString() == "failed" && !rejected["acquired"]!.GetValue<bool>() && !rejected["delivered"]!.GetValue<bool>() && downloads == 1, "cached FTYP-only corruption cannot report acquired or delivered");
        corruptProbe = false;
        var draftJob = media.Start(new(0, "themedeck", "BcDeFg123_-", StableIdentity: identity));
        await media.WaitAsync(draftJob["jobId"]!.ToString());
        Check(media.Get(draftJob["jobId"]!.ToString())["acquired"]!.GetValue<bool>(), "stable app draft acquires media before export without fabricated AppID");
        var videoJob = media.Start(new(0,"trailerhero","CdEfGh234_-",Quality:2160,StableIdentity:identity));
        Check(videoJob["actualHeight"] is null && videoJob["duration"] is null,"queued media reports no unverified dimensions or duration");
        await media.WaitAsync(videoJob["jobId"]!.ToString());
        var verifiedVideo = media.Get(videoJob["jobId"]!.ToString());
        Check(verifiedVideo["actualHeight"]!.GetValue<int>() == 1080 && verifiedVideo["actualWidth"]!.GetValue<int>() == 1920 && verifiedVideo["duration"]!.GetValue<double>() == 30.5,"job exports actual probe dimensions and duration rather than requested quality");
    }
    await using (var search = new ApplicationMediaSearch(Path.Combine(root,"search-check"), (command, ct) =>
    {
        Check(command.Arguments.Contains("--ignore-config") && command.Arguments.Contains("--no-plugin-dirs") && command.Arguments.Last().StartsWith("ytsearch20:",StringComparison.Ordinal), "media search isolates configuration and starts a bounded query");
        return Task.FromResult(new IntegrationToolResult(0,"{\"id\":\"AbCdEf012_-\",\"title\":\"Theme\",\"duration\":30}\n{\"id\":\"not#valid\",\"title\":\"Invalid\"}\n{\"id\":\"BcDeFg123_-\",\"title\":\"Too long\",\"duration\":901}"));
    }))
    {
        var result = await search.SearchAsync("Cuphead", "themedeck");
        Check(result.Count == 1 && result[0]!["id"]!.ToString() == "AbCdEf012_-", "media search filters invalid identities and overlong results");
        Check(!Directory.EnumerateDirectories(Path.Combine(root,"search-check","search")).Any(), "search leaves no pending workspace");
    }
    var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int active = 0, maximum = 0;
    async Task<IntegrationToolResult> Waiting(IntegrationToolCommand command, CancellationToken ct)
    {
        int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count); entered.TrySetResult();
        try { await block.Task.WaitAsync(ct); return new(1, ""); }
        finally { Interlocked.Decrement(ref active); }
    }
    var cancellable = new ApplicationMediaAcquisition(Path.Combine(root, "waiting"), Resolve, Waiting);
    var jobs = Enumerable.Range(0, 4).Select(n => cancellable.Start(new((uint)(100+n), "trailerhero", "AbCdEf012_" + n))).ToArray();
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(maximum <= 2, "owned process jobs are limited to two concurrent workers");
    bool full = false;
    try { cancellable.Start(new(105, "trailerhero", "AbCdEf012_5")); } catch (InvalidOperationException) { full = true; }
    Check(full, "queue is bounded while work is active");
    string cancelled = jobs[0]["jobId"]!.ToString(); cancellable.Cancel(cancelled);
    await cancellable.WaitAsync(cancelled);
    Check(cancellable.Get(cancelled)["status"]!.ToString() == "cancelled", "cancelling one owned job records cancellation without assignment");
    await cancellable.DisposeAsync();
    Check(active == 0, "disposal waits for every owned worker to stop");
    Check(!Directory.EnumerateFiles(Path.Combine(root, "waiting"), "*.mp4", SearchOption.AllDirectories).Any(), "cancelled jobs publish no media files");
    bool disposed = false;
    try { cancellable.Start(new(105, "trailerhero", "AbCdEf012_5")); } catch (ObjectDisposedException) { disposed = true; }
    Check(disposed, "closed engine rejects late jobs");
    var missing = new ApplicationToolRunner(Path.Combine(root, "missing-tools"), root);
    bool noFallback = false;
    try { missing.Resolve("yt-dlp.exe"); } catch (FileNotFoundException) { noFallback = true; }
    Check(noFallback, "missing app tool never falls back to installed plugin or PATH");
    bool noGame = false;
    try { missing.Resolve("Cuphead.exe"); } catch (ArgumentException) { noGame = true; }
    Check(noGame, "owned runner rejects arbitrary game executables");
    if (args.Length == 2 && args[0] == "--junction-root")
    {
        string linkedRoot = Path.GetFullPath(args[1]);
        Check((File.GetAttributes(linkedRoot) & FileAttributes.ReparsePoint) != 0, "redirected-root fixture is an actual Windows junction");
        var linkedStore = new ApplicationIntegrationDataStore(linkedRoot);
        bool guardedStore = false;
        try { await linkedStore.PatchAsync(identity, "themedeck", new JsonObject { ["path"] = "must not publish" }); } catch (IOException) { guardedStore = true; }
        Check(guardedStore, "draft writes reject a redirected ancestor");
        await using var redirectedMedia = new ApplicationMediaAcquisition(linkedRoot, Resolve, Runner);
        int before = calls;
        var blockedJob = redirectedMedia.Start(new(42, "trailerhero", "AbCdEf012_-"));
        await redirectedMedia.WaitAsync(blockedJob["jobId"]!.ToString());
        Check(redirectedMedia.Get(blockedJob["jobId"]!.ToString())["status"]!.ToString() == "failed" && before == calls, "media acquisition refuses junction root before starting tools");
        var redirectedRunner = new ApplicationToolRunner(root, linkedRoot);
        bool guardedRunner = false;
        try { await redirectedRunner.RunAsync(new("yt-dlp.exe", [], Path.Combine(linkedRoot, "workspace"), TimeSpan.FromSeconds(1)), CancellationToken.None); } catch (IOException) { guardedRunner = true; }
        Check(guardedRunner, "tool workspace cannot follow redirected data root");
        Check(!Directory.Exists(Path.Combine(linkedRoot, "pending")) && !Directory.Exists(Path.Combine(linkedRoot, "games")), "guard failure creates no files in redirected target");
    }
    Console.WriteLine($"Application integration checks: {passed} passed. Only fake runner and unique temporary files used.");
    if (args.Length == 1 && args[0] == "--http-audit")
    {
        using var actual = new ApplicationArtworkProviders();
        foreach (var request in new[] { ("igdb","Cuphead","grid_p",false), ("iidb","Cuphead","hero",false), ("xbox","Cuphead","grid_l",false), ("nintendo","Super Mario Odyssey","grid_p",true), ("playstation","Astro Bot","grid_l",false), ("ign","Cuphead","grid_p",true), ("alphacoders","Cuphead","hero",false) })
        {
            try
            {
                var result = await actual.SearchAsync(request.Item1,request.Item2,request.Item3,request.Item4,limit:2);
                Console.WriteLine("ACTUAL HTTP " + request.Item1 + " results=" + result.Count);
                if (result.Count == 0) throw new Exception("No candidate returned for the live audit title.");
                var bytes = await actual.DownloadAsync(request.Item1,result[0]!["url"]!.ToString());
                Console.WriteLine("ACTUAL DOWNLOAD " + request.Item1 + " bytes=" + bytes.Length);
            }
            catch (Exception error) { Console.WriteLine("ACTUAL HTTP " + request.Item1 + " failure=" + error.GetType().Name + " " + error.Message); }
        }
    }
    if (args.Length == 1 && args[0] is "--tools-audit" or "--trailer-audit")
    {
        string tools = Path.GetFullPath("Source/Playhub/Tools/Media");
        var actualRunner = new ApplicationToolRunner(tools,root);
        string work = Path.Combine(root,"actual-tools"); Directory.CreateDirectory(work);
        foreach (var tool in new[] { "yt-dlp.exe", "node.exe", "ffmpeg.exe", "ffprobe.exe" })
        {
            var version = await actualRunner.RunAsync(new(tool,[tool.StartsWith("ff") ? "-version" : "--version"],work,TimeSpan.FromSeconds(10)),CancellationToken.None);
            Check(version.ExitCode == 0, "actual packaged version command " + tool);
            Console.WriteLine(version.Output.Split('\n')[0]);
        }
        using var cancellation = new CancellationTokenSource(300);
        bool stopped = false;
        try { await actualRunner.RunAsync(new("node.exe",["--eval","setInterval(()=>{},1000)"],work,TimeSpan.FromSeconds(10)),cancellation.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "actual owned process cancellation stops its held child");
        await using var search = new ApplicationMediaSearch(Path.Combine(root,"actual-search"),tools);
        await using var media = new ApplicationMediaAcquisition(Path.Combine(root,"actual-media"),tools);
        foreach (var category in (args[0] == "--trailer-audit" ? new[] { "trailerhero" } : new[] { "themedeck", "trailerhero" }))
        {
            var results = await search.SearchAsync("Cuphead", category);
            Check(results.Count > 0, "actual packaged YouTube " + category + " search returns selectable results");
            var selected = results.OfType<JsonObject>().FirstOrDefault(item => double.TryParse(item["duration"]?.ToString(),out var duration) && duration is > 0 and <= 180) ?? throw new Exception("No bounded short media result.");
            Console.WriteLine("ACTUAL SELECTED " + category + " videoId=" + selected["id"] + " duration=" + selected["duration"]);
            var job = media.Start(new(0,category,selected["id"]!.ToString(),Quality:720,NormalizeAudio:category == "themedeck",StableIdentity:"pc:actual-tool-fixture"));
            await media.WaitAsync(job["jobId"]!.ToString());
            var completed = media.Get(job["jobId"]!.ToString());
            Console.WriteLine(completed.ToJsonString());
            Check(completed["acquired"]!.GetValue<bool>(), "actual packaged " + category + " download and probe publish a physical artifact");
            var probe = await actualRunner.RunAsync(new("ffprobe.exe",["-v","error","-show_entries","format=duration:stream=codec_type,codec_name,width,height","-of","json",completed["path"]!.ToString()],work,TimeSpan.FromSeconds(10)),CancellationToken.None);
            Console.WriteLine("ACTUAL PROBE " + category + " " + probe.Output.Replace("\n"," ").Replace("\r",""));
            if (category == "trailerhero") Check(probe.Output.Contains("h264") && probe.Output.Contains("aac"), "actual trailer is H264/AAC MP4");
            if (category == "trailerhero")
            {
                var actual = JsonNode.Parse(probe.Output)!;
                var stream = actual["streams"]!.AsArray().First(item=>item?["codec_type"]?.ToString()=="video")!;
                Check(completed["actualHeight"]!.GetValue<int>() == stream["height"]!.GetValue<int>() && completed["actualWidth"]!.GetValue<int>() == stream["width"]!.GetValue<int>(),"actual trailer snapshot preserves independently probed physical dimensions");
            }
        }

    }
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

