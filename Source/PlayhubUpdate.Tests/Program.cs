using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.Services;
using PlayhubSetup;
using UpdateTests;

var failures = 0;
var count = 0;
async Task Test(string name, Func<Task> action)
{
    count++;
    try { await action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
}
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
byte[] Zip(params (string Name, string Content)[] files)
{
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var file in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open());
            writer.Write(file.Content);
        }
    return stream.ToArray();
}
byte[] Package(byte[] zip)
{
    using var stream = new MemoryStream();
    var prefix = new byte[70000]; prefix[0] = (byte)'M'; prefix[1] = (byte)'Z';
    stream.Write(prefix); stream.Write(zip); stream.Write(BitConverter.GetBytes((long)zip.Length)); stream.Write("PLHB"u8);
    return stream.ToArray();
}
var payload = Package(Zip(("Playhub.exe", "NEW-1.4.0"), ("Assets/new.txt", "new asset")));
var digest = Convert.ToHexString(SHA256.HashData(payload));
var service = new PlayhubUpdateService();
string Release(string tag) => JsonSerializer.Serialize(new {
    tag_name = tag, html_url = "https://github.com/offline-test/fixture/releases/tag/" + tag, body = "Test release",
    assets = new[] { new { name = "Playhub Setup.exe", browser_download_url = "https://github.com/offline-test/fixture/releases/download/" + tag + "/Playhub%20Setup.exe", size = payload.Length, digest = "sha256:" + digest } }
});
void SetRelease(string tag)
{
    State.Reply = (request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
        Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(Release(tag)) : new ByteArrayContent(payload)
    });
}
Directory.CreateDirectory(State.Root);
var progress = new InlineProgress<(double Percent, string Status)>(_ => { });
var install = Path.Combine(State.Root, "custom-install", "Playhub");
var settings = Path.Combine(PlayhubSetup.Environment.GetFolderPath(PlayhubSetup.Environment.SpecialFolder.ApplicationData), "Playhub", "settings.json");
var info = new PlayhubUpdateService.UpdateInfo(true, "1.4.0", "1.3.0", null, null,
    "https://github.com/offline-test/fixture/releases/download/v1.4.0/Playhub%20Setup.exe", "Playhub Setup.exe", payload.Length, digest);
var extract = typeof(Installer).GetMethod("ExtractZip", BindingFlags.NonPublic | BindingFlags.Static)!;
void Extract(byte[] data, string destination)
{
    State.AssertPath(destination);
    using var zip = new MemoryStream(data);
    extract.Invoke(null, new object[] { zip, destination, progress });
}

