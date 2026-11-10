using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.Services;

const string repository = "https://api.github.com/repos/SteamDeckHomebrew/decky-loader/releases";
const string download = "https://github.com/SteamDeckHomebrew/decky-loader/releases/download";
const string tag = "v3.3.1";
const string redirected = "https://release-assets.githubusercontent.com/fixture/verified.exe";
int passed = 0;

// Opt-in verification of the real transport and installed hashes. Never starts,
// stops or writes to a loader or the Steam client.
bool forcePublicWebsite = args.SequenceEqual(new[] { "--probe-official-web" });
if (args.SequenceEqual(new[] { "--probe-official" }) || forcePublicWebsite)
{
    var transport = new HttpClientHandler { AllowAutoRedirect = false };
    using var http = new HttpClient(forcePublicWebsite ? new ThrottledApiProbe { InnerHandler = transport } : transport);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub-DeckyRepair-Probe/1.0");
    var client = new DeckyWindowsReleaseClient(http, RuntimeInformation.OSArchitecture);
    var latest = await client.GetAsync();
    var explicitRelease = await client.GetAsync(latest.Summary.Tag);
    var list = await client.ListAsync();
    if (!list.Any(release => release.Tag == latest.Summary.Tag) ||
        !latest.Assets.Select(asset => asset.Digest).SequenceEqual(explicitRelease.Assets.Select(asset => asset.Digest)))
        throw new InvalidOperationException("Official default, explicit tag and release list do not agree.");
    await Task.WhenAll(latest.Assets.Select(client.DownloadAsync));
    Console.WriteLine($"Official release {latest.Summary.Tag}: verified default, explicit tag, list and both Windows assets" +
        (forcePublicWebsite ? " through the public website after a simulated API throttle." : "."));
    string services = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "services");
    Console.WriteLine($"Installed pair matches: {DeckyFileUpgrade.Matches(services, latest.Assets)}");
    int? owner = Playhub.Shared.DeckyStartupGuard.ListenerOwner(1337);
    bool owned = owner is int pid && latest.Assets.Any(asset => Playhub.DeckyHost.DeckyProcessTree.FindRoot(pid,
        DeckyFileUpgrade.GetLoaderPath(services, asset.InstallName)) is not null);
    Console.WriteLine($"Owned TCP listener found: {owned}");
    return;
}

// Use our own apphost, padded to the minimum artifact size, for a real PE fixture.
var executable = await File.ReadAllBytesAsync(Environment.ProcessPath!);
Array.Resize(ref executable, Math.Max(510_000, executable.Length));
string digest = Convert.ToHexString(SHA256.HashData(executable));

