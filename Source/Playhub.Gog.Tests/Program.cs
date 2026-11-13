using System.Text.Json;
using Playhub.Services;

// Actual parser methods + inert files only. No registry reads or game execution.
string temp = Path.GetFullPath(Path.GetTempPath());
string sandbox = Path.Combine(temp, "Playhub-Gog-Parser-" + Guid.NewGuid().ToString("N"));
string install = Path.Combine(sandbox, "A Game With Spaces");
Directory.CreateDirectory(Path.Combine(install, "bin"));
string executable = Path.Combine(install, "bin", "game.exe");
File.WriteAllBytes(executable, new byte[] { 0 });
const string id = "1234567890";
string manifest = Path.Combine(install, $"goggame-{id}.info");
int checks = 0;
void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
object LaunchTask(string path = "bin/game.exe", object? type = null, string working = "bin", bool primary = true) =>
    new { isPrimary = primary, type = type ?? "FileTask", path, workingDir = working, arguments = "--language \"it IT\" --flag" };
void Write(object? tasks = null, object? gameId = null, object? rootId = null) =>
    File.WriteAllText(manifest, JsonSerializer.Serialize(new { gameId = gameId ?? id, rootGameId = rootId ?? id,
        name = "Manifest Title", playTasks = tasks ?? new[] { LaunchTask() } }));
Playhub.Models.UwpGameEntry? Read() => GogService.FromUninstallRecord(id + "_is1", "GOG.com", "Registered Title", install);
try
{
    Write(); var game = Read(); Check(game is not null, "exact uninstall record accepted");
    Check(game!.Name == "Registered Title", "registered display name retained");
    Check(game.LocalExecutablePath == executable && game.Executable == executable && game.Aumid == executable, "same existing executable identity");
    Check(game.SourceWorkingDirectory == Path.Combine(install, "bin"), "declared work directory");
    Check(game.SourceLaunchArguments == "--language \"it IT\" --flag", "quoted arguments preserved");
    Check(game.IsLocalExecutable && game.FileSize == 1, "inert file inspected only");
    Check(game.SteamIdentity == "", "existing shortcut identity scheme unchanged");
    Check(GogService.FromUninstallRecord(id + "_is1", "GOG.com", "", install)!.Name == "Manifest Title", "manifest title fallback");
    Check(GogService.FromUninstallRecord(id + "_is1", "Other", "", install) is null, "foreign publisher refused");
    foreach (string key in new[] { id, "GOGPACK" + id + "_is1", id + "_is1-extra", "../x_is1", "_is1" })
        Check(GogService.FromUninstallRecord(key, "GOG.com", "", install) is null, "non-installer key refused: " + key);
    Check(GogService.ReadInstalledManifest("relative", id, "") is null, "relative installation refused");
    Check(GogService.ReadInstalledManifest(install, "../123", "") is null, "manifest name injection refused");
    Write(rootId: "999"); Check(Read() is null, "DLC rootGameId differs");
    Write(gameId: "999"); Check(Read() is null, "mismatched gameId refused");
    Write(rootId: 123); Check(Read() is null, "unknown root ID type refused");
    Write(tasks: Array.Empty<object>()); Check(Read() is null, "no primary task refused");
    Write(tasks: new[] { LaunchTask(), LaunchTask() }); Check(Read() is null, "ambiguous primary refused");
    Write(tasks: new[] { LaunchTask(primary: false), LaunchTask() }); Check(Read() is not null, "non-primary support task ignored");
    Write(tasks: new[] { LaunchTask(type: "URLTask") }); Check(Read() is null, "URL task not imported as executable");
    Write(tasks: new[] { LaunchTask(type: 0) }); Check(Read() is not null, "numeric FileTask supported");
    Write(tasks: new[] { LaunchTask(type: 1) }); Check(Read() is null, "numeric URLTask refused");
    Write(tasks: new[] { LaunchTask(path: "bin/missing.exe") }); Check(Read() is null, "missing executable refused");
    Write(tasks: new[] { LaunchTask(working: "missing") }); Check(Read() is null, "missing working directory refused");
    Write(tasks: new[] { LaunchTask(working: "../") }); Check(Read() is null, "working directory escape refused");
    string sibling = Path.Combine(sandbox, "A Game With Spaces extra"); Directory.CreateDirectory(sibling);
    string foreign = Path.Combine(sibling, "foreign.exe"); File.WriteAllBytes(foreign, new byte[] { 0 });
    Write(tasks: new[] { LaunchTask(path: foreign) }); Check(Read() is null, "sibling prefix is outside install");
    Write(tasks: new[] { LaunchTask(path: "../A Game With Spaces extra/foreign.exe") }); Check(Read() is null, "relative executable escape refused");
    string text = Path.Combine(install, "not-a-game.txt"); File.WriteAllText(text, "inert");
    Write(tasks: new[] { LaunchTask(path: text) }); Check(Read() is null, "non-executable refused");
    Write(tasks: new[] { LaunchTask(working: "") }); Check(Read()!.SourceWorkingDirectory == install, "default workdir is install");
    var legacy = GogService.ReadPrimaryLaunch(install, id, executable);
    Check(legacy?.WorkingDirectory == install && legacy?.Arguments == "--language \"it IT\" --flag", "existing launch helper regression");
    File.WriteAllText(manifest, JsonSerializer.Serialize(new { playTasks = new[] { new { isPrimary = true, path = "bin/game.exe" } } }));
    Check(Read() is not null, "older manifest with implicit FileTask and absent optional IDs");
    File.WriteAllText(manifest, JsonSerializer.Serialize(new { playTasks = new[] { new { isPrimary = true, path = "bin/game.exe", arguments = 123 } } }));
    Check(Read() is null, "unknown arguments type refused");
    File.WriteAllText(manifest, JsonSerializer.Serialize(new { playTasks = new[] { new { isPrimary = true, path = "bin/game.exe", workingDir = 123 } } }));
    Check(Read() is null, "unknown workdir type refused");
    File.WriteAllText(manifest, JsonSerializer.Serialize(new { name = " ", playTasks = new[] { new { isPrimary = true, path = "bin/game.exe" } } }));
    Check(GogService.FromUninstallRecord(id + "_is1", "GOG.com", "", install)?.Name == "game", "blank registry and whitespace manifest title fall back to executable basename");
    File.WriteAllText(manifest, "{broken"); Check(Read() is null, "malformed JSON unavailable");
    File.WriteAllText(manifest, "[]"); Check(Read() is null, "unknown root shape unavailable");
    File.WriteAllText(manifest, new string(' ', 1024 * 1024 + 1)); Check(Read() is null, "bounded manifest size");
    File.Delete(manifest); Check(Read() is null, "no inferred exe without a declared manifest");
    Console.WriteLine($"{checks} GOG installed-record checks PASS; no registry/UI/network/process operations.");
}
finally
{
    string actual = Path.GetFullPath(sandbox);
    if (actual.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(actual).StartsWith("Playhub-Gog-Parser-", StringComparison.Ordinal))
        Directory.Delete(actual, true);
}
