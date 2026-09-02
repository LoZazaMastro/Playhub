using Playhub.Models;
using Playhub.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Contains("--print-bundled-manifest"))
{
    Console.WriteLine(BundledManifest.Assemble().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (args.Length == 2 && args[0] == "--validate-manifest")
{
    var manifest = RemotePluginCatalogService.Parse(File.ReadAllBytes(args[1]));
    Console.WriteLine($"Valid schema {manifest.SchemaVersion}, revision {manifest.CatalogRevision}, {manifest.Plugins.Count} plugins.");
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("canonical manifest preserves every configured record", Canonical),
    ("200 + one request + durable fresh cache", SuccessAndCache),
    ("expired cache + offline + retry throttling", Offline),
    ("HTTP failures preserve local and last-good bytes", HttpFailures),
    ("malformed JSON/schema/identity/source/URLs", InvalidDocuments),
    ("declared and streaming body limits", SizeLimits),
    ("schema version and monotonic catalog revision", Versions),
    ("merge, Decky updates and explicit deactivation", MergeRules),
    ("concurrent loads share one fetch", Concurrent),
    ("cache-only baseline never waits for remote refresh", CacheOnly),
    ("cache and fetch work stay off the UI context", OffUiContext),
    ("actual catalog mapping: addition/update/assets/install/offline", IntegrationTests.Mapping),
    ("actual UI hook: first render/revision/operation guards/review isolation", Playhub.MainWindow.RunCatalogUiTests),
    ("timeout, caller cancellation and cache write failure", CancellationAndCacheFailure)
};
foreach (var test in tests)
{
    await test.Run();
    Console.WriteLine("PASS " + test.Name);
}
Console.WriteLine($"PASS {tests.Length} test groups (fake HTTP only).");

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static RemotePluginCatalogEntry Entry(string id = "demo") => new()
{
    Name = id, InstallFolder = id, Repository = "LoZazaMastro/" + id,
    RepositoryUrl = "https://github.com/LoZazaMastro/" + id, Author = "LoZazaMastro",
    Version = "1.0.0", Category = "Playhub", ShortDescription = "Short", LongDescription = "Long",
    CatalogStatus = "playhub", CatalogSource = "playhub"
};

static RemotePluginCatalog Catalog(long revision = 1, params RemotePluginCatalogEntry[] entries) => new()
{
    CatalogRevision = revision, Plugins = entries.Length == 0 ? new[] { Entry() } : entries
};

static byte[] Bytes(RemotePluginCatalog catalog) => JsonSerializer.SerializeToUtf8Bytes(catalog,
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

static RemotePluginCatalog Local() => Catalog(0, Entry("bundled"));

static Task Canonical()
{
    var path = Path.Combine(BundledManifest.Root, "catalog/plugins.json");
    var bytes = File.ReadAllBytes(path);
    var parsed = RemotePluginCatalogService.Parse(bytes);
    Check(JsonNode.DeepEquals(JsonNode.Parse(bytes), BundledManifest.Assemble()), "Canonical data differs from configured source.");
    Check(parsed.Plugins.Count == 173, "Expected 12 built-ins and all 161 external entries.");
    Check(parsed.Plugins.Count(p => p.CatalogSource == "playhub") == 12, "Playhub provenance lost.");
    Check(parsed.Plugins.Count(p => p.CatalogSource == "decky-store") == 108, "Decky provenance lost.");
    Check(parsed.Plugins.Count(p => p.CatalogSource == "outside-store") == 53, "GitHub provenance lost.");
    Check(parsed.Plugins.Count(p => p.RepositoryUrl.StartsWith("https://gitlab.com/")) == 3, "GitLab sources lost.");
    return Task.CompletedTask;
}

static async Task SuccessAndCache()
{
    using var fixture = new Fixture();
    var responseBytes = Bytes(Catalog());
    fixture.Handler.Respond = (_, _) => Task.FromResult(Ok(responseBytes));
    var result = await fixture.Service.LoadAsync(Local());
    Check(result.Origin == "remote" && result.Catalog.Plugins.Count == 2, "Successful merge failed.");
    Check(File.ReadAllBytes(fixture.CachePath).SequenceEqual(responseBytes), "Cache differs from accepted bytes.");
    for (var i = 0; i < 5; i++) await fixture.Service.LoadAsync(Local());
    Check(fixture.Handler.Calls == 1, "Fresh catalog refetched.");
    File.SetLastWriteTimeUtc(fixture.CachePath, fixture.Now.UtcDateTime);
    var restart = fixture.NewService();
    Check((await restart.LoadAsync(Local())).Origin == "cache", "Restart did not use cache.");
    Check(fixture.Handler.Calls == 1, "Fresh persisted cache used network.");
    Check(fixture.Handler.LastUri?.AbsoluteUri == RemotePluginCatalogService.CatalogUrl, "Unexpected endpoint.");
}

static async Task Offline()
{
    using var fixture = new Fixture();
    File.WriteAllBytes(fixture.CachePath, Bytes(Catalog()));
    File.SetLastWriteTimeUtc(fixture.CachePath, fixture.Now.AddDays(-1).UtcDateTime);
    fixture.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
    var result = await fixture.Service.LoadAsync(Local());
    Check(result.Origin == "cache" && result.Catalog.Plugins.Count == 2, "Offline fallback missing.");
    await fixture.Service.LoadAsync(Local());
    Check(fixture.Handler.Calls == 1, "Failure retry was not throttled.");
    fixture.Now += RemotePluginCatalogService.RetryInterval;
    await fixture.Service.LoadAsync(Local());
    Check(fixture.Handler.Calls == 2, "Retry did not recover after interval.");
    File.WriteAllText(fixture.CachePath, "{");
    var local = Local();
    Check(ReferenceEquals((await fixture.NewService().LoadAsync(local)).Catalog, local), "Corrupt cache replaced local.");
}

static async Task HttpFailures()
{
    foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.InternalServerError,
        HttpStatusCode.TooManyRequests, HttpStatusCode.Redirect, HttpStatusCode.PartialContent, HttpStatusCode.NoContent })
    {
        using var fixture = new Fixture();
        fixture.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Headers = { Location = new Uri("http://127.0.0.1/private") }
        });
        var local = Local();
        Check(ReferenceEquals((await fixture.Service.LoadAsync(local)).Catalog, local), "HTTP failure replaced local.");
        Check(!File.Exists(fixture.CachePath) && fixture.Handler.Calls == 1, "Failure persisted or redirect followed.");
    }
    using var good = new Fixture();
    await good.Service.LoadAsync(Local());
    var cached = File.ReadAllBytes(good.CachePath);
    good.Now += RemotePluginCatalogService.RefreshInterval;
    good.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    var fallback = await good.Service.LoadAsync(Local());
    Check(fallback.Catalog.Plugins.Count == 2 && File.ReadAllBytes(good.CachePath).SequenceEqual(cached), "Last good lost.");
}

