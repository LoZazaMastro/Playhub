// Synthetic metadata/owned TEMP roots; optional real metadata is read-only, never a game/installer.
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.StoreLibrary;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
async Task Refuses(Func<Task> action, string expected)
{ try { await action(); throw new Exception("expected_refusal:" + expected); } catch (StoreOperationException error) { Check(error.Code == expected, expected + ":" + error.Code); } }
string runtime = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Playhub.StoreHost/Runtime"));
string root = Path.Combine(Path.GetTempPath(), "Playhub-GogModern-Checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    if (args.Length == 2 && args[0] == "--readonly-metadata")
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(args[1]));
        string id = metadata.RootElement.GetProperty("baseProductId").ToString(); StoreCliProvider.ValidateId(id);
        string destination = Path.Combine(root, "owned-metadata-probe"); Directory.CreateDirectory(destination); Guid job = Guid.NewGuid();
        File.WriteAllText(Path.Combine(destination, ".playhub-install-owner"), job.ToString("D"));
        var actual = new StoreCliProvider(StoreKind.Gog, runtime, Path.Combine(root, "state"), Environment.ProcessPath!);
        await actual.VerifyGogModernProjectAsync(id, destination, job, metadata.RootElement, default);
        Console.WriteLine(JsonSerializer.Serialize(new { id, metadataOnly = true, noRetailMutation = true,
            receipt = File.Exists(Path.Combine(root, "state", "gog", "modern-project-owned-" + id + ".json")) }));
        return;
    }
    var good = new Fixture(Path.Combine(root, "good"), runtime);
    StoreOwnedInstallFile[] payload = [new("game.exe", 0, "MD5", "D41D8CD98F00B204E9800998ECF8427E")];
    await Refuses(() => { StoreGogSupportData.Apply(Path.Combine(good.State, "legacy.json"), good.Path, "100", good.Job, "en-US", true, payload, default); return Task.CompletedTask; }, "gog_support_script_missing");
    await good.Verify();
    StoreGogSupportData.Apply(Path.Combine(good.State, "legacy.json"), good.Path, "100", good.Job, "en-US", false, payload, default);
    Check(File.Exists(good.Receipt), "modern_no_action_recipe_gets_private_receipt_before_completion");
    Check(good.Http.Requests == 2, "only_exact_offline_metadata_and_project_chunk_requested");
    await good.Finalize();
    Check(File.Exists(good.Receipt), "actual_finalizer_accepts_verified_modern_build_without_a_legacy_script");
    Check(Directory.GetFiles(good.Path).Select(Path.GetFileName).SequenceEqual(new[] { ".playhub-install-owner" }), "no_game_or_script_created_or_executed");
    using (var receipt = JsonDocument.Parse(File.ReadAllBytes(good.Receipt)))
    {
        Check(receipt.RootElement.GetProperty("OwnerJob").GetGuid() == good.Job && receipt.RootElement.GetProperty("ProductId").GetString() == "100", "receipt_bound_to_exact_job_and_product");
        Check(receipt.RootElement.GetProperty("ProjectHash").GetString()?.Length == 64, "receipt_retains_verified_project_digest");
    }
    foreach (string mutation in new[] { "project-action", "product-action", "task-installer", "unknown-project", "duplicate-product", "identity", "platform", "directory", "payload-tamper", "plain-hash", "offline-name", "no-offline", "owner", "redirect", "oversize" })
    {
        var fixture = new Fixture(Path.Combine(root, mutation), runtime, mutation);
        string expected = mutation switch
        {
            "project-action" or "product-action" or "task-installer" or "unknown-project" or "offline-name" => "gog_support_action_unsupported",
            "payload-tamper" or "plain-hash" => "gog_support_project_changed", "no-offline" => "gog_support_script_missing",
            "owner" => "destination_ownership_lost", "redirect" => "gog_support_project_unavailable", _ => "gog_support_manifest_invalid"
        };
        await Refuses(fixture.Verify, expected);
        Check(!File.Exists(fixture.Receipt) && Directory.GetFiles(fixture.Path).Length == 1, "failed_" + mutation + "_has_no_receipt_or_payload_side_effect");
    }
    Console.WriteLine($"PASS {checks} modern GOG project checks; synthetic HTTP/files only");
}
catch (Exception error) { Console.Error.WriteLine(error is StoreOperationException operation ? operation.Code : error.ToString()); Environment.ExitCode = 1; }

