// SPDX-License-Identifier: MIT. Inert fixtures only; no installer, game, credentials or network.
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
async Task Refuses(Func<Task> action, string code)
{ try { await action(); throw new Exception("accepted:" + code); } catch (StoreOperationException error) { Check(error.Code == code, code + ":" + error.Code); } }
string root = Path.Combine(Path.GetTempPath(), "Playhub-StorePrerequisites-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
string runtime = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Playhub.StoreHost/Runtime"));
byte[] payload = Encoding.UTF8.GetBytes("inert installer fixture");
StoreOwnedInstallFile file = new("Redists/vcredist_x86.exe", payload.Length, "SHA256", Convert.ToHexString(SHA256.HashData(payload)));
try
{
    string owned = Path.Combine(root, "shared"); Directory.CreateDirectory(Path.Combine(owned, "Redists"));
    Guid job = Guid.NewGuid(); File.WriteAllText(Path.Combine(owned, ".playhub-install-owner"), job.ToString("D")); File.WriteAllBytes(Path.Combine(owned, file.RelativePath), payload);
    string journal = Path.Combine(root, "shared-receipt.json"); int runs = 0, exit = 0;
    int Run(string path, IReadOnlyList<string> arguments, CancellationToken token)
    {
        Check(path == Path.Combine(owned, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)), "exact manifest-covered installer");
        Check(arguments.SequenceEqual(new[] { "/Q" }), "exact declared arguments without shell rewriting");
        bool pinned = false; try { File.WriteAllText(path, "tamper during execution"); } catch (IOException) { pinned = true; }
        Check(pinned, "installer stays retained through execution"); runs++; return exit;
    }
    StorePrerequisiteStep[] steps = [new(file.RelativePath, ["/Q"], [0, 1641, 3010])];
    void Apply(string authorization = "verified-manifest", CancellationToken token = default) => StorePrerequisiteInstaller.Apply(owned, job, authorization, [file], steps, journal, token, Run);
    Apply(); Apply(); Check(runs == 1 && File.Exists(journal), "repeat completion revalidates bytes and reuses exact receipt");
    exit = 3010; Apply("new-manifest"); Check(runs == 2, "reboot-required success only when declared");
    exit = 1603; await Refuses(() => { Apply("failed-manifest"); return Task.CompletedTask; }, "prerequisite_installer_failed");
    using (var receipt = JsonDocument.Parse(File.ReadAllBytes(journal))) Check(receipt.RootElement.GetProperty("Authorization").GetString() == "new-manifest", "failure cannot replace success receipt");
    var before = runs;
    await Refuses(() => { StorePrerequisiteInstaller.Apply(owned, job, "bad", [file], [steps[0], new("../outside.exe", [], [0])], journal, default, Run); return Task.CompletedTask; }, "uninstall_manifest_invalid");
    Check(runs == before, "all declarations preflight before first installer");
    File.WriteAllText(Path.Combine(owned, file.RelativePath), "changed");
    await Refuses(() => { Apply(); return Task.CompletedTask; }, "installed_files_modified"); Check(runs == before, "receipt never overrides altered payload");
    File.WriteAllBytes(Path.Combine(owned, file.RelativePath), payload);
    File.WriteAllText(Path.Combine(owned, ".playhub-install-owner"), Guid.NewGuid().ToString("D"));
    await Refuses(() => { Apply(); return Task.CompletedTask; }, "prerequisite_ownership_lost"); Check(runs == before, "foreign root cannot run setup");

    // Exercise the actual Epic adapter and its retained-manifest CLI protocol.
    string epicRoot = Path.Combine(root, "epic-game"), epicState = Path.Combine(root, "epic-state"); Directory.CreateDirectory(Path.Combine(epicRoot, "Redists"));
    File.WriteAllText(Path.Combine(epicRoot, ".playhub-install-owner"), job.ToString("D")); File.WriteAllBytes(Path.Combine(epicRoot, file.RelativePath), payload);
    string native = Path.Combine(epicState, "epic", "native"); Directory.CreateDirectory(Path.Combine(native, "manifests"));
    File.WriteAllText(Path.Combine(native, "installed.json"), """{"FixtureApp":{"version":"1","platform":"Windows"}}""");
    string manifestPath = Path.Combine(native, "manifests", "FixtureApp_Windows_1.manifest"); File.WriteAllText(manifestPath, "inert retained manifest");
    int epicRuns = 0, epicQueries = 0; bool tamperEpic = false;
    Task<(int, string)> EpicQuery(string tool, IReadOnlyList<string> args, IReadOnlyDictionary<string,string> env, CancellationToken token)
    {
        epicQueries++;
        if (args[0] == "info")
        {
            Check(args.SequenceEqual(new[] { "info", manifestPath, "--offline", "--json" }), "Epic info parses exact retained manifest offline");
            return Task.FromResult((0, """{"manifest":{"app_name":"FixtureApp","prerequisites":{"path":"Redists/vcredist_x86.exe","args":"/Q","ids":["vc"]}}}"""));
        }
        Check(args.SequenceEqual(new[] { "list-files", "FixtureApp", "--json", "--manifest", manifestPath }), "Epic file checksums from same retained manifest");
        if (tamperEpic) File.WriteAllText(Path.Combine(epicRoot, file.RelativePath), "changed");
        return Task.FromResult((0, JsonSerializer.Serialize(new[] { new { filename = file.RelativePath, file_size = payload.Length, sha_hash = Convert.ToHexString(SHA1.HashData(payload)) } })));
    }
    var epic = new StoreCliProvider(StoreKind.Epic, runtime, epicState, "unexecuted-host.exe", new HttpClient(new NoNetwork()), EpicQuery);
    epic.PrerequisiteRunner = (path, arguments, token) => { Check(arguments.SequenceEqual(new[] { "/Q" }), "Epic preserves SFX /Q"); epicRuns++; return 0; };
    await epic.FinalizeEpicPrerequisitesAsync("FixtureApp", epicRoot, job, default);
    await epic.FinalizeEpicPrerequisitesAsync("FixtureApp", epicRoot, job, default);
    Check(epicRuns == 1 && epicQueries == 4, "Epic actual adapter executes once and independently revalidates on repeat");
    tamperEpic = true; await Refuses(() => epic.FinalizeEpicPrerequisitesAsync("FixtureApp", epicRoot, job, default), "installed_files_modified"); Check(epicRuns == 1, "Epic tampered setup never runs");

    // Exercise GOG dependency discovery, separate download and final verification.
    var http = new GogMetadata(payload); string gogState = Path.Combine(root, "gog-state"), game = Path.Combine(root, "gog-game"); Directory.CreateDirectory(game);
    File.WriteAllText(Path.Combine(game, ".playhub-install-owner"), job.ToString("D")); int gogRuns = 0; bool tamperGog = false;
    Task<(int,string)> GogQuery(string tool, IReadOnlyList<string> args, IReadOnlyDictionary<string,string> env, CancellationToken token)
    {
        Check(args.Count == 7 && args[2] == "dependencies" && args[3] == "--ids" && args[4] == "VC2005" && args[5] == "--path", "GOG uses declared dependency ID and separate redist download command");
        string cache = args[6]; Check(!cache.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "GOG global runtimes outside game uninstall tree");
        string target = Path.Combine(cache, "__redist", "VC", "vcredist_x86.exe"); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, tamperGog ? [1,2,3] : payload);
        return Task.FromResult((0,""));
    }
    var gog = new StoreCliProvider(StoreKind.Gog, runtime, gogState, "unexecuted-host.exe", new HttpClient(http), GogQuery);
    gog.PrerequisiteRunner = (path, arguments, token) => { Check(arguments.SequenceEqual(new[] { "/Q" }), "GOG preserves SFX /Q"); gogRuns++; return 0; };
    using var metadata = JsonDocument.Parse("""{"dependencies":["VC2005"]}""");
    await gog.FinalizeGogPrerequisitesAsync("100", game, job, metadata.RootElement, default);
    Check(gogRuns == 1 && Directory.GetFiles(game).Length == 1, "GOG verified setup executes without adding machine runtimes to game tree");
    tamperGog = true; await Refuses(() => gog.FinalizeGogPrerequisitesAsync("100", game, job, metadata.RootElement, default), "installed_files_modified"); Check(gogRuns == 1, "GOG dependency payload tamper cannot reuse receipt");
    var updates = new List<TransferProgress>(); var times = new Queue<double>([0, 2, 4, 6, 8]);
    string telemetry = string.Join('\n', new[] { 100, 300, 300, 10, 210 }.Select(n => JsonSerializer.Serialize(new { kind = "stdout", line = $"= Progress: 1 {n}/1000," }))) +
        "\n" + """{"kind":"exit","exitCode":0,"stopRequested":false,"drained":true,"forced":false}""";
    await AmazonWorker.ReadAsync(new StringReader(telemetry), new StringReader(""), _ => Task.CompletedTask, updates.Add, () => times.Dequeue());
    Check(updates.Select(p => p.BytesPerSecond).SequenceEqual(new double?[] { null, 100, 0, null, 100 }), "Amazon speed uses byte deltas, shows real zero and resets after rollback without counting resumed bytes");
    checks += await FinalizationRetryChecks.Run(root);
    Console.WriteLine($"PASS {checks} shared, Epic and GOG prerequisite, retry and telemetry checks; no installer executed");
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

