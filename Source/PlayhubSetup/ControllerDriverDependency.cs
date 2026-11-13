using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace PlayhubSetup;

internal static class ControllerDriverDependency
{
    internal static ControllerDriverInstallResult Ensure(string installDirectory, Action installing)
    {
        bool required = ControllerDriverPolicy.IsRequired(typeof(ControllerDriverDependency).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(item => item.Key == "PlayhubControllerDriverRequired").Select(item => item.Value));
        if (!required) return new(ControllerDriverInstallStatus.NotRequired);
        string client = Path.Combine(installDirectory, "ViGEmClient.x64.dll");
        if (!File.Exists(client)) return new(ControllerDriverInstallStatus.InvalidPackage);
        if (!ControllerDriverPolicy.SupportsRelay(RuntimeInformation.OSArchitecture,
            OperatingSystem.IsWindowsVersionAtLeast(10))) return new(ControllerDriverInstallStatus.UnsupportedPlatform);

        try
        {
            if (!MatchesHash(client, ControllerDriverPolicy.ClientSha256)) return new(ControllerDriverInstallStatus.InvalidPackage);
            string dependencyDirectory = Path.Combine(installDirectory, "Dependencies", "ViGEmBus");
            string installer = Path.Combine(dependencyDirectory, ControllerDriverPolicy.InstallerName);
            if (!IsRegularPayloadPath(installDirectory, installer)
                || !MatchesHash(Path.Combine(dependencyDirectory, "LICENSE.txt"), ControllerDriverPolicy.LicenseSha256))
                return new(ControllerDriverInstallStatus.InvalidPackage);

            // A pinned, read-only handle survives validation and the elevated
            // child. A concurrent updater cannot replace the executed image.
            using var image = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Hash(image).Equals(ControllerDriverPolicy.InstallerSha256, StringComparison.OrdinalIgnoreCase)
                || !ControllerDriverPayloadTrust.Verify(installer)) return new(ControllerDriverInstallStatus.InvalidPackage);

            if (ControllerDriverPolicy.BeforeInstall(InspectInstalled()) is { } existing) return existing;
            using var transaction = DriverInstallLease.TryAcquire();
            if (transaction is null) return new(ControllerDriverInstallStatus.InstallationFailed, 1618);
            installing();
            // Preserve a driver/product installed while setup was validating.
            if (ControllerDriverPolicy.BeforeInstall(InspectInstalled()) is { } changed) return changed;

            using var process = global::System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/exenoui /qn /norestart REBOOT=ReallySuppress",
                WorkingDirectory = dependencyDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return new(ControllerDriverInstallStatus.InstallationFailed);
            // Let Windows Installer finish its own transaction. Killing or
            // relaunching it on a timeout can damage a shared controller bus.
            process.WaitForExit();
            return ControllerDriverPolicy.AfterInstall(process.ExitCode, ProbeAfterInstall(process.ExitCode));
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        { return new(ControllerDriverInstallStatus.PermissionRequired, 1223); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception
            or System.Security.Cryptography.CryptographicException or System.Security.SecurityException)
        { return new(ControllerDriverInstallStatus.InstallationFailed); }
    }

    internal static ControllerDriverPresence InspectInstalled()
    {
        try
        {
            string driver = Path.Combine(global::System.Environment.SystemDirectory, "drivers", "ViGEmBus.sys");
            using var service = global::Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
            string[]? devices = PresentBuses();
            bool product = ProductRegistered();
            if (service is null)
                return devices is { Length: 0 } && !product && !File.Exists(driver)
                    ? ControllerDriverPresence.Absent : ControllerDriverPresence.ExistingUnavailable;
            if (service.GetValue("ImagePath") is not string image
                || !image.Equals(@"\SystemRoot\System32\drivers\ViGEmBus.sys", StringComparison.OrdinalIgnoreCase)
                || service.GetValue("Type") is not int type || type != 1
                || !MatchesHash(driver, ControllerDriverPolicy.DriverSha256)
                || devices is not [var bus]
                || CM_Locate_DevNodeW(out uint node, bus, 0) != 0
                || CM_Get_DevNode_Status(out uint status, out uint problem, node, 0) != 0
                || (status & 8) == 0 || problem != 0)
                return ControllerDriverPresence.ExistingUnavailable;
            return ControllerDriverPresence.Ready;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or System.Security.Cryptography.CryptographicException)
        { return ControllerDriverPresence.Unknown; }
    }

    private static ControllerDriverPresence ProbeAfterInstall(int exitCode)
    {
        var presence = InspectInstalled();
        // Device startup can finish just after the bootstrapper returns. This
        // is a bounded capability check, not an installation retry or idle loop.
        for (int attempt = 0; exitCode == 0 && presence != ControllerDriverPresence.Ready && attempt < 24; attempt++)
        {
            Thread.Sleep(125);
            presence = InspectInstalled();
        }
        return presence;
    }

    private static bool ProductRegistered()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var product = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ControllerDriverPolicy.ProductCode);
            if (product is not null) return true;
        }
        return false;
    }

    private static string[]? PresentBuses()
    {
        const uint flags = 0x10000142;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out uint length, "ViGEmBus", flags) != 0 || length is 0 or > 8192) return null;
            char[] buffer = new char[length];
            uint result = CM_Get_Device_ID_ListW("ViGEmBus", buffer, length, flags);
            if (result == 0x1a) continue;
            if (result != 0 || buffer[^1] != '\0') return null;
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return null;
    }

    internal static bool IsRegularPayloadPath(string root, string path)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        path = Path.GetFullPath(path);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool MatchesHash(string path, string expected)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Hash(file).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private sealed class DriverInstallLease : IDisposable
    {
        private readonly Mutex _mutex;
        private DriverInstallLease(Mutex mutex) => _mutex = mutex;
        internal static DriverInstallLease? TryAcquire()
        {
            // The dependency is machine-wide. Two Playhub setups must not both
            // observe an absent bus and start the upstream install concurrently.
            var mutex = new Mutex(false, @"Global\Playhub.ControllerDriverDependency.1");
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            catch { mutex.Dispose(); throw; }
            if (acquired) return new DriverInstallLease(mutex);
            mutex.Dispose();
            return null;
        }
        public void Dispose()
        {
            try { _mutex.ReleaseMutex(); }
            finally { _mutex.Dispose(); }
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
}