static async Task InvalidDocuments()
{
    var mutations = new Action<JsonObject>[]
    {
        root => root["schemaVersion"] = 99,
        root => root.Remove("schemaVersion"),
        root => root["schemaVersion"] = "3",
        root => root["catalogRevision"] = "1",
        root => root["plugins"] = null,
        root => root["plugins"] = new JsonArray(),
        root => root["plugins"]![0] = null,
        root => root["officialDeckyCatalogUrl"] = "https://evil.example/plugins",
        root => root["unexpected"] = true,
        root => Plugin(root)["repository"] = "owner/../escape",
        root => Plugin(root)["installFolder"] = "../escape",
        root => Plugin(root)["installFolder"] = "CON.txt",
        root => Plugin(root)["installFolder"] = "CON .txt",
        root => Plugin(root)["installFolder"] = "directory:stream",
        root => Plugin(root)["installFolder"] = "gaming-mode",
        root => Plugin(root)["name"] = "Playhub Gaming Mode",
        root => Plugin(root)["name"] = "Varta",
        root => Plugin(root)["aliases"] = new JsonArray("owner/VartaPlugin"),
        root => Plugin(root)["aliases"] = new JsonArray("LoZazaMastro/GamingMode"),
        root => Plugin(root)["aliases"] = new JsonArray("C:\\gaming-mode"),
        root => Plugin(root)["aliases"] = null,
        root => Plugin(root)["aliases"] = new JsonArray((JsonNode?)null),
        root => Plugin(root)["author"] = null,
        root => Plugin(root)["version"] = " ",
        root => Plugin(root)["version"] = "",
        root => Plugin(root)["repositoryUrl"] = "https://github.com/other/repo",
        root => Plugin(root)["repositoryUrl"] = "http://github.com/LoZazaMastro/demo",
        root => Plugin(root)["repositoryUrl"] = "https://user@github.com/LoZazaMastro/demo",
        root => Plugin(root)["repositoryUrl"] = "https://github.com:444/LoZazaMastro/demo",
        root => Plugin(root)["coverUrl"] = "file:///C:/private.png",
        root => Plugin(root)["coverUrl"] = "https://127.0.0.1/private.png",
        root => Plugin(root)["coverUrl"] = "https://github.com.evil.example/image.png",
        root => Plugin(root)["coverUrl"] = "https://raw.githubusercontent.com/owner/repo/../private",
        root => Plugin(root)["coverUrl"] = "https://raw.githubusercontent.com/owner/repo/%0d%0a",
        root => Plugin(root)["catalogSource"] = "installed",
        root => Plugin(root)["catalogStatus"] = "decky",
        root => Plugin(root)["catalogPluginId"] = -1,
        root => Plugin(root)["repository"] = "other/demo",
        root => root["plugins"]!.AsArray().Add(Plugin(root).DeepClone()),
        root => Plugin(root)["longDescription"] = new string('x', 32769),
        root => { Plugin(root)["catalogReleaseUrl"] = "https://github.com/other/demo/releases/download/v1/demo.zip";
            Plugin(root)["releaseAssetName"] = "demo.zip"; },
        root => { Plugin(root)["catalogReleaseUrl"] = "https://github.com/LoZazaMastro/demo/releases/download/v1/wrong.zip";
            Plugin(root)["releaseAssetName"] = "demo.zip"; }
    };
    var invalid = new List<byte[]> { Encoding.UTF8.GetBytes("{"), Encoding.UTF8.GetBytes("[]"),
        Encoding.UTF8.GetBytes("{\"schemaVersion\":3,\"schemaVersion\":3,\"catalogRevision\":1,\"plugins\":[]}") };
    foreach (var mutation in mutations)
    {
        var root = JsonNode.Parse(Bytes(Catalog()))!.AsObject();
        mutation(root);
        invalid.Add(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
    foreach (var bytes in invalid)
    {
        using var fixture = new Fixture();
        await fixture.Service.LoadAsync(Local());
        var cached = File.ReadAllBytes(fixture.CachePath);
        fixture.Now += RemotePluginCatalogService.RefreshInterval;
        fixture.Handler.Respond = (_, _) => Task.FromResult(Ok(bytes));
        var result = await fixture.Service.LoadAsync(Local());
        Check(result.Error is not null && result.Catalog.Plugins.Count == 2, "Malformed data accepted: " + Encoding.UTF8.GetString(bytes));
        Check(File.ReadAllBytes(fixture.CachePath).SequenceEqual(cached), "Malformed data overwrote cache.");
    }
    static JsonObject Plugin(JsonObject root) => root["plugins"]![0]!.AsObject();
}

static async Task SizeLimits()
{
    using var declared = new Fixture();
    var counted = new CountingStream(RemotePluginCatalogService.MaxDocumentBytes + 100);
    declared.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(counted) { Headers = { ContentLength = RemotePluginCatalogService.MaxDocumentBytes + 1 } }
    });
    var local = Local();
    Check(ReferenceEquals((await declared.Service.LoadAsync(local)).Catalog, local) && counted.BytesRead == 0,
        "Oversized declared body was read.");
    using var streamed = new Fixture();
    var streaming = new CountingStream(RemotePluginCatalogService.MaxDocumentBytes + 100);
    streamed.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(streaming) });
    Check(ReferenceEquals((await streamed.Service.LoadAsync(local)).Catalog, local), "Oversized streamed body accepted.");
    Check(streaming.BytesRead == RemotePluginCatalogService.MaxDocumentBytes + 1, "Stream was not bounded.");
    var exact = Bytes(Catalog()).Concat(Enumerable.Repeat((byte)' ', RemotePluginCatalogService.MaxDocumentBytes - Bytes(Catalog()).Length)).ToArray();
    Check(RemotePluginCatalogService.Parse(exact).Plugins.Count == 1, "Exact byte limit rejected.");
    var tooMany = Catalog(1, Enumerable.Range(0, 1001).Select(i => Entry("p" + i)).ToArray());
    try { RemotePluginCatalogService.Parse(Bytes(tooMany)); throw new Exception("Plugin count limit missing."); }
    catch (InvalidDataException) { }
}

