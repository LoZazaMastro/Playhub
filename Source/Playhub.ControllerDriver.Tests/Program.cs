using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PlayhubSetup;

int passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name); passed++;
}

Check(ControllerDriverPolicy.BeforeInstall(ControllerDriverPresence.Ready)?.Status == ControllerDriverInstallStatus.Ready,
    "compatible driver is preserved without an installation");
Check(ControllerDriverPolicy.BeforeInstall(ControllerDriverPresence.Absent) is null,
    "only a completely absent driver is eligible for installation");
foreach (var presence in new[] { ControllerDriverPresence.ExistingUnavailable, ControllerDriverPresence.Unknown })
    Check(ControllerDriverPolicy.BeforeInstall(presence)?.Status == ControllerDriverInstallStatus.ExistingDriverPreserved,
        "existing or unreadable driver cannot be replaced: " + presence);
Check(ControllerDriverPolicy.AfterInstall(0, ControllerDriverPresence.Ready).IsReady,
    "successful setup requires an actual started bus");
foreach (var presence in new[] { ControllerDriverPresence.Absent, ControllerDriverPresence.ExistingUnavailable, ControllerDriverPresence.Unknown })
    Check(!ControllerDriverPolicy.AfterInstall(0, presence).IsReady,
        "successful exit alone cannot advertise working support: " + presence);
foreach (int exit in new[] { 3010, 1641 })
    Check(ControllerDriverPolicy.AfterInstall(exit, ControllerDriverPresence.Ready) is { Status: ControllerDriverInstallStatus.RestartRequired, IsReady: false },
        "restart is explicit and never automatic: " + exit);
foreach (int exit in new[] { 1223, 1602 })
    Check(ControllerDriverPolicy.AfterInstall(exit, ControllerDriverPresence.Ready).Status == ControllerDriverInstallStatus.PermissionRequired,
        "cancelled authorization remains visible: " + exit);
foreach (int exit in new[] { 1603, 1618, 1638, -1 })
    Check(ControllerDriverPolicy.AfterInstall(exit, ControllerDriverPresence.Ready) is { Status: ControllerDriverInstallStatus.InstallationFailed, IsReady: false },
        "failure is neither retried nor called ready: " + exit);
Check(ControllerDriverPolicy.SupportsRelay(Architecture.X64, true)
    && !ControllerDriverPolicy.SupportsRelay(Architecture.X64, false)
    && !ControllerDriverPolicy.SupportsRelay(Architecture.Arm64, true)
    && !ControllerDriverPolicy.SupportsRelay(Architecture.X86, true),
    "relay availability is independent of upstream driver architecture support");
Check(!ControllerDriverPolicy.IsRequired([]) && !ControllerDriverPolicy.IsRequired(["false"])
    && !ControllerDriverPolicy.IsRequired(["true", "true"]) && ControllerDriverPolicy.IsRequired(["true"]),
    "only one explicit build opt-in can request a driver dependency");

foreach (var language in Loc.Languages)
{
    Loc.Lang = language.Code;
    foreach (var status in Enum.GetValues<ControllerDriverInstallStatus>())
    {
        var result = new ControllerDriverInstallResult(status);
        if (result.MessageKey is { } key) Check(!string.IsNullOrWhiteSpace(Loc.T(key)) && Loc.T(key) != key,
            "localized dependency result " + language.Code + " " + status);
    }
    Check(Loc.T("InstallingControllerSupport") != "InstallingControllerSupport", "localized controller progress " + language.Code);
}

string scratch = Path.Combine(Path.GetTempPath(), "Playhub-ControllerDependency-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    Check(ControllerDriverDependency.Ensure(scratch, () => throw new Exception("No dependency may launch"))
        .Status == ControllerDriverInstallStatus.InvalidPackage, "required but missing SDK cannot request elevation");
    string nested = Path.Combine(scratch, "Dependencies", "ViGEmBus");
    Directory.CreateDirectory(nested);
    string file = Path.Combine(nested, "fixture.exe");
    File.WriteAllText(file, "Fixture data only; not executable.");
    Check(ControllerDriverDependency.IsRegularPayloadPath(scratch, file), "owned regular payload is accepted");
    Check(!ControllerDriverDependency.IsRegularPayloadPath(scratch, scratch + "-other\\fixture.exe"), "sibling-prefix path is rejected");
    File.WriteAllText(Path.Combine(scratch, "ViGEmClient.x64.dll"), "Invalid SDK fixture.");
    Check(ControllerDriverDependency.Ensure(scratch, () => throw new Exception("No dependency may launch"))
        .Status == ControllerDriverInstallStatus.InvalidPackage, "invalid SDK cannot request elevation");
}
finally { Directory.Delete(scratch, recursive: true); }

// Explicit, read-only artifact checks; this path never calls Ensure, Process.Start,
// pnputil, msiexec or any executable from the dependency package.
if (args is ["--artifact-proof", var assetDirectory])
{
    string exe = Path.Combine(assetDirectory, ControllerDriverPolicy.InstallerName);
    using var input = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
    Check(Convert.ToHexString(SHA256.HashData(input)) == ControllerDriverPolicy.InstallerSha256, "official release executable digest");
    Check(ControllerDriverPayloadTrust.Verify(exe), "official timestamped Nefarius Authenticode signature");
    Check(!ControllerDriverPayloadTrust.Verify(Path.Combine(Environment.SystemDirectory, "kernel32.dll")), "unrelated signed publisher is rejected");
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(assetDirectory, "LICENSE.txt"))))
        == ControllerDriverPolicy.LicenseSha256, "complete upstream BSD license digest");
    using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetDirectory, "SOURCE.json")));
    Check(source.RootElement.GetProperty("sha256").GetString() == ControllerDriverPolicy.InstallerSha256
        && source.RootElement.GetProperty("x64DriverSha256").GetString() == ControllerDriverPolicy.DriverSha256,
        "provenance is consistent with immutable release pins");
    Console.WriteLine("READ-ONLY installed capability: " + ControllerDriverDependency.InspectInstalled());
}
Console.WriteLine($"{passed} checks passed. No installer or driver action was executed.");