object Asset(string name, string release = tag, string? url = null, string? sha = null, int? size = null) => new
{
    name, state = "uploaded", digest = sha ?? "sha256:" + digest, size = size ?? executable.Length,
    browser_download_url = url ?? $"{download}/{release}/{name}"
};
object Metadata(string release = tag, bool preview = false, object[]? assets = null) => new
{
    tag_name = release, draft = false, prerelease = preview, published_at = "2026-10-27T03:26:19Z",
    assets = assets ?? [Asset("PluginLoader-win.exe", release), Asset("PluginLoader-win-noconsole.exe", release)]
};
static HttpResponseMessage Json(object obj) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(obj)) };
static HttpResponseMessage Binary(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
static HttpResponseMessage Redirect(string uri)
{
    var response = new HttpResponseMessage(HttpStatusCode.Found);
    response.Headers.Location = new Uri(uri);
    return response;
}

using var success = new HttpClient(new Fixture(url =>
{
    if (url == repository + "/latest" || url == repository + "/tags/" + tag) return Json(Metadata());
    if (url == repository + "?per_page=30")
        return Json(new[] { Metadata("v3.2.9"), Metadata(preview: true), Metadata(), Metadata("v3.3.0", assets: []) });
    if (url == download + "/" + tag + "/PluginLoader-win.exe") return Redirect(redirected);
    if (url == redirected || url == download + "/" + tag + "/PluginLoader-win-noconsole.exe") return Binary(executable);
    throw new Exception("Unexpected URL: " + url);
}));
var releaseClient = new DeckyWindowsReleaseClient(success, Architecture.X64);
var release = await releaseClient.GetAsync();
Check(release.Summary.Tag == tag && release.Console.InstallName == DeckyFileUpgrade.ConsoleName &&
    release.Background.InstallName == DeckyFileUpgrade.BackgroundName, "Stable release maps official assets to the installed pair");
Check((await releaseClient.ListAsync()).Select(item => item.Tag).SequenceEqual(new[] { tag, "v3.2.9" }),
    "Chooser excludes previews and missing assets, and orders compatible releases by version");
Check((await releaseClient.GetAsync(tag)).Summary.Tag == tag, "Selected release resolves only its exact tag");
Check((await releaseClient.DownloadAsync(release.Console)).SequenceEqual(executable) &&
    (await releaseClient.DownloadAsync(release.Background)).SequenceEqual(executable), "GitHub asset redirect, length, digest and PE are verified");
await Reject<ArgumentException>(() => releaseClient.GetAsync("../latest"), "Reject invalid selected tag before HTTP");

const string linuxOnlyTag = "v3.4.0";
var fallbackRequests = new List<string>();
using (var fallbackHttp = new HttpClient(new Fixture(url =>
{
    fallbackRequests.Add(url);
    if (url == repository + "/latest" || url == repository + "/tags/" + linuxOnlyTag)
        return Json(Metadata(linuxOnlyTag, assets: [Asset("PluginLoader-linux", linuxOnlyTag)]));
    if (url == repository + "?per_page=30")
        return Json(new[] { Metadata("v3.2.9"), Metadata("v3.3.2", preview: true), Metadata(), Metadata(linuxOnlyTag, assets: []) });
    throw new Exception("Unexpected URL: " + url);
})))
{
    var client = new DeckyWindowsReleaseClient(fallbackHttp, Architecture.X64);
    var compatible = await client.GetAsync();
    Check(compatible.Summary.Tag == tag && compatible.Assets.All(asset => asset.Url.AbsolutePath.Contains("/" + tag + "/")) &&
        fallbackRequests.SequenceEqual(new[] { repository + "/latest", repository + "?per_page=30" }),
        "Default install falls back to the newest stable release with a verified Windows pair");
    fallbackRequests.Clear();
    await Reject<InvalidDataException>(() => client.GetAsync(linuxOnlyTag), "Exact tag with no Windows pair is rejected");
    Check(fallbackRequests.SequenceEqual(new[] { repository + "/tags/" + linuxOnlyTag }),
        "Exact tag failure never requests or selects a fallback release");
}

using (var malformedHttp = new HttpClient(new Fixture(url => url == repository + "/latest"
    ? Json(new { tag_name = tag, assets = Array.Empty<object>() })
    : throw new Exception("Malformed latest must not trigger fallback: " + url))))
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(malformedHttp, Architecture.X64).GetAsync(),
        "Malformed latest metadata never triggers fallback");

const string web = "https://github.com/SteamDeckHomebrew/decky-loader/releases";
const string webPath = "/SteamDeckHomebrew/decky-loader/releases";
string WebSummary(string release, bool preview = false) =>
    $"""released this <relative-time datetime="2026-10-27T04:26:19Z"></relative-time><include-fragment src="{web}/expanded_assets/{release}"></include-fragment>""" +
    (preview ? """<span class="Label Label--warning">Pre-release</span>""" : "");
string TagPage(string release) => $"""<meta property="og:url" content="{webPath}/tag/{release}" />""" + WebSummary(release);
string Section(string release, bool preview = false) =>
    $"""<section data-release-anchor="release-{release}">{WebSummary(release, preview)}</section>""";
string AssetRow(string release, string filename, bool includeDigest = true, string? path = null) =>
    $"""<li><a href="{path ?? webPath + "/download/" + release + "/" + filename}">{filename}</a>""" +
    (includeDigest ? $"""<clipboard-copy aria-label="Copy to clipboard digest for {filename}" value="sha256:{digest}"></clipboard-copy>""" : "") +
    "<span>14.9 MB</span></li>";
string WebAssets(string release) =>
    AssetRow(release, "PluginLoader-win.exe") + AssetRow(release, "PluginLoader-win-noconsole.exe");