static async Task Versions()
{
    using var fixture = new Fixture();
    fixture.Handler.Respond = (_, _) => Task.FromResult(Ok(Bytes(Catalog(5))));
    await fixture.Service.LoadAsync(Local());
    fixture.Now += RemotePluginCatalogService.RefreshInterval;
    fixture.Handler.Respond = (_, _) => Task.FromResult(Ok(Bytes(Catalog(4))));
    var result = await fixture.Service.LoadAsync(Local());
    Check(result.Catalog.CatalogRevision == 5 && result.Error is not null, "Revision rollback accepted.");
    var newerBundle = Catalog(6, Entry("newer-bundled"));
    Check(ReferenceEquals((await fixture.Service.LoadAsync(newerBundle)).Catalog, newerBundle), "Older cache replaced newer bundle.");
    fixture.Now += RemotePluginCatalogService.RetryInterval;
    fixture.Handler.Respond = (_, _) => Task.FromResult(Ok(Bytes(Catalog(7))));
    Check((await fixture.Service.LoadAsync(newerBundle)).Catalog.CatalogRevision == 7, "New revision did not recover.");
}

static Task MergeRules()
{
    var entry = Entry() with { IconGlyph = "icon", CoverUrl = "https://github.com/owner/repo/image.png", Aliases = new[] { "old-name" } };
    var local = Catalog(0, entry, Entry("keep"));
    var remote = Catalog(1, Entry() with { Name = "Renamed", Aliases = new[] { "new-name" } }, Entry("new"), Entry("inactive") with { Active = false });
    var merged = RemotePluginCatalogService.Merge(local, remote);
    Check(merged.Plugins.Count == 3 && merged.Plugins[0].Name == "Renamed" && merged.Plugins[1].Name == "keep", "Additive merge/order failed.");
    Check(merged.Plugins[0].CoverUrl == entry.CoverUrl && merged.Plugins[0].IconGlyph == "icon" && merged.Plugins[0].Aliases.Count == 2, "Optional metadata erased.");
    Check(local.Plugins[0].Name == "demo", "Caller local object mutated.");
    var decky = Entry() with { CatalogSource = "decky-store", CatalogStatus = "decky", CatalogPluginId = 42, Version = "3.0.0" };
    var updatedDecky = decky with { Version = "4.0.0", CatalogReleaseUrl = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/new.zip", ReleaseAssetName = "new.zip", LongDescription = "Updated" };
    var updated = RemotePluginCatalogService.Merge(Catalog(0, decky), Catalog(1, updatedDecky)).Plugins[0];
    Check(updated.Version == "4.0.0" && updated.CatalogReleaseUrl == updatedDecky.CatalogReleaseUrl && updated.LongDescription == "Updated",
        "Trusted Decky version/release/description update ignored.");
    Check(RemotePluginCatalogService.Merge(local, Catalog(1, Entry() with { Active = false })).Plugins.Single().Name == "keep",
        "Explicit deactivation did not remove existing definition.");
    Check(RemotePluginCatalogService.Merge(Catalog(0, decky), Catalog(1, decky with { Active = false })).Plugins.Count == 0,
        "Decky deactivation ignored.");
    foreach (var collision in new[] { Entry() with { CatalogSource = "outside-store" }, Entry() with { InstallFolder = "renamed" }, Entry("new") with { InstallFolder = "demo" } })
    {
        try { RemotePluginCatalogService.Merge(local, Catalog(1, collision)); throw new Exception("Identity conflict accepted."); }
        catch (InvalidDataException) { }
    }
    return Task.CompletedTask;
}

static async Task CacheOnly()
{
    using var fixture = new Fixture();
    var local = Local();
    Check(ReferenceEquals((await fixture.Service.LoadCachedAsync(local)).Catalog, local) && fixture.Handler.Calls == 0,
        "Cache-only startup fetched remote.");
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Handler.Respond = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return Ok(Bytes(Catalog())); };
    var refreshing = fixture.Service.RefreshAsync(local);
    await entered.Task;
    try
    {
        var baseline = await fixture.Service.LoadCachedAsync(local).WaitAsync(TimeSpan.FromSeconds(1));
        Check(ReferenceEquals(baseline.Catalog, local), "In-flight refresh blocked the local baseline.");
    }
    finally { release.TrySetResult(); }
    await refreshing;
    Check((await fixture.Service.LoadCachedAsync(local)).Catalog.Plugins.Count == 2, "Accepted refresh not visible to cached baseline.");

    // A new process must apply the cached update and tombstone without a network call.
    var decky = Entry("decky") with { Repository = "owner/decky", RepositoryUrl = "https://github.com/owner/decky",
        CatalogSource = "decky-store", CatalogStatus = "decky", CatalogPluginId = 1,
        ReleaseAssetName = "old.zip", CatalogReleaseUrl = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/old.zip" };
    var trusted = Catalog(2, decky with { Version = "2.0.0", ReleaseAssetName = "new.zip",
        CatalogReleaseUrl = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/new.zip" }, Entry("bundled") with { Active = false });
    File.WriteAllBytes(fixture.CachePath, Bytes(trusted));
    File.SetLastWriteTimeUtc(fixture.CachePath, fixture.Now.AddDays(-1).UtcDateTime);
    var count = fixture.Handler.Calls;
    var cached = await fixture.NewService().LoadCachedAsync(Catalog(0, decky, Entry("bundled")));
    Check(cached.Origin == "cache" && cached.Catalog.Plugins.Single().Version == "2.0.0" && fixture.Handler.Calls == count,
        "Offline cache lost Decky update/tombstone or fetched network.");
}

static async Task Concurrent()
{
    using var fixture = new Fixture();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Handler.Respond = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return Ok(Bytes(Catalog())); };
    var tasks = Enumerable.Range(0, 16).Select(_ => fixture.Service.LoadAsync(Local())).ToArray();
    await entered.Task;
    release.SetResult();
    await Task.WhenAll(tasks);
    Check(fixture.Handler.Calls == 1 && tasks.All(t => t.Result.Catalog.Plugins.Count == 2), "Concurrent fetches were duplicated.");
}

static async Task CancellationAndCacheFailure()
{
    using var timeout = new Fixture(TimeSpan.FromMilliseconds(30));
    timeout.Handler.Respond = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Ok(Bytes(Catalog())); };
    var local = Local();
    Check(ReferenceEquals((await timeout.Service.LoadAsync(local)).Catalog, local), "Timeout lost local fallback.");
    using var cancel = new Fixture();
    using var cts = new CancellationTokenSource();
    cancel.Handler.Respond = (_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(Ok(Bytes(Catalog()))); };
    try { await cancel.Service.LoadAsync(local, cts.Token); throw new Exception("Caller cancellation swallowed."); }
    catch (OperationCanceledException) { }
    Check(!File.Exists(cancel.CachePath), "Cancelled fetch cached.");
    using var failure = new Fixture();
    Directory.CreateDirectory(failure.CachePath);
    var result = await failure.Service.LoadAsync(local);
    Check(result.Origin == "remote" && result.Catalog.Plugins.Count == 2 && result.Error?.StartsWith("Cache write:") == true,
        "Cache failure discarded valid in-memory result.");
}

static async Task OffUiContext()
{
    using var fixture = new Fixture();
    var context = new SynchronizationContext();
    var previous = SynchronizationContext.Current;
    var start = fixture.Handler.Respond;
    fixture.Handler.Respond = (request, token) =>
    {
        Check(SynchronizationContext.Current != context, "Fetch ran on caller context.");
        return start(request, token);
    };
    Task<RemotePluginCatalogResult> task;
    try
    {
        SynchronizationContext.SetSynchronizationContext(context);
        task = fixture.Service.LoadAsync(Local());
    }
    finally { SynchronizationContext.SetSynchronizationContext(previous); }
    Check((await task).Origin == "remote", "Worker fetch failed.");
}

static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

sealed class Fixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Playhub.RemoteCatalog.Tests", Guid.NewGuid().ToString("N"));
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    public FakeHandler Handler { get; } = new();
    public string CachePath => Path.Combine(_directory, "catalog.json");
    public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public RemotePluginCatalogService Service { get; }
    public Fixture(TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(_directory);
        _client = new HttpClient(Handler);
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"schemaVersion\":3,\"catalogRevision\":1,\"plugins\":[{\"name\":\"remote\",\"installFolder\":\"remote\",\"author\":\"LoZazaMastro\",\"repository\":\"LoZazaMastro/remote\",\"version\":\"1.0.0\",\"category\":\"Playhub\",\"shortDescription\":\"Short\",\"longDescription\":\"Long\",\"catalogSource\":\"playhub\",\"catalogStatus\":\"playhub\"}]}")
        });
        Service = NewService();
    }
    public RemotePluginCatalogService NewService() => new(_client, CachePath, () => Now, _timeout);
    public void Dispose() { _client.Dispose(); Directory.Delete(_directory, recursive: true); }
}

sealed class FakeHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = null!;
    public int Calls;
    public Uri? LastUri;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        LastUri = request.RequestUri;
        return Respond(request, cancellationToken);
    }
}

sealed class CountingStream(int length) : Stream
{
    public int BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        count = Math.Min(count, length - BytesRead);
        Array.Fill(buffer, (byte)' ', offset, count);
        BytesRead += count;
        return count;
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
