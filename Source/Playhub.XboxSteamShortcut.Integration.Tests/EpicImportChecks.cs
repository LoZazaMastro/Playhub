using System.Text.Json;
using Playhub.Services;

internal static class EpicImportChecks
{
    internal static int Run(string fixtureRoot)
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
        string common = Path.Combine(fixtureRoot, "ProgramData");
        string standard = Path.Combine(common, "Epic", "EpicGamesLauncher", "Data");
        string custom = Path.Combine(fixtureRoot, "Custom Epic Data"), old = Path.Combine(fixtureRoot, "Old Data");
        string[] Resolve(Func<Microsoft.Win32.RegistryView, string?> read, Func<string, bool>? exists = null) =>
            EpicManifestPaths.Resolve(read, exists ?? (_ => true), common).ToArray();
        var views = new List<Microsoft.Win32.RegistryView>();
        var paths = Resolve(view => { views.Add(view); return view == Microsoft.Win32.RegistryView.Registry64 ? custom : old; });
        Check(views.SequenceEqual(new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 }), "explicit registry64 view first");
        Check(paths.SequenceEqual(new[] { Path.Combine(custom, "Manifests"), Path.Combine(old, "Manifests"), Path.Combine(standard, "Manifests") }), "custom registry roots precede standard fallback");
        Check(Resolve(_ => standard + Path.DirectorySeparatorChar).Length == 1, "duplicate roots canonicalized");
        Check(Resolve(_ => "relative").Single() == Path.Combine(standard, "Manifests"), "relative root refused");
        Check(Resolve(_ => "https://invalid/").Single() == Path.Combine(standard, "Manifests"), "URI root refused");
        Check(Resolve(_ => null).Single() == Path.Combine(standard, "Manifests"), "missing root uses normal fallback");
        Check(Resolve(_ => throw new UnauthorizedAccessException()).Single() == Path.Combine(standard, "Manifests"), "denied registry uses normal fallback");
        Check(Resolve(_ => custom, _ => false).Length == 0, "missing manifest directories not synthesized");
        Check(Resolve(_ => custom, path => path != Path.Combine(custom, "Manifests")).Single() == Path.Combine(standard, "Manifests"), "absent custom retains normal fallback");
        return count + RunInstalled(fixtureRoot);
    }
    private static int RunInstalled(string fixtureRoot)
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        string install = Path.Combine(fixtureRoot, "Fortnite"), helper = Path.Combine(fixtureRoot, "InstalledItems");
        string metadata = Path.Combine(install, ".egstore"), binaries = Path.Combine(install, "FortniteGame", "Binaries", "Win64");
        Directory.CreateDirectory(metadata); Directory.CreateDirectory(binaries); Directory.CreateDirectory(helper);
        string id = "11111111222233334444555555555555", record = Path.Combine(helper, id + ".egi");
        string manifest = Path.Combine(metadata, id + ".manifest"), executable = Path.Combine(binaries, "FortniteBootstrapper.exe");
        File.WriteAllBytes(executable, []);
        byte[] manifestBytes = new byte[41];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(manifestBytes, 0x44BEC00C);
        File.WriteAllBytes(manifest, manifestBytes);
        var data = new Dictionary<string, object?> { ["version"] = "++Fortnite+Release-fixture-Windows",
            ["launchExe"] = "FortniteGame/Binaries/Win64/FortniteBootstrapper.exe",
            ["launchCommand"] = "-fixture-normal-bootstrap-argument", ["buildSize"] = 37170389839L };
        var values = new Dictionary<string, object?> { ["installationId"] = id, ["state"] = "Installed",
            ["dir"] = install, ["metaDir"] = metadata, ["manifestPath"] = manifest, ["platform"] = "Windows",
            ["sandboxId"] = "fn", ["itemId"] = "4fe75bbc5a674f4f9b356b5c90567da5", ["artifactId"] = "Fortnite",
            ["stagedVersion"] = "", ["pendingTags"] = Array.Empty<string>(), ["manifestData"] = data,
            ["pendingManifestData"] = new { version = "", launchExe = "", launchCommand = "", buildSize = 0 } };
        void Write(Dictionary<string, object?>? v = null) => File.WriteAllText(record,
            JsonSerializer.Serialize(new { v4 = v ?? values }));
        Dictionary<string, object?> Change(string key, object? value) { var copy = new Dictionary<string, object?>(values); copy[key] = value; return copy; }
        Dictionary<string, object?> Data(string key, object? value) { var copy = new Dictionary<string, object?>(data); copy[key] = value; return Change("manifestData", copy); }
        bool Refused(Dictionary<string, object?> v) { Write(v); return EpicInstalledItem.TryParse(record) is null; }
        Write();
        var game = EpicInstalledItem.TryParse(record);
        Check(game is { Name: "Fortnite", IsLocalExecutable: true } && game.LocalExecutablePath == executable, "observed Fortnite Installed v4 schema is recognized");
        Check(game!.SourceLaunchArguments == "-fixture-normal-bootstrap-argument" && !game.LocalExecutablePath.Contains("Shipping"), "preserves the recorded bootstrapper and opaque command");
        Check(Refused(Change("state", "Incomplete")), "incomplete installation refused");
        Check(Refused(Change("state", "Uninstalled")), "uninstalled record refused");
        Check(Refused(Change("platform", "Mac")), "other platform refused");
        Check(Refused(Change("installationId", Guid.NewGuid().ToString("N"))), "filename and record GUID must agree");
        Check(Refused(Change("stagedVersion", "pending-update")), "staged version refused");
        Check(Refused(Change("stagedVersion", 4)), "malformed staged state refused");
        Check(Refused(Change("pendingTags", new[] { "pending" })), "pending transaction refused");
        Check(Refused(Change("pendingManifestData", new { version = "new-version" })), "pending manifest version refused");
        Check(Refused(Change("pendingManifestData", new { version = 1 })), "malformed pending version refused");
        Check(Refused(Change("artifactId", "")), "missing application identity refused");
        Check(Refused(Change("artifactId", "SupportTool")), "undocumented helper schema cannot admit other applications");
        Check(Refused(Change("sandboxId", "other-game")), "Fortnite compatibility cannot admit another catalog namespace");
        Check(Refused(Change("sandboxId", "ue")), "Unreal Engine installation not imported as game");
        Check(Refused(Change("bIsApplication", false)), "explicit non application refused");
        Check(Refused(Change("AppCategories", new[] { "dlc" })), "explicit DLC category refused");
        Check(Refused(Data("launchExe", "")), "non launchable DLC-style record refused");
        Check(Refused(Data("launchExe", "../../Outside.exe")), "launch path escape refused");
        Check(Refused(Data("launchExe", executable)), "absolute launch override refused");
        Check(Refused(Data("launchExe", "FortniteGame/Binaries/Win64/missing.exe")), "stale payload path refused");
        Check(Refused(Data("launchExe", "FortniteGame/Binaries/Win64/file.cmd")), "script target refused");
        Check(Refused(Data("version", "")), "missing installed version refused");
        Check(Refused(Data("launchCommand", 9)), "malformed launch command refused");
        Check(Refused(Data("buildSize", 0)), "empty installed build refused");
        Check(Refused(Change("dir", Path.Combine(fixtureRoot, "missing"))), "missing install root refused");
        Check(Refused(Change("metaDir", Path.Combine(fixtureRoot, ".egstore"))), "foreign metadata root refused");
        Check(Refused(Change("manifestPath", Path.Combine(metadata, "other.manifest"))), "foreign manifest identity refused");
        Write(); File.Move(manifest, manifest + ".held");
        Check(EpicInstalledItem.TryParse(record) is null, "missing completed manifest refused");
        File.Move(manifest + ".held", manifest); File.WriteAllBytes(manifest, new byte[41]);
        Check(EpicInstalledItem.TryParse(record) is null, "invalid completed manifest header refused");
        File.WriteAllBytes(manifest, manifestBytes);
        File.WriteAllText(record, "{"); Check(EpicInstalledItem.TryParse(record) is null, "truncated helper publication refused");
        File.WriteAllText(record, "{\"v5\":{}}"); Check(EpicInstalledItem.TryParse(record) is null, "unknown schema refused");
        Write();
        string launcher = Path.Combine(fixtureRoot, "LauncherManifests"); Directory.CreateDirectory(launcher);
        var scan = EpicGamesService.Scan(new[] { launcher }, helper);
        Check(scan.Count == 1 && scan[0].Name == "Fortnite", "actual scanner finds helper-only Fortnite");
        string item = Path.Combine(launcher, id + ".item");
        File.WriteAllText(item, JsonSerializer.Serialize(new { InstallLocation = install,
            LaunchExecutable = "FortniteGame/Binaries/Win64/FortniteBootstrapper.exe", DisplayName = "Preferred Launcher Name",
            LaunchCommand = "launcher-command", bIsApplication = true, AppCategories = new[] { "games" },
            CatalogNamespace = "fn", CatalogItemId = "4fe75bbc5a674f4f9b356b5c90567da5", AppName = "Fortnite" }));
        scan = EpicGamesService.Scan(new[] { launcher }, helper);
        Check(scan.Count == 1 && scan[0].Name == "Preferred Launcher Name" && scan[0].SourceLaunchArguments == "launcher-command", "launcher record wins without duplicate or argument overwrite");
        File.WriteAllText(item, "{");
        Check(EpicGamesService.Scan(new[] { launcher }, helper).Count == 0, "malformed newer launcher record cannot be revived by old helper metadata");
        File.WriteAllText(item, JsonSerializer.Serialize(new { InstallLocation = install,
            LaunchExecutable = "FortniteGame/Binaries/Win64/FortniteBootstrapper.exe", bIsApplication = false }));
        Check(EpicGamesService.Scan(new[] { launcher }, helper).Count == 0, "nonapplication launcher record cannot be revived by helper fallback");
        return count;
    }
}