static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };

var webRequests = new List<string>();
using (var websiteHttp = new HttpClient(new Fixture(url =>
{
    webRequests.Add(url);
    if (url.StartsWith(repository, StringComparison.Ordinal)) return ThrottledApiProbe.Response();
    if (url == web + "/latest") return Redirect(web + "/tag/" + linuxOnlyTag);
    if (url == web + "/tag/" + linuxOnlyTag) return Html(TagPage(linuxOnlyTag));
    if (url == web + "/tag/" + tag) return Html(TagPage(tag));
    if (url == web) return Html(Section("v3.2.9") + Section(linuxOnlyTag) + Section("v3.3.2", preview: true) + Section(tag));
    if (url == web + "/expanded_assets/" + linuxOnlyTag) return Html(AssetRow(linuxOnlyTag, "PluginLoader-linux"));
    if (url == web + "/expanded_assets/" + tag || url == web + "/expanded_assets/v3.2.9")
        return Html(WebAssets(url.Split('/').Last()));
    if (url == download + "/" + tag + "/PluginLoader-win.exe") return Redirect(redirected);
    if (url == redirected) return Binary(executable);
    if (url == download + "/" + tag + "/PluginLoader-win-noconsole.exe")
        return new(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(executable)) };
    throw new Exception("Unexpected URL: " + url);
})))
{
    var client = new DeckyWindowsReleaseClient(websiteHttp, Architecture.X64);
    var recovered = await client.GetAsync();
    Check(recovered.Summary.Tag == tag && recovered.Assets.All(asset => asset.Size == 0 && asset.Digest == digest),
        "API throttle uses public latest and then the newest compatible stable website release");
    Check((await client.ListAsync()).Select(item => item.Tag).SequenceEqual(new[] { tag, "v3.2.9" }) &&
        !webRequests.Contains(web + "/expanded_assets/v3.3.2"),
        "Throttled chooser lists verified stable website pairs and ignores stable-named prereleases");
    webRequests.Clear();
    var selected = await client.GetAsync(tag);
    Check(selected.Summary.Tag == tag && webRequests.SequenceEqual(new[]
    {
        repository + "/tags/" + tag, web + "/tag/" + tag, web + "/expanded_assets/" + tag
    }), "Throttled explicit selection reads only the requested tag and its assets");
    Check((await client.DownloadAsync(selected.Console)).SequenceEqual(executable) &&
        (await client.DownloadAsync(selected.Background)).SequenceEqual(executable),
        "Website assets verify the same digest and PE with exact or unknown Content-Length");
    webRequests.Clear();
    await Reject<InvalidDataException>(() => client.GetAsync(linuxOnlyTag), "Website exact tag without a Windows pair is rejected");
    Check(!webRequests.Contains(web), "Website exact tag never enumerates a substitute release");
}

using (var badRedirect = new HttpClient(new Fixture(url => url == repository + "/latest" ? ThrottledApiProbe.Response()
    : url == web + "/latest" ? Redirect("https://example.com" + webPath + "/tag/" + tag) : throw new Exception("Followed foreign release host"))))
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(badRedirect, Architecture.X64).GetAsync(),
        "Website latest rejects redirects to a foreign host");
using (var wrongTag = new HttpClient(new Fixture(url => url == repository + "/tags/" + tag ? ThrottledApiProbe.Response()
    : url == web + "/tag/" + tag ? Html(TagPage("v3.2.9")) : throw new Exception("Read assets for a different release"))))
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(wrongTag, Architecture.X64).GetAsync(tag),
        "Website explicit tag rejects a different canonical release");
foreach (var badAsset in new[]
{
    AssetRow(tag, "PluginLoader-win.exe", includeDigest: false),
    AssetRow(tag, "PluginLoader-win.exe", path: "https://example.com/PluginLoader-win.exe"),
    AssetRow(tag, "PluginLoader-win.exe", path: webPath + "/download/v3.2.9/PluginLoader-win.exe")
})
{
    using var badAssetHttp = new HttpClient(new Fixture(url => url == repository + "/tags/" + tag ? ThrottledApiProbe.Response()
        : url == web + "/tag/" + tag ? Html(TagPage(tag))
        : url == web + "/expanded_assets/" + tag ? Html(badAsset + AssetRow(tag, "PluginLoader-win-noconsole.exe"))
        : throw new Exception("Accepted unverified website asset")));
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(badAssetHttp, Architecture.X64).GetAsync(tag),
        "Website asset rejects a missing digest, foreign host or mismatched release path");
}