sealed class Fixture
{
    public string Path { get; } public string State { get; } public Guid Job { get; } = Guid.NewGuid();
    public string Receipt => System.IO.Path.Combine(State, "gog", "modern-project-owned-100.json");
    public FakeHttp Http { get; } public JsonDocument Metadata { get; } private StoreCliProvider Provider { get; }
    public Fixture(string root, string runtime, string? mutation = null)
    {
        Path = System.IO.Path.Combine(root, "owned"); State = System.IO.Path.Combine(root, "state"); Directory.CreateDirectory(Path);
        File.WriteAllText(System.IO.Path.Combine(Path, ".playhub-install-owner"), (mutation == "owner" ? Guid.NewGuid() : Job).ToString("D"));
        var project = JsonNode.Parse("""{"project":{"baseProductId":"100","installDirectory":"Synthetic","name":"Synthetic","platform":"windows","scriptInterpreter":true,"products":[{"productId":"100","name":"Synthetic","depots":[],"tasks":[{"type":"FileTask","path":"game.exe","category":"game","url":""}]}]}}""")!;
        if (mutation == "project-action") project["project"]!["actions"] = new JsonArray(new JsonObject { ["install"] = "execute" });
        if (mutation == "product-action") project["project"]!["products"]![0]!["install"] = "execute";
        if (mutation == "task-installer") project["project"]!["products"]![0]!["tasks"]![0]!["type"] = "InstallTask";
        if (mutation == "unknown-project") project["project"]!["futureSetup"] = true;
        if (mutation == "duplicate-product") ((JsonArray)project["project"]!["products"]!).Add(project["project"]!["products"]![0]!.DeepClone());
        if (mutation == "identity") project["project"]!["baseProductId"] = "200";
        if (mutation == "platform") project["project"]!["platform"] = "linux";
        if (mutation == "directory") project["project"]!["installDirectory"] = "Other";
        byte[] plain = Encoding.UTF8.GetBytes(project.ToJsonString()), compressed = Compress(plain);
        string compressedHash = Hash(compressed), plainHash = mutation == "plain-hash" ? new string('A', 32) : Hash(plain);
        byte[] depot = Compress(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { depot = new { items = new[] { new { path = mutation == "offline-name" ? "setup.exe" : "project.json", type = "DepotFile", chunks = new[] { new { compressedMd5 = compressedHash, compressedSize = compressed.Length, md5 = plainHash, size = plain.Length } } } } } })));
        string manifestHash = Hash(depot);
        byte[] payloadDepot = Compress(Encoding.UTF8.GetBytes("""{"depot":{"items":[{"path":"game.exe","type":"DepotFile","md5":"D41D8CD98F00B204E9800998ECF8427E","chunks":[]}]}}"""));
        string payloadHash = Hash(payloadDepot);
        var metadata = JsonNode.Parse(JsonSerializer.Serialize(new { baseProductId = "100", installDirectory = "Synthetic", products = new[] { new { productId = "100" } }, scriptInterpreter = true,
            HGLInstallLanguage = "en-US", depots = new[] { new { productId = "100", manifest = payloadHash, languages = new[] { "*" } } },
            offlineDepot = new { productId = "100", manifest = manifestHash, size = plain.Length, compressedSize = compressed.Length } }))!;
        if (mutation == "no-offline") metadata.AsObject().Remove("offlineDepot");
        Metadata = JsonDocument.Parse(metadata.ToJsonString());
        Http = new(depot, mutation == "payload-tamper" ? new byte[compressed.Length] : mutation == "oversize" ? new byte[1024 * 1024 + 1] : compressed, manifestHash, compressedHash, mutation == "redirect", payloadHash, payloadDepot);
        Provider = new(StoreKind.Gog, runtime, State, Environment.ProcessPath!, new HttpClient(Http), (_, _, _, _) => throw new Exception("no_native_tool"));
    }
    public Task Verify() => Provider.VerifyGogModernProjectAsync("100", Path, Job, Metadata.RootElement, default);
    public Task Finalize()
    {
        string directory = System.IO.Path.Combine(State, "gog", "native", "heroic_gogdl", "manifests"); Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory, "100"), Metadata.RootElement.GetRawText());
        return Provider.FinalizeGogInstallationAsync("100", Path, Job);
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    static byte[] Compress(byte[] data) { using var stream = new MemoryStream(); using (var compressor = new ZLibStream(stream, CompressionMode.Compress, true)) compressor.Write(data); return stream.ToArray(); }
}
sealed class FakeHttp(byte[] depot, byte[] project, string manifestHash, string compressedHash, bool redirect, string payloadHash, byte[] payloadDepot) : HttpMessageHandler
{
    public int Requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++; bool metadata = request.RequestUri!.AbsolutePath == $"/content-system/v2/meta/{manifestHash[..2]}/{manifestHash.Substring(2, 2)}/{manifestHash}";
        bool chunk = request.RequestUri.AbsolutePath == $"/content-system/v2/offline/{compressedHash[..2]}/{compressedHash.Substring(2, 2)}/{compressedHash}";
        bool payload = request.RequestUri.AbsolutePath == $"/content-system/v2/meta/{payloadHash[..2]}/{payloadHash.Substring(2, 2)}/{payloadHash}";
        if (request.RequestUri.Host != "gog-cdn-fastly.gog.com" || !metadata && !chunk && !payload) throw new Exception("unexpected_http_request");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = chunk && redirect ? new(HttpMethod.Get, "https://foreign.invalid/") : request, Content = new ByteArrayContent(metadata ? depot : payload ? payloadDepot : project) });
    }
}