sealed class NoNetwork : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("network forbidden"); }
sealed class GogMetadata : HttpMessageHandler
{
    readonly Dictionary<string, byte[]> responses = [];
    internal GogMetadata(byte[] payload)
    {
        byte[] depot = Zip(JsonSerializer.SerializeToUtf8Bytes(new { depot = new { items = new[] { new { type = "DepotFile", path = "__redist/VC/vcredist_x86.exe", md5 = Hash(payload), chunks = new[] { new { size = payload.Length, md5 = Hash(payload) } } } } } }));
        string hash = Hash(depot), depotPath = $"/content-system/v2/dependencies/meta/{hash[..2]}/{hash.Substring(2,2)}/{hash}"; responses[depotPath] = depot;
        byte[] repository = Zip(JsonSerializer.SerializeToUtf8Bytes(new { depots = new[] { new { dependencyId = "VC2005", manifest = hash, executable = new { path = "__redist/VC/vcredist_x86.exe", arguments = "/Q" } } } }));
        string repositoryHash = Hash(repository), repoPath = $"/content-system/v2/dependencies/meta/{repositoryHash[..2]}/{repositoryHash.Substring(2,2)}/{repositoryHash}"; responses[repoPath] = repository;
        responses["/dependencies/repository"] = JsonSerializer.SerializeToUtf8Bytes(new { repository_manifest = "https://gog-cdn-fastly.gog.com" + repoPath });
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { if (!responses.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes)) throw new Exception("unexpected URL"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request }); }
    static string Hash(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
    static byte[] Zip(byte[] data) { using var output = new MemoryStream(); using (var zip = new ZLibStream(output, CompressionLevel.Fastest, true)) zip.Write(data); return output.ToArray(); }
}