using var arm = new HttpClient(new Fixture(_ => Json(Metadata(assets:
    [Asset("PluginLoader-win.exe"), Asset("PluginLoader-win-arm64.exe"), Asset("PluginLoader-win-arm64-noconsole.exe")]))));
Check((await new DeckyWindowsReleaseClient(arm, Architecture.Arm64).GetAsync()).Console.Url.AbsolutePath.EndsWith("-arm64.exe"),
    "ARM64 selects the native ARM64 pair, not x64");

foreach (var invalid in new[]
{
    Metadata(preview: true), Metadata("v3.3.1-rc.1"), Metadata(assets: []),
    Metadata(assets: [Asset("PluginLoader-win.exe"), Asset("PluginLoader-win.exe"), Asset("PluginLoader-win-noconsole.exe")]),
    Metadata(assets: [Asset("PluginLoader-win.exe", sha: ""), Asset("PluginLoader-win-noconsole.exe")]),
    Metadata(assets: [Asset("PluginLoader-win.exe", size: int.MaxValue), Asset("PluginLoader-win-noconsole.exe")]),
    Metadata(assets: [Asset("PluginLoader-win.exe", url: "https://github.com/another/decky-loader/releases/download/v3.3.1/PluginLoader-win.exe"), Asset("PluginLoader-win-noconsole.exe")]),
    Metadata(assets: [Asset("PluginLoader-win.exe", "v3.2.9"), Asset("PluginLoader-win-noconsole.exe")]),
})
{
    using var client = new HttpClient(new Fixture(_ => Json(invalid)));
    await Reject<InvalidDataException>(() => new DeckyInstallerService(client).InstallOrUpdateLatestAsync(),
        "Invalid upstream metadata cannot reach installation");
}
using (var client = new HttpClient(new Fixture(_ => Json(Metadata("v3.2.9")))))
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(client, Architecture.X64).GetAsync(tag),
        "Selected release cannot silently fall back to an older tag");
using (var client = new HttpClient(new Fixture(_ => new(HttpStatusCode.NotFound))))
    await Reject<HttpRequestException>(() => new DeckyInstallerService(client).InstallOrUpdateLatestAsync(),
        "Unavailable official release cannot reach installation");
using (var client = new HttpClient(new Fixture(_ => Redirect("https://example.com/loader.exe"))))
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(client, Architecture.X64).DownloadAsync(release.Console),
        "Asset redirect to an unrelated host is rejected");
using (var client = new HttpClient(new Fixture(_ => Binary(executable[..^1]))))
    await Reject<InvalidDataException>(() => new DeckyWindowsReleaseClient(client, Architecture.X64).DownloadAsync(release.Console),
        "Truncated asset is rejected");
using (var client = new HttpClient(new Fixture(_ => Binary(new byte[executable.Length]))))
{
    var verifier = new DeckyWindowsReleaseClient(client, Architecture.X64);
    await Reject<InvalidDataException>(() => verifier.DownloadAsync(release.Console), "Wrong SHA-256 is rejected");
    var nonPe = release.Console with { Digest = Convert.ToHexString(SHA256.HashData(new byte[executable.Length])) };
    await Reject<InvalidDataException>(() => verifier.DownloadAsync(nonPe), "Matching digest is not enough for a non-PE asset");
}

