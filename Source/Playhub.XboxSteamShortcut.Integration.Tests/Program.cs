using System.Reflection;
using System.Text.Json;
using Playhub.Models;
using Playhub.Services;
using VDFParser.Models;

// Invoke only the actual private artwork write phase with isolated fixture paths.
// No service discovery, activation, network or Steam edit-session method is called.
var apply = typeof(UwpXboxService).GetMethod("ApplySelectedArtworkForUser", BindingFlags.Static | BindingFlags.NonPublic)!;
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
string root = Path.Combine(Path.GetTempPath(), "playhub-xbox-service-art-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    string selection = Path.Combine(root, "selected.png");
    File.WriteAllBytes(selection, [1, 2, 3, 4]);
    var game = new UwpGameEntry { SteamGridDbCoverPath = selection, SteamGridDbBannerPath = selection, SteamGridDbHeroPath = selection, SteamGridDbLogoPath = selection };
    uint id = 2485417537;
    var names = new[] { id + "p", id.ToString(), id + "_hero", id + "_logo" };
    foreach (string profile in new[] { "first", "second" })
    {
        string user = Path.Combine(root, profile), grid = Path.Combine(user, "config", "grid");
        Directory.CreateDirectory(grid);
        foreach (string name in names) File.WriteAllText(Path.Combine(grid, name + ".jpeg"), profile + name);
        apply.Invoke(null, [game, user, id]);
        foreach (string name in names)
            Check(File.ReadAllText(Path.Combine(grid, name + ".jpeg")) == profile + name && !File.Exists(Path.Combine(grid, name + ".png")), "real export retains " + profile + "/" + name);
        File.Delete(Path.Combine(grid, id + "_hero.jpeg"));
        apply.Invoke(null, [game, user, id]);
        Check(File.ReadAllBytes(Path.Combine(grid, id + "_hero.png")).SequenceEqual(File.ReadAllBytes(selection)), "real export fills only missing hero for " + profile);
    }
    string disabled = Path.Combine(root, "disabled");
    game.SteamGridDbArtworkDisabled = true;
    apply.Invoke(null, [game, disabled, id]);
    Check(!Directory.Exists(disabled), "artwork-disabled export creates no files");
    var baseline = Path.Combine(root, "steam.png");
    var replacement = Path.Combine(root, "replacement.png");
    var stale = Path.Combine(root, "stale.png");
    File.WriteAllText(baseline, "actual Steam asset");
    File.WriteAllText(replacement, "explicit replacement");
    File.WriteAllText(stale, "old automatic cache");
    var current = new UwpGameEntry { SteamGridDbCoverPath = stale };
    var actual = new Dictionary<string, string> { ["cover"] = baseline, ["banner"] = baseline, ["hero"] = baseline, ["logo"] = baseline, ["icon"] = baseline };
    ImportedArtworkSelection.RefreshSteam(current, actual);
    foreach (var type in ImportedArtworkSelection.Types)
        Check(ImportedArtworkSelection.Get(current, type) == baseline, "actual Steam " + type + " wins over automatic cache");
    ImportedArtworkSelection.ClearAutomatic(current, "cover");
    Check(current.SteamGridDbCoverPath == baseline, "changing cover shape retains Steam artwork");
    ImportedArtworkSelection.Choose(current, "cover", replacement);
    ImportedArtworkSelection.RefreshSteam(current, actual);
    Check(current.SteamGridDbCoverPath == replacement, "refresh does not erase user's replacement");
    Check(ImportedArtworkSelection.Current(current, actual)["cover"] == replacement, "card and summary share user's replacement");
    ImportedArtworkSelection.ClearAutomatic(current, "cover");
    Check(current.SteamGridDbCoverPath == replacement, "scraper preference does not erase explicit cover");
    ImportedArtworkSelection.Choose(current, "cover", null);
    ImportedArtworkSelection.RefreshSteam(current, actual);
    Check(current.SteamGridDbCoverPath == "" && !ImportedArtworkSelection.Current(current, actual).ContainsKey("cover"), "explicit removal cannot be resurrected by refresh");
    Check(ImportedArtworkSelection.Current(current, actual)["hero"] == baseline, "removing cover preserves other artwork categories");
    var gridOnly = Path.Combine(root, "remove-grid"); Directory.CreateDirectory(gridOnly);
    File.WriteAllText(Path.Combine(gridOnly, id + "p.png"), "original exact cover");
    File.WriteAllText(Path.Combine(gridOnly, id + "_hero.png"), "unrelated hero");
    File.WriteAllText(Path.Combine(gridOnly, (id + 1) + "p.png"), "other game's cover");
    var backup = Path.Combine(root, "removed-backup");
    ImportedArtworkSelection.RemoveExactFiles(gridOnly, id, "cover", backup, default);
    Check(!File.Exists(Path.Combine(gridOnly, id + "p.png")) && File.ReadAllText(Path.Combine(backup, id + "p.png")) == "original exact cover", "exact removal keeps recoverable original");
    Check(File.ReadAllText(Path.Combine(gridOnly, id + "_hero.png")) == "unrelated hero" && File.ReadAllText(Path.Combine(gridOnly, (id + 1) + "p.png")) == "other game's cover", "removal preserves other types and games");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    bool refused = false;
    try { ImportedArtworkSelection.RemoveExactFiles(gridOnly, id, "hero", Path.Combine(root, "cancel-backup"), cancelled.Token); } catch (OperationCanceledException) { refused = true; }
    Check(refused && File.Exists(Path.Combine(gridOnly, id + "_hero.png")), "cancelled removal changes no artwork");
    var pending = new UwpGameEntry();
    ImportedArtworkSelection.Choose(pending, "cover", replacement);
    var deferredUser = Path.Combine(root, "deferred-user");
    var deferredGrid = Path.Combine(deferredUser, "config", "grid"); Directory.CreateDirectory(deferredGrid);
    File.WriteAllText(Path.Combine(deferredGrid, id + "p.jpeg"), "old Steam baseline");
    File.WriteAllText(Path.Combine(deferredGrid, id + "_hero.jpeg"), "retain Steam hero");
    apply.Invoke(null, [pending, deferredUser, id]);
    Check(!File.Exists(Path.Combine(deferredGrid, id + "p.jpeg")) && File.ReadAllText(Path.Combine(deferredGrid, id + "p.png")) == "explicit replacement", "deferred export writes an explicit replacement over Steam baseline");
    ImportedArtworkSelection.Choose(pending, "cover", null);
    apply.Invoke(null, [pending, deferredUser, id]);
    Check(!File.Exists(Path.Combine(deferredGrid, id + "p.png")), "deferred export honors explicit cover removal");
    Check(File.ReadAllText(Path.Combine(deferredGrid, id + "_hero.jpeg")) == "retain Steam hero", "deferred removal preserves other asset categories");
    File.WriteAllText(Path.Combine(deferredGrid, id + "p.jpeg"), "atomic original");
    string atomicBackup = Path.Combine(root, "atomic-backup");
    ImportedArtworkSelection.ReplaceExactFile(deferredGrid, id, "cover", replacement, atomicBackup, default);
    Check(File.ReadAllText(Path.Combine(deferredGrid, id + "p.png")) == "explicit replacement", "atomic replacement commits complete bytes");
    Check(File.ReadAllText(Path.Combine(atomicBackup, id + "p.jpeg")) == "atomic original", "replacement retains recoverable original");
    Check(File.ReadAllText(Path.Combine(deferredGrid, id + "_hero.jpeg")) == "retain Steam hero", "replacement preserves unrelated hero");
    bool missingRefused = false;
    try { ImportedArtworkSelection.ReplaceExactFile(deferredGrid, id, "cover", Path.Combine(root, "missing.png"), Path.Combine(root, "missing-backup"), default); } catch (FileNotFoundException) { missingRefused = true; }
    Check(missingRefused && File.ReadAllText(Path.Combine(deferredGrid, id + "p.png")) == "explicit replacement", "missing replacement cannot destroy current Steam cover");
    bool canceledReplace = false;
    try { ImportedArtworkSelection.ReplaceExactFile(deferredGrid, id, "cover", baseline, Path.Combine(root, "canceled-replace"), cancelled.Token); } catch (OperationCanceledException) { canceledReplace = true; }
    Check(canceledReplace && File.ReadAllText(Path.Combine(deferredGrid, id + "p.png")) == "explicit replacement", "cancelled replacement retains current cover");
    ImportedArtworkSelection.ReplaceExactFile(deferredGrid, id, "cover", Path.Combine(deferredGrid, id + "p.png"), Path.Combine(root, "same-path"), default);
    Check(!Directory.Exists(Path.Combine(root, "same-path")), "same exact selected Steam file creates no backup or writes");
    Check(!Directory.EnumerateFiles(deferredGrid, ".playhub-artwork-*.tmp").Any(), "replacement failure and success leave no temporary files");
    VerifyGogLaunch(root);
    VerifyLocalRouting(root);
    passed += EpicImportChecks.Run(root);
    passed += EpicProtocolExportChecks.Run(root);
    Console.WriteLine($"Actual UwpXboxService isolated checks: {passed} passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

void VerifyLocalRouting(string root)
{
    string executable = Path.Combine(root, "Epic game", "Binaries", "Win64", "Fixture.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
    File.WriteAllBytes(executable, []);
    const string rawArguments = "--profile=\"two  spaces\"  \"\" --quoted=\\\"value\\\"";
    string manifest = Path.Combine(root, "epic.item");
    File.WriteAllText(manifest, JsonSerializer.Serialize(new
    {
        InstallLocation = Path.Combine(root, "Epic game"), LaunchExecutable = "Binaries/Win64/Fixture.exe",
        DisplayName = "Epic fixture", LaunchCommand = rawArguments, bIsApplication = true, AppCategories = new[] { "games" },
        CatalogNamespace = "fixture-space", CatalogItemId = "fixture-item", AppName = "fixture-app"
    }));
    var parse = typeof(EpicGamesService).GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic)!;
    var epic = (UwpGameEntry?)parse.Invoke(null, [manifest]);
    Check(epic is { IsLocalExecutable: true } && epic.LocalExecutablePath == executable && epic.SourceLaunchArguments == rawArguments,
        "Epic manifest retains its exact executable and opaque launch command");
    foreach (var local in new[] { epic!, new UwpGameEntry { Name = "Manual fixture", IsLocalExecutable = true,
        LocalExecutablePath = executable, SourceLaunchArguments = rawArguments } })
    {
        const int originalAppId = unchecked((int)0xa1234567);
        string target = "\"" + executable + "\"";
        var direct = new VDFEntry { appid = originalAppId, Exe = target, LaunchOptions = rawArguments,
            StartDir = "\"D:\\Original working directory\"", Tags = ["User collection"] };
        string firstOptions = UwpXboxService.BuildLaunchOptions(local, direct, target);
        var managed = XboxSteamShortcut.Create(direct, 12, local.Name, "C:\\Playhub\\Playhub.GameSession.exe",
            UwpXboxService.LocalStartDirectory(local), "", firstOptions, true, 0);
        Check(managed.appid == originalAppId && managed.StartDir == direct.StartDir
            && firstOptions == (local.EpicAppName.Length > 0
                ? global::Playhub.GameSession.EpicShortcutArguments.Build(local.EpicNamespace, local.EpicCatalogItemId, local.EpicAppName, executable)
                : "--game " + target + " " + rawArguments),
            local.Name + " wraps the original target once and retains AppID, directory and raw arguments");
        Check(UwpXboxService.BuildLaunchOptions(local, managed, target) == firstOptions,
            local.Name + " reimport cannot add a second session wrapper");
        Check(managed.Tags.SequenceEqual(direct.Tags) && managed.AllowDesktopConfig == 0 && managed.AllowOverlay == 1,
            local.Name + " keeps user collections and the shared Steam Input/overlay routing policy");
    }
}

void VerifyGogLaunch(string root)
{
    const string gameId = "1207658695";
    string install = Path.Combine(root, "GOG game");
    string executable = Path.Combine(install, "Engine", "Fixture.exe");
    string workingDirectory = Path.Combine(install, "Game data");
    Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
    Directory.CreateDirectory(workingDirectory);
    File.WriteAllBytes(executable, []);
    string manifest = Path.Combine(install, $"goggame-{gameId}.info");
    const string sourceArguments = "-conf \"game config.conf\" -savedir \"Slots\\One\"";
    void WriteTask(string path, string workingDir) => File.WriteAllText(manifest, JsonSerializer.Serialize(new
    {
        playTasks = new[] { new { path, workingDir, isPrimary = true, arguments = sourceArguments } }
    }));

    WriteTask("Engine/Fixture.exe", "Game data");
    var launch = GogService.ReadPrimaryLaunch(install, gameId, executable);
    Check(launch == (workingDirectory, sourceArguments), "GOG matching primary task supplies one coherent directory and arguments pair");
    var game = new UwpGameEntry
    {
        IsLocalExecutable = true, LocalExecutablePath = executable,
        SourceWorkingDirectory = launch?.WorkingDirectory, SourceLaunchArguments = launch!.Value.Arguments
    };
    string target = $"\"{executable}\"";
    var entry = XboxSteamShortcut.Create(null, 3, "GOG fixture", "Playhub.GameSession.exe",
        UwpXboxService.LocalStartDirectory(game), "", UwpXboxService.BuildLaunchOptions(game, null, target), true, 0);
    using var vdf = new MemoryStream(VDFParser.VDFSerializer.Serialize(new[] { entry }));
    VDFEntry saved = VDFParser.VDFParser.Parse(vdf)[0];
    Check(saved.StartDir == $"\"{workingDirectory}\"" && saved.LaunchOptions == $"--game {target} {sourceArguments}",
        "Steam shortcut stores the game's actual working directory and exact launch options");
    saved.StartDir = "\"D:\\User's custom working directory\"";
    var reimported = XboxSteamShortcut.Create(saved, 4, "GOG fixture", "Playhub.GameSession.exe",
        UwpXboxService.LocalStartDirectory(game), "", UwpXboxService.BuildLaunchOptions(game, saved, target), true, 1);
    Check(reimported.StartDir == saved.StartDir && reimported.appid == saved.appid && reimported.LaunchOptions == saved.LaunchOptions,
        "GOG reimport preserves the user's Steam directory, identity and launch options");
    WriteTask("Engine/Fixture.exe", "");
    Check(GogService.ReadPrimaryLaunch(install, gameId, executable)?.WorkingDirectory == install,
        "GOG primary task defaults to the installation directory");
    WriteTask("Engine/Other.exe", "Game data");
    Check(GogService.ReadPrimaryLaunch(install, gameId, executable) is null,
        "GOG task for another executable cannot override registry launch metadata");
    WriteTask("Engine/Fixture.exe", "..");
    Check(GogService.ReadPrimaryLaunch(install, gameId, executable) is null,
        "GOG task cannot use a relative working directory outside the installation");
    game.SourceWorkingDirectory = null;
    Check(UwpXboxService.LocalStartDirectory(game) == $"\"{Path.GetDirectoryName(executable)}\"",
        "Unspecified source directory keeps the normal executable-folder fallback");
}