try
{
    await Test("preview policy is isolated from release version comparison", () => {
        var older = info with { IsNewer = false, LatestVersion = "1.2.1" };
        Require(PlayhubUpdatePolicy.ShouldOffer(older) == PlayhubUpdatePolicy.IsPreview, "normal build offered older version");
        Require(!PlayhubUpdatePolicy.ShouldOffer(null), "empty release offered");
        Require(PlayhubUpdatePolicy.ShouldOffer(info) == !PlayhubUpdatePolicy.IsPreview, "preview must offer only the pinned release");
        Require(PlayhubUpdatePolicy.ReleaseTag == (PlayhubUpdatePolicy.IsPreview ? "v1.2.1" : null), "wrong pinned release policy");
        Require(!PlayhubUpdatePolicy.ShouldOffer(older with { DownloadUrl = null }), "preview offered missing installer");
        Require(older.LatestVersion == "1.2.1" && !older.IsNewer, "real metadata was falsified");
        Require(PlayhubUpdatePolicy.Repository("configured/repo") ==
            (PlayhubUpdatePolicy.IsPreview ? "LoZazaMastro/Playhub" : "configured/repo"), "wrong repository policy");
        return Task.CompletedTask;
    });
    await Test("setup starts once, including repeated ContentRendered events", () => {
        var session = new SetupSession();
        Require(session.TryStart(), "initial start rejected");
        for (var i = 0; i < 40; i++) Require(!session.TryStart(), "setup started twice");
        session.Complete(true);
        Require(!session.TryStart(), "completed setup restarted");
        return Task.CompletedTask;
    });
    foreach (var scenario in new[] { (true, true, true, 1), (true, false, true, 0), (false, true, true, 0), (true, true, false, 0) })
        await Test($"finish once: success={scenario.Item1}, launch={scenario.Item2}, install={scenario.Item3}", () => {
            var session = new SetupSession();
            Require(!session.TryFinish(out _), "unfinished setup allowed launch");
            session.TryStart(); session.Complete(scenario.Item1);
            var launches = 0; var finishes = 0;
            for (var i = 0; i < 40; i++)
                if (session.TryFinish(out var succeeded)) {
                    finishes++;
                    if (succeeded && scenario.Item2 && scenario.Item3) launches++;
                }
            Require(finishes == 1 && launches == scenario.Item4, "duplicate or inappropriate launch");
            return Task.CompletedTask;
        });
    foreach (var version in new[] { ("1.3.0", true), ("1.4.0", false), ("1.4", false), ("1.4.0.0", false), ("1.5.0", false) })
        await Test("version comparison " + version.Item1, async () => {
            SetRelease("v1.4.0");
            Require((await service.CheckAsync("offline-test/fixture", version.Item1))?.IsNewer == version.Item2, "wrong comparison");
        });
    await Test("download exact bytes and complete progress", async () => {
        SetRelease("v1.4.0"); double fraction = 0;
        var file = await service.DownloadInstallerAsync(info, new InlineProgress<PlayhubUpdateService.DownloadProgress>(p => fraction = p.Fraction));
        Require(File.ReadAllBytes(file).SequenceEqual(payload) && fraction == 1, "download mismatch");
        PlayhubSetup.Environment.ProcessPath = file;
    });
    await DownloadStallTests.RunAsync(Test, Require, info, payload);
    await Test("pinned test release requests only 1.2.1", async () => {
        State.Reply = (request, _) => {
            Require(request.RequestUri!.AbsolutePath.EndsWith("/releases/tags/v1.2.1"), "preview requested latest instead of pinned tag");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release("v1.2.1")) });
        };
        var pinned = await service.CheckAsync("offline-test/fixture", "1.3.0", "v1.2.1");
        Require(pinned is { LatestVersion: "1.2.1", IsNewer: false }, "pinned metadata was falsified");
    });
    await Test("missing or wrong pinned release never falls back to latest", async () => {
        var calls = 0;
        State.Reply = (_, _) => { calls++; throw new HttpRequestException("missing tag fixture"); };
        Require(await service.CheckAsync("offline-test/fixture", "1.3.0", "v1.2.1") is null && calls == 1, "preview fell back to a different release");
        SetRelease("v1.4.0");
        Require(await service.CheckAsync("offline-test/fixture", "1.3.0", "v1.2.1") is null, "wrong tag accepted");
        SetRelease("v1.4.0");
    });
    await Test("install update preserves settings and shortcuts", async () => {
        Directory.CreateDirectory(install); File.WriteAllText(Path.Combine(install, "Playhub.exe"), "OLD-1.3.0");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!); File.WriteAllText(settings, "{\"Language\":\"it\",\"Accent\":\"purple\",\"OnboardingComplete\":true}");
        var desktop = Path.Combine(PlayhubSetup.Environment.GetFolderPath(PlayhubSetup.Environment.SpecialFolder.DesktopDirectory), "Playhub.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(desktop)!); File.WriteAllText(desktop, "existing custom shortcut");
        await Installer.InstallAsync(new InstallOptions(install, true, true, "it", true), progress);
        Require(File.ReadAllText(Path.Combine(install, "Playhub.exe")) == "NEW-1.4.0", "old file remained");
        Require(File.ReadAllText(Path.Combine(install, "Assets", "new.txt")) == "new asset", "missing new file");
        using var json = JsonDocument.Parse(File.ReadAllText(settings));
        Require(json.RootElement.GetProperty("Accent").GetString() == "purple" && json.RootElement.GetProperty("OnboardingComplete").GetBoolean(), "settings lost");
        Require(File.ReadAllText(desktop) == "existing custom shortcut", "shortcut changed");
        Require(Installer.ReadInstallDir() == install && Installer.ReadAppLanguage() == "it", "custom path or language lost");
        Require(File.ReadAllBytes(Path.Combine(install, Installer.UninstallerName)).SequenceEqual(payload), "uninstaller wrong");
        Require(Registry.CurrentUser.GetValue("DisplayVersion") as string == Installer.AppVersion, "registry version wrong");
        Installer.LaunchApp(install);
        Require(PlayhubSetup.Process.Launches.Single().FileName == Path.Combine(install, "Playhub.exe"), "wrong relaunch target");
    });
    await Test("post-update check is clean", async () => {
        SetRelease("v" + Installer.AppVersion); Require((await service.CheckAsync("offline-test/fixture", Installer.AppVersion))?.IsNewer == false, "update repeated");
    });
    async Task Reject(string name, PlayhubUpdateService.UpdateInfo badInfo, Func<HttpResponseMessage> response)
    {
        await Test(name, async () => {
            State.Reply = (_, _) => Task.FromResult(response());
            var failed = false;
            try { await service.DownloadInstallerAsync(badInfo); } catch { failed = true; }
            Require(failed, "unsafe download accepted");
            Require(!Directory.GetFiles(AppPaths.DownloadsRoot, "*.part", SearchOption.AllDirectories).Any(), "partial file left");
            Require(File.ReadAllBytes(PlayhubSetup.Environment.ProcessPath!).SequenceEqual(payload), "last valid installer overwritten");
        });
    }
    await Reject("bad checksum rejected", info with { Sha256Digest = new string('0', 64) }, () => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    await Reject("short download rejected", info, () => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload[..65000]) });
    await Reject("HTML instead of EXE rejected", info with { Sha256Digest = null }, () => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[payload.Length]) });
    await Reject("HTTP 404 rejected", info, () => new(HttpStatusCode.NotFound));
    await Reject("non-HTTPS URL rejected", info with { DownloadUrl = "http://github.com/offline-test/file.exe" }, () => new(HttpStatusCode.OK));
    await Test("cancelled download cleaned", async () => {
        State.Reply = (_, token) => { token.ThrowIfCancellationRequested(); throw new Exception("Cancellation not propagated"); };
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancelled = false;
        try { await service.DownloadInstallerAsync(info, cancellationToken: cts.Token); } catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "cancellation not reported");
        Require(!Directory.GetFiles(AppPaths.DownloadsRoot, "*.part", SearchOption.AllDirectories).Any(), "partial remains");
    });
    await Test("offline returns no update", async () => {
        State.Reply = (_, _) => throw new HttpRequestException("offline fixture");
        Require(await service.CheckAsync("offline-test/fixture", "1.3.0") is null, "wrong offline result");
    });
    await Test("stable redirect fallback, never Atom", async () => {
        State.Reply = (request, _) => {
            if (request.RequestUri!.Host == "api.github.com") throw new HttpRequestException("rate limit fixture");
            Require(request.RequestUri.AbsolutePath.EndsWith("/releases/latest"), "unstable fallback used");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://github.com/offline-test/fixture/releases/tag/v1.4.0"),
                Content = new StringContent("fixture")
            });
        };
        Require((await service.CheckAsync("offline-test/fixture", "1.3.0"))?.IsNewer == true, "fallback failed");
    });
    await Test("unresolved redirect is not an update", async () => {
        State.Reply = (request, _) => request.RequestUri!.Host == "api.github.com"
            ? throw new HttpRequestException("rate limit fixture")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent("no release") });
        Require(await service.CheckAsync("offline-test/fixture", "1.3.0") is null, "unverified fallback accepted");
    });
    await Test("ZIP cannot escape into sibling directory", () => {
        var dest = Path.Combine(State.Root, "zip-target"); Directory.CreateDirectory(dest);
        var rejected = false;
        try { Extract(Zip(("../zip-target-escape/probe.txt", "escape")), dest); } catch (TargetInvocationException) { rejected = true; }
        Require(rejected && !File.Exists(Path.Combine(State.Root, "zip-target-escape", "probe.txt")), "ZIP escaped");
        return Task.CompletedTask;
    });
    await Test("invalid later entry leaves installed files unchanged", () => {
        var dest = Path.Combine(State.Root, "invalid-later"); Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "Playhub.exe"), "OLD");
        try { Extract(Zip(("Playhub.exe", "NEW"), ("../escape.txt", "escape")), dest); } catch (TargetInvocationException) { }
        Require(File.ReadAllText(Path.Combine(dest, "Playhub.exe")) == "OLD", "preflight did not protect installation");
        return Task.CompletedTask;
    });
    await Test("failed replacement rolls back changed and added files", () => {
        var dest = Path.Combine(State.Root, "rollback"); Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "Playhub.exe"), "OLD"); File.WriteAllText(Path.Combine(dest, "blocked"), "file");
        var failed = false;
        try { Extract(Zip(("Playhub.exe", "NEW"), ("new-folder/new.txt", "new"), ("blocked/child.txt", "fails")), dest); }
        catch (TargetInvocationException) { failed = true; }
        Require(failed && File.ReadAllText(Path.Combine(dest, "Playhub.exe")) == "OLD", "partial replacement left");
        Require(!Directory.Exists(Path.Combine(dest, "new-folder")), "added directory left");
        return Task.CompletedTask;
    });
    await Test("locked file triggers rollback", () => {
        var dest = Path.Combine(State.Root, "locked"); Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "first.txt"), "OLD"); File.WriteAllText(Path.Combine(dest, "locked.txt"), "LOCKED");
        using var locked = new FileStream(Path.Combine(dest, "locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        var failed = false;
        try { Extract(Zip(("first.txt", "NEW"), ("locked.txt", "replacement")), dest); } catch (TargetInvocationException) { failed = true; }
        Require(failed && File.ReadAllText(Path.Combine(dest, "first.txt")) == "OLD", "locked-file failure did not roll back");
        return Task.CompletedTask;
    });
    await Test("duplicate entries rejected before replacement", () => {
        var dest = Path.Combine(State.Root, "duplicate"); Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "Playhub.exe"), "OLD");
        try { Extract(Zip(("Playhub.exe", "NEW"), ("Playhub.exe", "DUPLICATE")), dest); } catch (TargetInvocationException) { }
        Require(File.ReadAllText(Path.Combine(dest, "Playhub.exe")) == "OLD", "duplicate overwrote installed version");
        return Task.CompletedTask;
    });
    await Test("staging and backup folders cleaned", () => {
        Require(!Directory.GetDirectories(State.Root, ".playhub-update-*", SearchOption.AllDirectories).Any(), "staging leftovers");
        return Task.CompletedTask;
    });
}
finally
{
    // Only this run's unique directory is removed; production folders are never used.
    if (Directory.Exists(State.Root)) Directory.Delete(State.Root, recursive: true);
}
Console.WriteLine($"RESULT {count - failures}/{count} passed. External HTTP, real process launches and registry writes: zero. Test data removed.");
System.Environment.ExitCode = failures == 0 ? 0 : 1;
