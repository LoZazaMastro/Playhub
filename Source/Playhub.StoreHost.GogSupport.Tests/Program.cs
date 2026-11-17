// SPDX-License-Identifier: MIT. Synthetic GOG metadata/files only; no real CLI, game, account, registry or network.
using System.Diagnostics;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

if (args.Contains("--supervise"))
{
    _ = Console.ReadLine();
    Console.WriteLine("""{"kind":"exit","exitCode":0,"stopRequested":false,"drained":true,"forced":false}""");
    return;
}
if (args.Length == 3 && args[0] == "--metadata-copy-check")
{
    const string productId = "1207658901";
    var sourceRoot = new StoreInstallFileSystem().CanonicalDestination(args[1]);
    var probeRoot = Path.Combine(Path.GetTempPath(), "Playhub-GogSupport-RealMetadata-" + Guid.NewGuid().ToString("N"));
    var gameRoot = Path.Combine(probeRoot, "owned-copy"); var state = Path.Combine(probeRoot, "state");
    Directory.CreateDirectory(gameRoot); var ownerJob = Guid.NewGuid(); File.WriteAllText(Path.Combine(gameRoot, ".playhub-install-owner"), ownerJob.ToString("D"));
    void CopyReadOnly(string relative)
    {
        var source = StoreOwnedInstallFiles.PathFor(sourceRoot, relative); var target = StoreOwnedInstallFiles.PathFor(gameRoot, relative);
        using var sourcePins = StoreOwnedInstallFiles.PinRoot(Path.GetDirectoryName(source)!);
        using var input = StoreOwnedInstallFiles.OpenReadFile(source); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output);
    }
    foreach (var relative in new[] { "goggame-" + productId + ".info", "goggame-" + productId + ".script" }) CopyReadOnly(relative);
    foreach (var directory in new[] { "DOSBOX", "gog-support/" + productId })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(sourceRoot, directory), "*", SearchOption.AllDirectories)) CopyReadOnly(Path.GetRelativePath(sourceRoot, file));
    var manifestDir = Path.Combine(state, "gog", "native", "heroic_gogdl", "manifests"); Directory.CreateDirectory(manifestDir);
    File.Copy(args[2], Path.Combine(manifestDir, productId));
    var provider = new StoreCliProvider(StoreKind.Gog, Environment.GetEnvironmentVariable("PLAYHUB_TEST_RUNTIME") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Playhub.StoreHost/Runtime")), state, Environment.ProcessPath!);
    await provider.FinalizeGogInstallationAsync(productId, gameRoot, ownerJob);
    File.WriteAllText(Path.Combine(state, "gog", "installed-" + productId + ".json"), JsonSerializer.Serialize(gameRoot));
    var launch = await provider.PrepareLaunchAsync(productId, gameRoot, default);
    var uninstall = await provider.PrepareUninstallAsync(productId, gameRoot, ownerJob, default);
    Console.WriteLine(JsonSerializer.Serialize(new { probeRoot, configsPresent = File.Exists(Path.Combine(gameRoot, "dosboxT2K.conf")) && File.Exists(Path.Combine(gameRoot, "dosboxT2K_single.conf")),
        cloudSavesCreated = Directory.Exists(Path.Combine(gameRoot, "cloud_saves")), dependencyReceipt = File.Exists(Path.Combine(state, "gog", "dependencies-owned-" + productId + ".json")),
        primaryExecutable = Path.GetRelativePath(gameRoot, launch.GameExecutable), workingDirectory = Path.GetRelativePath(gameRoot, launch.WorkingDirectory), argv = launch.Arguments,
        uninstallOwnedFiles = uninstall.Files.Count, uninstallIncludesPrimary = uninstall.Files.Any(f => StringComparer.OrdinalIgnoreCase.Equals(StoreOwnedInstallFiles.PathFor(gameRoot, f.RelativePath), launch.GameExecutable)),
        uninstallIncludesCopiedConfig = uninstall.Files.Any(f => f.RelativePath == "dosboxT2K.conf"), noGameOrInstallerExecuted = true }));
    return;
}
var checks = 0; var root = Path.Combine(Path.GetTempPath(), "Playhub-GogSupport-Checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
void Refuses(Action action, string code)
{
    try { action(); throw new Exception("accepted:" + code); }
    catch (StoreOperationException e) { Check(e.Code == code, "specific refusal " + code + " received " + e.Code); }
}
Fixture New(string name, string? script = null) => new(Path.Combine(root, name), script);
try
{
    var f = New("primary-config");
    Check(!f.ConfigsPresent(), "causal baseline: primary DOSBox configuration references are missing after raw download");
    var provider = f.Provider();
    await using (var transfer = await provider.StartAsync(new(f.Job, new(StoreKind.Gog, Fixture.Id), f.Path, "en-US"), _ => { }, default))
    {
        var complete = await transfer.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Check(complete.Completed && complete.InstallVerified && f.ConfigsPresent(), "actual transfer cannot confirm installation before supportData supplies both referenced configs: " + complete.ErrorCode);
    }
    Check(Directory.Exists(Path.Combine(f.Path, "cloud_saves")), "source-less folder action creates cloud_saves under owned root");
    var plan = await provider.PrepareLaunchAsync(Fixture.Id, f.Path, default);
    Check(plan.WorkingDirectory == Path.Combine(f.Path, "DOSBOX") && plan.Arguments.SequenceEqual(new[] { "-conf", "..\\dosboxT2K.conf", "-conf", "..\\dosboxT2K_single.conf", "-noconsole", "-c", "exit" }), "primary argv and working directory preserved exactly");
    var removal = await provider.PrepareUninstallAsync(Fixture.Id, f.Path, f.Job, default);
    Check(removal.Files.Count == f.Files.Count + 2 && removal.Files.Any(x => x.RelativePath == "dosboxT2K.conf"), "copied configurations and original support files enter uninstall ownership");
    Check(removal.Files.Any(x => x.Chunks?.Count == 2), "v2 multi-chunk checksum retained without fictional whole-file MD5");
    await provider.FinalizeGogInstallationAsync(Fixture.Id, f.Path, f.Job);
    Check(f.ConfigsPresent(), "repeat finalization verifies owned copies idempotently");
    File.WriteAllText(Path.Combine(f.Path, "cloud_saves", "user.sav"), "save to preserve");
    await provider.UninstallAsync(removal, default);
    Check(!File.Exists(Path.Combine(f.Path, "dosboxT2K.conf")) && !File.Exists(f.Executable), "actual safe uninstall removes copied configs and payload");
    Check(File.ReadAllText(Path.Combine(f.Path, "cloud_saves", "user.sav")) == "save to preserve", "user save preserves generated folder");
    Check(!File.Exists(f.Receipt), "private support receipt removed only after successful uninstall commit");

    var unsupported = New("unsupported", Fixture.Script("""{"install":{"action":"Execute","arguments":{"path":"setup.exe"}},"languages":["*"]}"""));
    await using (var transfer = await unsupported.Provider().StartAsync(new(unsupported.Job, new(StoreKind.Gog, Fixture.Id), unsupported.Path, null), _ => { }, default))
    {
        var failed = await transfer.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Check(!failed.Completed && !failed.InstallVerified && failed.ErrorCode == "gog_support_action_unsupported", "zero-exit CLI with unsupported required setup is never a completed/installed transfer");
        Check(!File.Exists(Path.Combine(unsupported.State, "gog", "installed-" + Fixture.Id + ".json")), "failed finalization never commits own install registration");
    }
    var missing = New("missing-source"); File.Delete(Path.Combine(missing.Path, "gog-support", Fixture.Id, "app", "dosboxT2K.conf"));
    await using (var transfer = await missing.Provider().StartAsync(new(missing.Job, new(StoreKind.Gog, Fixture.Id), missing.Path, null), _ => { }, default))
    {
        var failed = await transfer.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Check(!failed.Completed && !failed.InstallVerified, "missing downloaded support source cannot be falsely completed");
        Check(!File.Exists(Path.Combine(missing.Path, "dosboxT2K_single.conf")), "all sources preflight before first copy");
    }
    var changed = New("changed-source"); File.WriteAllText(Path.Combine(changed.Path, "gog-support", Fixture.Id, "app", "dosboxT2K.conf"), new string('x', 12));
    Refuses(() => changed.Apply(), "installed_files_modified");
    Check(!File.Exists(changed.Receipt), "tampered source has no ownership claim");
    var modifiedScript = New("changed-script"); File.WriteAllText(Path.Combine(modifiedScript.Path, "goggame-" + Fixture.Id + ".script"), "{}");
    Refuses(() => modifiedScript.Apply(), "installed_files_modified");
    var foreign = New("foreign-file"); File.WriteAllText(Path.Combine(foreign.Path, "dosboxT2K.conf"), "user settings");
    Refuses(() => foreign.Apply(), "gog_support_destination_conflict");
    Check(File.ReadAllText(Path.Combine(foreign.Path, "dosboxT2K.conf")) == "user settings", "unowned existing configuration never overwritten or adopted");
    var modifiedCopy = New("modified-copy"); modifiedCopy.Apply(); File.WriteAllText(Path.Combine(modifiedCopy.Path, "dosboxT2K.conf"), "user modified config");
    Refuses(() => modifiedCopy.Apply(), "gog_support_destination_conflict");
    Check(File.ReadAllText(Path.Combine(modifiedCopy.Path, "dosboxT2K.conf")) == "user modified config", "retry does not overwrite user-modified owned configuration");
    var otherOwner = New("wrong-owner"); File.WriteAllText(Path.Combine(otherOwner.Path, ".playhub-install-owner"), Guid.NewGuid().ToString("D"));
    Refuses(() => otherOwner.Apply(), "destination_ownership_lost");

    foreach (var target in new[] { "{app}/../escape.conf", "C:/outside", "{app}/.playhub-install-owner", "{app}/gog-support/1207658901/app/dosboxT2K.conf", "{app}/CON", "{app}/bad:stream" })
    {
        var hostile = New("target-" + checks, Fixture.Script(Fixture.Copy("{supportDir}/app/dosboxT2K.conf", target, "file")));
        var rejected = false; try { hostile.Apply(); } catch (StoreOperationException) { rejected = true; }
        Check(rejected && !File.Exists(hostile.Receipt), "unsafe destination refused before ownership/copy: " + target);
    }
    foreach (var source in new[] { "{app}/DOSBOX/game.exe", "{supportDir}/../other/config", "C:/outside", "{supportDir}/app/../../escape" })
    {
        var hostile = New("source-" + checks, Fixture.Script(Fixture.Copy(source, "{app}/out.conf", "file")));
        var rejected = false; try { hostile.Apply(); } catch (StoreOperationException) { rejected = true; }
        Check(rejected && !File.Exists(hostile.Receipt), "unsafe source refused before ownership/copy: " + source);
    }
    var language = New("language", Fixture.Script("""{"install":{"action":"Execute","arguments":{}},"languages":["fr-FR"]}""" + "," + Fixture.Copy("{supportDir}/app", "{app}", "folder")));
    language.Apply(); Check(language.ConfigsPresent(), "unselected-language setup is not run or interpreted");
    var badProduct = New("wrong-product", Fixture.Script(Fixture.Copy("{supportDir}/app", "{app}", "folder")).Replace("\"productId\":\"1207658901\"", "\"productId\":\"999\""));
    Refuses(() => badProduct.Apply(), "gog_support_script_invalid");
    var registry = New("arbitrary-registry", Fixture.Script("""{"install":{"action":"setRegistry","arguments":{"root":"HKEY_LOCAL_MACHINE","subkey":"Software\\OtherGame","valueName":"InstallDir"}}}"""));
    Refuses(() => registry.Apply(), "gog_support_action_unsupported");

    var bundled = new Fixture(Path.Combine(root, "bundled-runtime"), null, bundledDependency: true);
    var bundledProvider = bundled.Provider();
    await using (var transfer = await bundledProvider.StartAsync(new(bundled.Job, new(StoreKind.Gog, Fixture.Id), bundled.Path, null), _ => { }, default))
    { var completed = await transfer.Completion.WaitAsync(TimeSpan.FromSeconds(15)); Check(completed.Completed && completed.InstallVerified, "game-local runtime dependency hashes verified before completion"); }
    Check(File.Exists(Path.Combine(bundled.State, "gog", "dependencies-owned-" + Fixture.Id + ".json")), "exact dependency version retained privately at install time");
    bundled.DisableDependencyNetwork = true;
    var bundledRemoval = await bundledProvider.PrepareUninstallAsync(Fixture.Id, bundled.Path, bundled.Job, default);
    Check(bundledRemoval.Files.Any(f => Path.GetFullPath(StoreOwnedInstallFiles.PathFor(bundled.Path, f.RelativePath)) == bundled.Executable), "runtime executable enters uninstall plan from verified dependency receipt");
    await bundledProvider.UninstallAsync(bundledRemoval, default);
    Check(!File.Exists(bundled.Executable), "bundled runtime removed through owned manifest without changing system runtimes");
    var oldBundled = new Fixture(Path.Combine(root, "old-bundled-runtime"), null, bundledDependency: true);
    File.WriteAllText(Path.Combine(oldBundled.State, "gog", "installed-" + Fixture.Id + ".json"), JsonSerializer.Serialize(oldBundled.Path));
    var migrated = await oldBundled.Provider().PrepareUninstallAsync(Fixture.Id, oldBundled.Path, oldBundled.Job, default);
    Check(migrated.Files.Any(f => f.RelativePath == "DOSBOX/game.exe"), "old owned installation migrates only after exact official dependency checksums match");
    var tamperedDependency = new Fixture(Path.Combine(root, "tampered-runtime"), null, bundledDependency: true);
    File.WriteAllText(tamperedDependency.Executable, "changed runtime");
    await using (var transfer = await tamperedDependency.Provider().StartAsync(new(tamperedDependency.Job, new(StoreKind.Gog, Fixture.Id), tamperedDependency.Path, null), _ => { }, default))
    { var failure = await transfer.Completion.WaitAsync(TimeSpan.FromSeconds(15)); Check(!failure.Completed && !failure.InstallVerified, "wrong bundled runtime content prevents false completion"); }
    Check(!File.Exists(tamperedDependency.Receipt), "dependency failure occurs before support data ownership/copy");

    var overwriteOwned = New("overwrite-authorized"); overwriteOwned.Add("dosboxT2K.conf", "downloaded original config", false);
    overwriteOwned.Apply(); Check(File.ReadAllText(Path.Combine(overwriteOwned.Path, "dosboxT2K.conf")) == "[sdl]\nfull=1", "overwrite true replaces only checksum-proven downloaded target");
    var overwriteForeign = New("overwrite-false", Fixture.Script(Fixture.Copy("{supportDir}/app", "{app}", "folder").Replace("\"overwrite\":true", "\"overwrite\":false")));
    overwriteForeign.Add("dosboxT2K.conf", "downloaded original config", false);
    Refuses(() => overwriteForeign.Apply(), "installed_files_modified");

    var privateBinding = New("private-receipt-binding"); privateBinding.Apply();
    var receiptText = File.ReadAllText(privateBinding.Receipt);
    File.WriteAllText(privateBinding.Receipt, receiptText.Replace(privateBinding.Job.ToString(), Guid.NewGuid().ToString()));
    Refuses(() => privateBinding.Apply(), "gog_support_receipt_invalid");
    var modifiedInstalledConfig = New("uninstall-modified-configuration");
    var modifiedProvider = modifiedInstalledConfig.Provider();
    await using (var transfer = await modifiedProvider.StartAsync(new(modifiedInstalledConfig.Job, new(StoreKind.Gog, Fixture.Id), modifiedInstalledConfig.Path, null), _ => { }, default)) _ = await transfer.Completion;
    var beforeEdit = await modifiedProvider.PrepareUninstallAsync(Fixture.Id, modifiedInstalledConfig.Path, modifiedInstalledConfig.Job, default);
    File.WriteAllText(Path.Combine(modifiedInstalledConfig.Path, "dosboxT2K.conf"), "user change");
    try { await modifiedProvider.UninstallAsync(beforeEdit, default); throw new Exception("accepted modified config removal"); }
    catch (StoreOperationException e) { Check(e.Code == "installed_files_modified", "modified copied configuration blocks removal before any original payload is deleted"); }
    Check(File.Exists(modifiedInstalledConfig.Executable) && File.ReadAllText(Path.Combine(modifiedInstalledConfig.Path, "dosboxT2K.conf")) == "user change", "whole uninstall preflight preserves modified configuration and game");
    var canceled = New("canceled-before-copies");
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        try { StoreGogSupportData.Apply(canceled.Receipt, canceled.Path, Fixture.Id, canceled.Job, "en-US", true, canceled.Files, cancellation.Token); throw new Exception("accepted canceled finalization"); }
        catch (OperationCanceledException) { Check(!File.Exists(canceled.Receipt) && !canceled.ConfigsPresent(), "cancel before finalization has no copy or private ownership receipt"); }
    }
    var traversalLink = New("reparse-source"); var foreignFolder = Path.Combine(root, "foreign-link-target"); Directory.CreateDirectory(foreignFolder);
    var supportFolder = Path.Combine(traversalLink.Path, "gog-support", Fixture.Id, "app"); var heldFolder = supportFolder + "-held"; Directory.Move(supportFolder, heldFolder);
    Directory.CreateSymbolicLink(supportFolder, foreignFolder);
    Refuses(() => traversalLink.Apply(), "destination_reparse_point");
    Check(!File.Exists(traversalLink.Receipt) && Directory.GetFileSystemEntries(foreignFolder).Length == 0, "support source reparse never traverses or writes foreign tree");

    var chunked = new StoreOwnedInstallFile("inert", 6, null, null, [new(3, Fixture.Md5("abc")), new(3, Fixture.Md5("def"))]);
    StoreOwnedInstallFiles.VerifyContents(new MemoryStream(Encoding.UTF8.GetBytes("abcdef")), chunked, default); Check(true, "exact ordered MD5 chunks verify");
    Refuses(() => StoreOwnedInstallFiles.VerifyContents(new MemoryStream(Encoding.UTF8.GetBytes("abcxef")), chunked, default), "installed_files_modified");
    Refuses(() => StoreOwnedInstallFiles.ValidateChecksum(chunked with { Size = 5 }), "uninstall_manifest_invalid");
    Refuses(() => StoreOwnedInstallFiles.ValidateChecksum(chunked with { Chunks = [new(0, Fixture.Md5(""))] }), "uninstall_manifest_invalid");
    Refuses(() => StoreOwnedInstallFiles.ValidateChecksum(chunked with { Chunks = [new(6, "not-a-digest")] }), "uninstall_manifest_invalid");
    Console.WriteLine($"PASS {checks} GOG supportData/provider checks; synthetic owned files/metadata only");
}
catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }

sealed class Fixture
{
    internal const string Id = "1207658901";
    internal string Path { get; } internal string State { get; } internal Guid Job { get; } = Guid.NewGuid();
    internal string Executable => System.IO.Path.Combine(Path, "DOSBOX", "game.exe");
    internal string Receipt => System.IO.Path.Combine(State, "gog", "support-owned-" + Id + ".json");
    internal List<StoreOwnedInstallFile> Files { get; } = [];
    private readonly string depot;
    private readonly Dictionary<string, byte[]> responses = [];
    internal bool DisableDependencyNetwork { get; set; }
    internal Fixture(string directory, string? script, bool bundledDependency = false)
    {
        Path = directory; State = directory + "-state"; Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory, ".playhub-install-owner"), Job.ToString("D"));
        Add("DOSBOX/game.exe", "inert-executable", false, chunks: true);
        Add("gog-support/" + Id + "/app/dosboxT2K.conf", "[sdl]\nfull=1", true, chunks: true);
        Add("gog-support/" + Id + "/app/dosboxT2K_single.conf", "[autoexec]\nmount c ..\ntyrian", true);
        Add("goggame-" + Id + ".info", JsonSerializer.Serialize(new { gameId = Id, rootGameId = Id, playTasks = new[] { new { isPrimary = true, type = "FileTask", path = "DOSBOX\\game.exe", workingDir = "DOSBOX", arguments = "-conf \"..\\dosboxT2K.conf\" -conf \"..\\dosboxT2K_single.conf\" -noconsole -c \"exit\"" } } }), false);
        Add("goggame-" + Id + ".script", script ?? Script(Copy("{supportDir}/app", "{app}", "folder") + "," + """{"install":{"action":"supportData","arguments":{"type":"folder","target":"{app}/cloud_saves"}}}, {"install":{"action":"setRegistry","arguments":{"root":"HKEY_LOCAL_MACHINE","subkey":"Software\\GOG.com\\Games\\{productID}","valueName":"DOSBOXCONF"}}}"""), false);
        var items = Files.Select(file => new { type = "DepotFile", path = file.RelativePath.StartsWith("gog-support/") ? file.RelativePath[("gog-support/" + Id + "/").Length..] : file.RelativePath,
            flags = file.RelativePath.StartsWith("gog-support/") ? new[] { "support" } : Array.Empty<string>(), md5 = file.Hash,
            chunks = file.Chunks?.Select(c => new { size = c.Size, md5 = c.Hash }).ToArray() ?? new[] { new { size = file.Size, md5 = file.Hash! } } }).ToArray();
        depot = JsonSerializer.Serialize(new { depot = new { items = bundledDependency ? items.Where(x => x.path != "DOSBOX/game.exe").ToArray() : items } });
        if (bundledDependency)
        {
            string Compressed(string json)
            {
                using var output = new MemoryStream(); using (var zip = new ZLibStream(output, CompressionLevel.SmallestSize, true)) { var bytes = Encoding.UTF8.GetBytes(json); zip.Write(bytes); }
                var encoded = output.ToArray(); var hash = Convert.ToHexString(MD5.HashData(encoded)).ToLowerInvariant();
                var url = "https://gog-cdn-fastly.gog.com/content-system/v2/dependencies/meta/" + hash[..2] + "/" + hash.Substring(2, 2) + "/" + hash; responses.Add(url, encoded); return url;
            }
            var runtime = Compressed(JsonSerializer.Serialize(new { depot = new { items = items.Where(x => x.path == "DOSBOX/game.exe").ToArray() } }));
            var repository = Compressed(JsonSerializer.Serialize(new { depots = new[] { new { dependencyId = "FixtureDOSBox", executable = new { path = "", arguments = "" }, manifest = new Uri(runtime).Segments[^1] } } }));
            responses.Add("https://content-system.gog.com/dependencies/repository?generation=2", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { repository_manifest = repository })));
        }
        var manifests = System.IO.Path.Combine(State, "gog", "native", "heroic_gogdl", "manifests"); Directory.CreateDirectory(manifests);
        File.WriteAllText(System.IO.Path.Combine(manifests, Id), JsonSerializer.Serialize(new { version = 2, baseProductId = Id, platform = "windows", HGLInstallLanguage = "en-US", scriptInterpreter = true, dependencies = bundledDependency ? new[] { "FixtureDOSBox" } : Array.Empty<string>(), products = new[] { new { temp_executable = "" } }, depots = new[] { new { productId = Id, languages = new[] { "*" }, manifest = new string('a', 32) } } }));
        StoreCredentialVault.Write(System.IO.Path.Combine(State, "gog", "credentials.dpapi"), """{"fixture":{"user_id":"synthetic-only","access_token":"synthetic-only"}}""");
    }
    internal void Add(string name, string value, bool support, bool chunks = false)
    {
        var bytes = Encoding.UTF8.GetBytes(value); var file = System.IO.Path.Combine(Path, name.Replace('/', '\\')); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes);
        if (chunks) { var half = bytes.Length / 2; Files.Add(new(name, bytes.Length, null, null, [new(half, Convert.ToHexString(MD5.HashData(bytes.AsSpan(0, half)))), new(bytes.Length - half, Convert.ToHexString(MD5.HashData(bytes.AsSpan(half))))])); }
        else Files.Add(new(name, bytes.Length, "MD5", Convert.ToHexString(MD5.HashData(bytes))));
    }
    internal StoreCliProvider Provider() => new(StoreKind.Gog, Environment.GetEnvironmentVariable("PLAYHUB_TEST_RUNTIME") ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../Playhub.StoreHost/Runtime")), State, Environment.ProcessPath!, new HttpClient(new Http(depot, responses, () => DisableDependencyNetwork)), (_, _, _, _) => throw new Exception("no real CLI query"));
    internal void Apply() => StoreGogSupportData.Apply(Receipt, Path, Id, Job, "en-US", true, Files, default);
    internal bool ConfigsPresent() => File.Exists(System.IO.Path.Combine(Path, "dosboxT2K.conf")) && File.Exists(System.IO.Path.Combine(Path, "dosboxT2K_single.conf"));
    internal static string Script(string actions) => "{\"productId\":\"" + Id + "\",\"actions\":[" + actions + "]}";
    internal static string Copy(string source, string target, string type) => JsonSerializer.Serialize(new { install = new { action = "supportData", arguments = new { source, target, type, overwrite = true } }, languages = new[] { "*" } });
    internal static string Md5(string text) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)));
}
sealed class Http(string depot, IReadOnlyDictionary<string, byte[]> responses, Func<bool> disabled) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (responses.TryGetValue(uri.AbsoluteUri, out var bytes))
        {
            if (disabled()) throw new Exception("dependency network must not run during receipt-backed uninstall");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request });
        }
        if (uri.Host != "gog-cdn-fastly.gog.com" || uri.AbsolutePath.Contains("/dependencies/")) throw new Exception("no unrecognized network");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(depot), RequestMessage = request });
    }
}
