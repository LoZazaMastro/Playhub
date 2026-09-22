using Playhub.Services;

var root = Path.Combine(Path.GetTempPath(), "PlayhubRepairTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    var package = Path.Combine(root, "bundle");
    var installed = Path.Combine(root, "installed");
    Directory.CreateDirectory(package);
    Directory.CreateDirectory(installed);
    var source = Path.Combine(package, "GamingMode.exe");
    var target = Path.Combine(installed, "GamingMode.exe");
    Throws(() => GamingModeRepairPayload.IsCurrent(package, installed), "missing bundle is an error");
    File.WriteAllText(source, "AAAA");
    Check(!GamingModeRepairPayload.IsCurrent(package, installed), "missing installed agent");
    File.WriteAllText(target, "BBBB");
    File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(1));
    Check(!GamingModeRepairPayload.IsCurrent(package, installed), "same-size newer corruption detected");
    File.WriteAllText(target, "AAAA");
    File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddYears(-1));
    Check(GamingModeRepairPayload.IsCurrent(package, installed), "timestamps do not cause reinstall");
    Directory.CreateDirectory(Path.Combine(package, "assets"));
    File.WriteAllText(Path.Combine(package, "assets", "logo.ico"), "icon");
    Check(!GamingModeRepairPayload.IsCurrent(package, installed), "missing runtime asset");
    File.WriteAllText(Path.Combine(package, "install.ps1"), "must not execute");
    File.WriteAllText(Path.Combine(package, "GamingMode.UI.exe"), "legacy UI");
    File.WriteAllText(Path.Combine(package, "config.json"), "defaults");
    var config = Path.Combine(installed, "config.json");
    const string preferences = "{\"defaultMode\":\"Gaming\",\"nextBootMode\":\"Desktop\",\"gaming\":{\"closeExplorerInGamingMode\":false,\"allowExplorerCloseInGamingMode\":false,\"restoreExplorerOnDesktop\":false,\"unknown\":42},\"safety\":{\"apiPort\":48123}}";
    File.WriteAllText(config, preferences);
    File.WriteAllText(config + ".bak", "backup");
    Throws(() => GamingModeRepairPayload.Restore(package, installed, () => true), "running agent blocks replacement");
    Check(!File.Exists(Path.Combine(installed, "assets", "logo.ico")), "blocked repair makes no copies");
    GamingModeRepairPayload.Restore(package, installed, () => false);
    Check(GamingModeRepairPayload.IsCurrent(package, installed), "runtime payload restored and verified");
    Check(!File.Exists(Path.Combine(installed, "install.ps1")) && !File.Exists(Path.Combine(installed, "GamingMode.UI.exe")), "no standalone installer or UI restored");
    Check(GamingModeRepairPayload.ReadApiPort(config) == 48123, "configured API port");
    Check(File.ReadAllText(config) == preferences && File.ReadAllText(config + ".bak") == "backup", "all preferences and backup preserved byte-for-byte");
    foreach (var invalid in new[] { "null", "{}", "{", "{\"defaultMode\":\"Gaming\",\"gaming\":{},\"safety\":{\"apiPort\":70000}}" })
    {
        File.WriteAllText(config, invalid);
        Throws(() => GamingModeRepairPayload.ReadApiPort(config), "invalid config rejected without reset");
        Check(File.ReadAllText(config) == invalid, "invalid config preserved");
    }
    Check(GamingModeRepairPayload.IsExpectedStartup(target, "agent --boot", installed, target), "integrated boot command accepted");
    Check(!GamingModeRepairPayload.IsExpectedStartup(target, "", installed, target), "standalone UI command rejected");
    Check(!GamingModeRepairPayload.IsExpectedStartup(target, "agent", installed, target), "missing boot flag rejected");
    Check(!GamingModeRepairPayload.IsExpectedStartup(source, "agent --boot", package, target), "stale agent path rejected");
    Check(!GamingModeRepairPayload.IsExpectedStartup(target, "agent --boot", package, target), "stale working directory rejected");
    Check(GamingModeRepairPayload.IsKnownAgentStartup(source, "agent"), "obsolete owned startup recognized");
    Check(!GamingModeRepairPayload.IsKnownAgentStartup(source, "agent --custom"), "custom startup arguments preserved");
    var link = Path.Combine(root, "Gaming Mode Agent.lnk");
    Check(GamingModeRepairPayload.RepairStartupShortcut(link, target, true) == GamingModeStartupRepair.Unresolved && !File.Exists(link), "missing startup not recreated");
    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
    try
    {
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = source;
        shortcut.Arguments = "agent";
        shortcut.WorkingDirectory = package;
        shortcut.Description = "User description";
        shortcut.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        Check(GamingModeRepairPayload.RepairStartupShortcut(link, target, false) == GamingModeStartupRepair.Unresolved, "invalid payload prevents startup redirection");
        Check(GamingModeRepairPayload.RepairStartupShortcut(link, target, true) == GamingModeStartupRepair.Repaired, "obsolete startup actually repaired");
        shortcut = shell.CreateShortcut(link);
        Check(GamingModeRepairPayload.IsExpectedStartup((string)shortcut.TargetPath, (string)shortcut.Arguments, (string)shortcut.WorkingDirectory, target), "saved startup target arguments and directory verified");
        Check((string)shortcut.Description == "User description", "unrelated shortcut properties preserved");
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        Check(GamingModeRepairPayload.RepairStartupShortcut(link, target, true) == GamingModeStartupRepair.Healthy, "startup repair is idempotent");
        shortcut = shell.CreateShortcut(link);
        shortcut.Arguments = "agent --custom";
        shortcut.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        var linkBytes = File.ReadAllBytes(link);
        Check(GamingModeRepairPayload.RepairStartupShortcut(link, target, true) == GamingModeStartupRepair.Unresolved && linkBytes.SequenceEqual(File.ReadAllBytes(link)), "custom startup preserved byte-for-byte");
    }
    finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    var repository = new DirectoryInfo(AppContext.BaseDirectory);
    while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "Source", "Playhub"))) repository = repository.Parent;
    if (repository is null) throw new Exception("Repository not found for localization regression checks.");
    var serviceText = File.ReadAllText(Path.Combine(repository.FullName, "Source", "Playhub", "Services", "RepairService.cs"));
    var localization = File.ReadAllText(Path.Combine(repository.FullName, "Source", "Playhub", "Services", "LocalizationService.cs"));
    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(serviceText,
        "(?:notes\\.Add\\(|progress\\.Report\\(\\([0-9.]+, )\"([^\"]+)\""))
    {
        var key = match.Groups[1].Value;
        var line = localization.Split('\n').SingleOrDefault(line => line.TrimStart().StartsWith("[\"" + key + "\"] = V("));
        Check(line is not null, "localized repair message: " + key);
        Check(System.Text.RegularExpressions.Regex.Matches(line!.Split("= V(", 2)[1], "\"[^\"]*\"").Count == 11, "all translations: " + key);
    }
    Check(!serviceText.Contains("LoadConfigAsync") && !serviceText.Contains("SaveConfigAsync") && !serviceText.Contains("install.ps1") && !serviceText.Contains("Registry."), "orchestrator cannot reset preferences or invoke legacy installation");
    Console.WriteLine($"PASS: {passed} focused repair checks.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++;
}
void Throws(Action action, string name)
{
    try { action(); }
    catch { passed++; return; }
    throw new Exception("FAIL: " + name);
}
