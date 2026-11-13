using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.Services;

var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "Source", "Playhub"))) repository = repository.Parent;
if (repository is null) throw new DirectoryNotFoundException("Run from the Playhub checkout.");
var source = Path.Combine(repository.FullName, "Source", "Playhub", BundledDeckyWindowsRelease.RelativeRoot);
var fixture = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Playhub-BundledDecky-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(fixture);
var bundle = Path.Combine(fixture, "bundle");
Directory.CreateDirectory(Path.Combine(bundle, "sources"));
foreach (var name in new[] { "PluginLoader.exe", "PluginLoader_noconsole.exe", "LICENSE", BundledDeckyWindowsRelease.SourceName })
    File.Copy(Path.Combine(source, name), Path.Combine(bundle, name));
int checks = 0;
void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS " + label); }
async Task Reject<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); } catch (T) { Check(true, label); return; }
    throw new InvalidOperationException(label);
}
try
{
    var offlineHandler = new Handler(_ => throw new HttpRequestException("Offline fixture"));
    using var offline = new HttpClient(offlineHandler);
    var client = new DeckyWindowsReleaseClient(offline, Architecture.X64, bundle);
    var selected = await client.GetAsync("v3.3.1");
    Check(selected.Assets.All(asset => asset.Url.IsFile) && offlineHandler.Calls == 1,
        "Explicit official release falls back to verified official bytes when the network is unavailable");
    var payload = new Dictionary<DeckyWindowsReleaseClient.Asset, byte[]>();
    foreach (var asset in selected.Assets) payload[asset] = await client.DownloadAsync(asset);
    Check(payload.Count == 2 && payload.All(item => Convert.ToHexString(SHA256.HashData(item.Value)).Equals(item.Key.Digest, StringComparison.OrdinalIgnoreCase)),
        "Both actual bundled Windows executables match pinned hashes");
    var consolePath = Path.Combine(bundle, "PluginLoader.exe");
    var tampered = (byte[])payload[selected.Console].Clone(); tampered[^1] ^= 1;
    File.WriteAllBytes(consolePath, tampered);
    await Reject<InvalidDataException>(() => client.DownloadAsync(selected.Console), "Modified executable bytes are rejected before staging");
    File.WriteAllBytes(consolePath, payload[selected.Console]);
    var fallback = await client.GetAsync();
    Check(fallback.OfflineFallback && fallback.Summary.Tag == "v3.3.1" && fallback.Assets.All(asset => asset.Url.IsFile),
        "Offline latest resolution selects the verified local pair");
    await Reject<InvalidDataException>(() => client.DownloadAsync(selected.Console with { Url = new Uri(Path.Combine(fixture, "foreign.exe")) }),
        "Local transport rejects arbitrary file paths before reading them");
    await Reject<InvalidDataException>(() => client.DownloadAsync(selected.Console with { Digest = new string('0', 64) }),
        "Local transport rejects rewritten digest metadata");
    var arm = new DeckyWindowsReleaseClient(offline, Architecture.Arm64, bundle);
    await Reject<HttpRequestException>(() => arm.GetAsync(), "ARM64 never substitutes the x64 offline build");
    await Reject<InvalidDataException>(() => arm.DownloadAsync(selected.Console), "ARM64 rejects x64 local transport");

    var originalSource = File.ReadAllBytes(Path.Combine(bundle, BundledDeckyWindowsRelease.SourceName));
    var changed = (byte[])originalSource.Clone(); changed[^1] ^= 1;
    File.WriteAllBytes(Path.Combine(bundle, BundledDeckyWindowsRelease.SourceName), changed);
    await Reject<InvalidDataException>(() => client.GetAsync("v3.3.1"), "A changed corresponding-source archive invalidates the bundled release");
    File.WriteAllBytes(Path.Combine(bundle, BundledDeckyWindowsRelease.SourceName), originalSource);

    object Metadata(string tag) => new { tag_name = tag, draft = false, prerelease = false, published_at = "2026-10-27T04:26:19Z",
        assets = new[] { new { name = "PluginLoader-win.exe", state = "uploaded", size = selected.Console.Size,
            digest = "sha256:" + selected.Console.Digest, browser_download_url = $"https://github.com/SteamDeckHomebrew/decky-loader/releases/download/{tag}/PluginLoader-win.exe" },
            new { name = "PluginLoader-win-noconsole.exe", state = "uploaded", size = selected.Background.Size,
            digest = "sha256:" + selected.Background.Digest, browser_download_url = $"https://github.com/SteamDeckHomebrew/decky-loader/releases/download/{tag}/PluginLoader-win-noconsole.exe" } } };
    using var currentHttp = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Metadata("v3.3.1"))) }));
    var current = await new DeckyWindowsReleaseClient(currentHttp, Architecture.X64, bundle).GetAsync();
    Check(current.Assets.All(asset => asset.Url.Scheme == "https") && !current.OfflineFallback,
        "Online upstream 3.3.1 retains official URLs instead of substituting any local build");
    var explicitCurrent = await new DeckyWindowsReleaseClient(currentHttp, Architecture.X64, bundle).GetAsync("v3.3.1");
    Check(explicitCurrent.Assets.All(asset => asset.Url.Scheme == "https") && explicitCurrent.Console.Digest == BundledDeckyWindowsRelease.ConsoleDigest,
        "An explicit installed-version selection still verifies official upstream metadata online");
    using var futureHttp = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Metadata("v3.3.2"))) }));
    var future = await new DeckyWindowsReleaseClient(futureHttp, Architecture.X64, bundle).GetAsync();
    Check(future.Summary.Tag == "v3.3.2" && future.Assets.All(asset => asset.Url.Scheme == "https") && !future.OfflineFallback,
        "A newer official release retains priority and upstream provenance");

    var installed = Path.Combine(fixture, "installed");
    Directory.CreateDirectory(installed);
    BundledDeckyWindowsRelease.RequireSafeOfflineReplacement(installed);
    int callbacks = 0;
    DeckyFileUpgrade.Apply(installed, payload, () => callbacks++, () => callbacks++, () => callbacks++, () => callbacks++, () => callbacks++);
    Check(callbacks == 3 && BundledDeckyWindowsRelease.IsOfficialPair(installed) && !BundledDeckyWindowsRelease.IsRetiredCompatibilityPair(installed),
        "Offline transaction installs only the unmodified official pair using inert lifecycle callbacks");
    BundledDeckyWindowsRelease.RequireSafeOfflineReplacement(installed);
    Check(!BundledDeckyWindowsRelease.HasNewerInstalledVersion(installed), "Official 3.3.1 metadata is not treated as a newer installed release");
    File.WriteAllText(Path.Combine(installed, "PluginLoader.exe"), "unknown or newer installation fixture");
    await Reject<IOException>(() => Task.Run(() => BundledDeckyWindowsRelease.RequireSafeOfflineReplacement(installed)),
        "An unknown existing installation is preserved when upstream cannot be checked");
    File.Copy(Environment.ProcessPath!, Path.Combine(installed, "PluginLoader.exe"), overwrite: true);
    Check(BundledDeckyWindowsRelease.HasNewerInstalledVersion(installed),
        "Real PE metadata for a newer installed version prevents a compatibility downgrade");

    var previous = new byte[] { 4, 8, 15, 16, 23, 42 };
    foreach (var name in new[] { "PluginLoader.exe", "PluginLoader_noconsole.exe" }) File.WriteAllBytes(Path.Combine(installed, name), previous);
    await Reject<IOException>(() => Task.Run(() => DeckyFileUpgrade.Apply(installed, payload, () => { }, () => { },
        () => throw new IOException("Start fixture rejected"), () => { }, () => { })), "A failed start triggers the existing rollback transaction");
    Check(Directory.GetFiles(installed).All(path => File.ReadAllBytes(path).SequenceEqual(previous)) && Directory.GetDirectories(installed).Length == 0,
        "Rollback restores both original bytes and removes staging files");
    Console.WriteLine($"{checks}/{checks} bundled Decky checks passed. No loader, Steam or hardware actions.");
}
finally
{
    if (!fixture.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "Playhub-BundledDecky-", StringComparison.OrdinalIgnoreCase))
        throw new IOException("Unsafe fixture cleanup path");
    Directory.Delete(fixture, recursive: true);
}

sealed class Handler(Func<Uri, HttpResponseMessage> response) : HttpMessageHandler
{
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Calls++; return Task.FromResult(response(request.RequestUri!)); }
}