var folder = Directory.CreateTempSubdirectory("Playhub-DeckyRepair-");
try
{
    string services = Directory.CreateDirectory(Path.Combine(folder.FullName, "services")).FullName;
    string plugin = Path.Combine(folder.FullName, "plugin-settings.json");
    File.WriteAllText(plugin, "settings to preserve");
    var payload = release.Assets.ToDictionary(asset => asset, _ => executable);
    string[] paths = release.Assets.Select(asset => Path.Combine(services, asset.InstallName)).ToArray();
    foreach (var path in paths) File.WriteAllText(path, "original loader");
    var events = new List<string>();
    void Prepare() => events.Add("prepare");
    bool foreignListener = false;
    void Stop()
    {
        if (foreignListener) throw new IOException("A foreign listener occupies TCP 1337.");
        if (events.Count == 1)
        {
            Check(paths.All(path => File.ReadAllText(path) == "original loader"), "No executable replaced before stop");
            string staging = Path.Combine(Directory.GetDirectories(services).Single(), "staging");
            Check(DeckyFileUpgrade.Matches(staging, release.Assets), "Both assets are on disk and verified before stop");
        }
        events.Add("stop");
    }
    void Start()
    {
        events.Add("start");
        Check(DeckyFileUpgrade.Matches(services, release.Assets), "Start sees the verified pair in place");
        foreignListener = true;
        throw new IOException("A foreign listener claimed TCP 1337 before verification.");
    }
    await Reject<IOException>(() =>
    {
        DeckyFileUpgrade.Apply(services, payload, Prepare, Stop, Start, () => events.Add("stop owned"), () =>
        {
            Check(foreignListener && paths.All(path => File.ReadAllText(path) == "original loader"),
                "Rollback restores both executables while the foreign listener keeps TCP 1337");
            events.Add("restore");
        });
        return Task.CompletedTask;
    }, "Failed start propagates failure");
    Check(events.SequenceEqual(new[] { "prepare", "stop", "start", "stop owned", "restore" }) &&
        paths.All(path => File.ReadAllText(path) == "original loader"), "Failed start stops the new loader and restores both original files");
    Check(Directory.GetDirectories(services).Length == 0 && File.ReadAllText(plugin) == "settings to preserve", "Rollback leaves no scratch and preserves unrelated settings");

    DeckyFileUpgrade.Apply(services, payload, () => { }, () => { }, () => { },
        () => throw new Exception("Unexpected stop after start"), () => throw new Exception("Unexpected rollback"));
    Check(DeckyFileUpgrade.Matches(services, release.Assets) && Directory.GetDirectories(services).Length == 0,
        "Successful update retains only the verified pair");
    Check(File.ReadAllText(plugin) == "settings to preserve", "Successful update leaves plugin settings untouched");
}
finally
{
    foreach (var name in new[] { DeckyFileUpgrade.ConsoleName, DeckyFileUpgrade.BackgroundName })
        File.Delete(Path.Combine(folder.FullName, "services", name));
    File.Delete(Path.Combine(folder.FullName, "plugin-settings.json"));
    Directory.Delete(Path.Combine(folder.FullName, "services"));
    Directory.Delete(folder.FullName);
}

var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var firstOperation = DeckyInstallerService.RunDesktopOperationAsync<int>(async () =>
{
    firstEntered.SetResult();
    await releaseFirst.Task;
    throw new IOException("Fixture upgrade failed after an await.");
});
await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
bool secondEntered = false;
var secondOperation = DeckyInstallerService.RunDesktopOperationAsync(() =>
{
    secondEntered = true;
    return Task.FromResult(1);
});
Check(!secondEntered && !secondOperation.IsCompleted, "A second desktop operation waits across the first operation's await");
releaseFirst.SetResult();
await Reject<IOException>(() => firstOperation, "The first desktop operation propagates failure");
Check(await secondOperation.WaitAsync(TimeSpan.FromSeconds(3)) == 1 && secondEntered,
    "A failed desktop operation releases the gate for the next restart or removal");

Console.WriteLine($"Decky Windows release checks: {passed} passed. All HTTP and lifecycle operations were fixtures.");

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
    passed++;
}
async Task Reject<T>(Func<Task> run, string message) where T : Exception
{
    try { await run(); }
    catch (T) { Check(true, message); return; }
    throw new Exception("Accepted invalid operation: " + message);
}

sealed class Fixture(Func<string, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(response(request.RequestUri!.AbsoluteUri));
}

sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public override bool CanSeek => false;
}

sealed class ThrottledApiProbe : DelegatingHandler
{
    internal static HttpResponseMessage Response()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.RequestUri?.Host == "api.github.com" ? Task.FromResult(Response()) : base.SendAsync(request, cancellationToken);
}
