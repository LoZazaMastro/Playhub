using Playhub.Shared;

var root = Path.Combine(Path.GetTempPath(), "playhub-optional-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var exe = Path.Combine(root, "GamingMode.exe");
var marker = Path.Combine(root, "preferences", GamingModeInstallationPolicy.DisabledMarkerName);
var checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
try
{
    Check(!GamingModeInstallationPolicy.ShouldMaintainInstallation(exe, marker), "Fresh installations do not auto-install the optional agent");
    File.WriteAllText(exe, "fixture");
    Check(GamingModeInstallationPolicy.ShouldMaintainInstallation(exe, marker), "Existing opted-in installations receive updates");
    GamingModeInstallationPolicy.Disable(marker);
    Check(!GamingModeInstallationPolicy.ShouldMaintainInstallation(exe, marker), "Partial removal cannot trigger automatic repair or restart");
    File.Delete(exe);
    Check(!GamingModeInstallationPolicy.ShouldMaintainInstallation(exe, marker), "Completed uninstall cannot be undone by an app update");
    GamingModeInstallationPolicy.Enable(marker);
    Check(!GamingModeInstallationPolicy.ShouldMaintainInstallation(exe, marker), "Clearing opt-out alone does not make missing files installed");
    File.WriteAllText(exe, "fixture");
    Check(GamingModeInstallationPolicy.ShouldMaintainInstallation(exe, marker), "Explicit installation re-enables maintenance");
    Check(GamingModeInstallationPolicy.OwnsExecutable(exe, root), "Owned executable accepted");
    Check(!GamingModeInstallationPolicy.OwnsExecutable(Path.Combine(root, "nested", "GamingMode.exe"), root), "Nested executable not owned");
    Check(!GamingModeInstallationPolicy.OwnsExecutable(Path.Combine(root + "-other", "GamingMode.exe"), root), "Prefix-sharing installation not owned");
    Check(!GamingModeInstallationPolicy.OwnsExecutable(Path.Combine(root, "QuickSettingsAgent.exe"), root), "Quick Settings helper excluded from agent removal");
    Check(!GamingModeInstallationPolicy.OwnsExecutable(null, root), "Unreadable process path never authorizes termination");
    Console.WriteLine($"{checks}/{checks} optional Gaming Mode checks passed.");
}
finally { Directory.Delete(root, recursive: true); }
