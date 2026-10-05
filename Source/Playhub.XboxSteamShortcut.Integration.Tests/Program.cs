using System.Reflection;
using Playhub.Models;
using Playhub.Services;

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
    Console.WriteLine($"Actual UwpXboxService isolated checks: {passed} passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
