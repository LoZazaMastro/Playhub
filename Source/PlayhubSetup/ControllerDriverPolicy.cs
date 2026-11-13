using System.Runtime.InteropServices;

namespace PlayhubSetup;

public enum ControllerDriverInstallStatus
{
    NotRequired, Ready, Installed, RestartRequired, PermissionRequired,
    ExistingDriverPreserved, UnsupportedPlatform, InvalidPackage, InstallationFailed
}

public sealed record ControllerDriverInstallResult(ControllerDriverInstallStatus Status, int? ExitCode = null)
{
    public bool IsReady => Status is ControllerDriverInstallStatus.Ready or ControllerDriverInstallStatus.Installed;
    public string? MessageKey => Status switch
    {
        ControllerDriverInstallStatus.RestartRequired => "ControllerRestartRequired",
        ControllerDriverInstallStatus.PermissionRequired => "ControllerPermissionRequired",
        ControllerDriverInstallStatus.ExistingDriverPreserved => "ControllerExistingPreserved",
        ControllerDriverInstallStatus.UnsupportedPlatform => "ControllerPlatformUnavailable",
        ControllerDriverInstallStatus.InvalidPackage or ControllerDriverInstallStatus.InstallationFailed => "ControllerSetupIncomplete",
        _ => null
    };
}

internal enum ControllerDriverPresence { Absent, Ready, ExistingUnavailable, Unknown }

internal static class ControllerDriverPolicy
{
    internal static bool IsRequired(IEnumerable<string?> metadataValues) => metadataValues.ToArray() is ["true"];
    internal const string InstallerName = "ViGEmBus_1.22.0_x64_x86_arm64.exe";
    internal const string InstallerSha256 = "89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A";
    internal const string DriverSha256 = "B6D6FA5CA8334368FC366A3E78552EFB74EEF657061371B2DE407AA158B0A11C";
    internal const string ClientSha256 = "00303F99E1968D523FBD08A7C691D60D145B7335059EC593947841D124C10875";
    internal const string LicenseSha256 = "3959F6CA50466675190590DFD024585744B6456AE7D39A3222E6550D62882586";
    internal const string ProductCode = "{9C581C76-2D68-40F8-AA6F-94D3C5215C05}";
    // The upstream all-in-one also contains ARM64/x86 drivers. This relay is x64.
    internal static bool SupportsRelay(Architecture architecture, bool windows10OrLater) =>
        architecture == Architecture.X64 && windows10OrLater;

    internal static ControllerDriverInstallResult? BeforeInstall(ControllerDriverPresence presence) => presence switch
    {
        ControllerDriverPresence.Absent => null,
        ControllerDriverPresence.Ready => new(ControllerDriverInstallStatus.Ready),
        _ => new(ControllerDriverInstallStatus.ExistingDriverPreserved)
    };

    internal static ControllerDriverInstallResult AfterInstall(int exitCode, ControllerDriverPresence presence) =>
        exitCode switch
        {
            3010 or 1641 => new(ControllerDriverInstallStatus.RestartRequired, exitCode),
            1223 or 1602 => new(ControllerDriverInstallStatus.PermissionRequired, exitCode),
            0 when presence == ControllerDriverPresence.Ready => new(ControllerDriverInstallStatus.Installed, exitCode),
            _ => new(ControllerDriverInstallStatus.InstallationFailed, exitCode)
        };
}
