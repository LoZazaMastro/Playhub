using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Playhub.Services;

/// <summary>
/// Stato del driver PawnIO (https://pawnio.eu, namazso, GPL-2.0-or-later).
/// PawnIO e' il ponte firmato verso la mailbox SMU (AMD) e gli MSR RAPL (Intel):
/// senza di lui il controllo del TDP non esiste su Windows.
///
/// Questo servizio SOLO LEGGE: registro di sistema, file del driver, stato del
/// servizio nello Service Control Manager, firma Authenticode. Non apre mai il
/// device \\.\PawnIO, non avvia e non ferma niente.
/// </summary>
public enum PawnIoState
{
    /// <summary>Nessuna traccia del driver: si puo' proporre l'installazione.</summary>
    NotInstalled,
    /// <summary>Driver registrato, file presente e firmato, servizio in esecuzione.</summary>
    Installed,
    /// <summary>Qualcosa c'e' ma non e' utilizzabile: <see cref="PawnIoStatus.Reason"/> dice cosa.</summary>
    Present,
    /// <summary>Non e' stato possibile stabilirlo (piattaforma o errore di lettura).</summary>
    Unknown
}

/// <param name="State">Esito della diagnosi.</param>
/// <param name="Reason">Codice stabile, non tradotto, sempre valorizzato.</param>
/// <param name="Detail">Testo tecnico dell'errore che ha prodotto Reason (mai inghiottito).</param>
public sealed record PawnIoStatus(
    PawnIoState State,
    string Reason,
    string? Version = null,
    string? DriverPath = null,
    string? ServiceStatus = null,
    string? SignerSubject = null,
    string? Detail = null)
{
    public bool IsUsable => State == PawnIoState.Installed;
}

public static class PawnIoService
{
    private const string ServiceKeyPath = @"SYSTEM\CurrentControlSet\Services\PawnIO";
    private const string ProductKeyPath = @"SOFTWARE\PawnIO";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string ServiceName = "PawnIO";

    // --- Service Control Manager, sola lettura ------------------------------
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    private const int ERROR_ACCESS_DENIED = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    /// <summary>
    /// Diagnosi completa. Non solleva mai: ogni fallimento diventa uno stato con
    /// Reason leggibile e Detail con il messaggio originale.
    /// </summary>
    public static PawnIoStatus Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PawnIoStatus(PawnIoState.Unknown, "platform_unsupported");
        }

        try
        {
            return DetectWindows();
        }
        catch (Exception ex)
        {
            Diag.Crash("PawnIoService.Detect", ex);
            return new PawnIoStatus(PawnIoState.Unknown, "detection_failed", Detail: Describe(ex));
        }
    }

    [SupportedOSPlatform("windows")]
    private static PawnIoStatus DetectWindows()
    {
        var version = ReadProductVersion();

        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var service = machine.OpenSubKey(ServiceKeyPath);
        if (service is null)
        {
            return new PawnIoStatus(PawnIoState.NotInstalled, "service_not_registered", version);
        }

        // Type 1 = SERVICE_KERNEL_DRIVER. Qualunque altro valore non e' PawnIO.
        var type = service.GetValue("Type") as int?;
        if (type != 1)
        {
            return new PawnIoStatus(PawnIoState.Present, "service_type_invalid", version,
                Detail: "Type=" + (type?.ToString() ?? "assente"));
        }

        var start = service.GetValue("Start") as int?;
        var driverPath = ResolveImagePath(service.GetValue("ImagePath") as string);
        if (string.IsNullOrEmpty(driverPath))
        {
            return new PawnIoStatus(PawnIoState.Present, "driver_path_unreadable", version);
        }
        if (!File.Exists(driverPath))
        {
            return new PawnIoStatus(PawnIoState.Present, "driver_file_missing", version, driverPath);
        }

        version ??= ReadFileVersion(driverPath);

        // Start 4 = SERVICE_DISABLED: il servizio non partira' mai, e' una causa
        // definita e va detta, non nascosta dietro un generico "non funziona".
        if (start == 4)
        {
            return new PawnIoStatus(PawnIoState.Present, "service_disabled", version, driverPath, "disabled");
        }

        var (serviceState, serviceReason, serviceDetail) = QueryServiceState();
        if (serviceReason is not null)
        {
            return new PawnIoStatus(PawnIoState.Present, serviceReason, version, driverPath, serviceState, Detail: serviceDetail);
        }
        if (serviceState != "running")
        {
            return new PawnIoStatus(PawnIoState.Present, "service_not_running", version, driverPath, serviceState);
        }

        // Un driver kernel in esecuzione e' per forza passato dal controllo firma
        // di Windows, ma un file sostituito a caldo o una catena revocata devono
        // comunque risultare qui, non a valle nel plugin.
        var signature = AuthenticodeVerifier.Verify(driverPath);
        if (!signature.IsTrusted)
        {
            return new PawnIoStatus(PawnIoState.Present, "driver_signature_invalid", version, driverPath,
                serviceState, signature.SignerSubject, signature.Detail);
        }

        return new PawnIoStatus(PawnIoState.Installed, "ready", version, driverPath, serviceState,
            signature.SignerSubject, signature.Detail);
    }

    /// <summary>Stato del servizio nello SCM. Nessuna eccezione: (stato, reason, dettaglio).</summary>
    private static (string? State, string? Reason, string? Detail) QueryServiceState()
    {
        var manager = IntPtr.Zero;
        var handle = IntPtr.Zero;
        try
        {
            manager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (manager == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                return (null, "service_manager_unavailable", "OpenSCManager error " + error);
            }

            handle = OpenServiceW(manager, ServiceName, SERVICE_QUERY_STATUS);
            if (handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                return error switch
                {
                    ERROR_SERVICE_DOES_NOT_EXIST => (null, "service_not_registered", null),
                    ERROR_ACCESS_DENIED => (null, "service_query_denied", "OpenService error 5"),
                    _ => (null, "service_query_failed", "OpenService error " + error)
                };
            }

            if (!QueryServiceStatus(handle, out var status))
            {
                var error = Marshal.GetLastWin32Error();
                return (null, "service_query_failed", "QueryServiceStatus error " + error);
            }

            var state = status.dwCurrentState switch
            {
                1 => "stopped",
                2 => "start_pending",
                3 => "stop_pending",
                4 => "running",
                5 => "continue_pending",
                6 => "pause_pending",
                7 => "paused",
                _ => "unknown"
            };
            return (state, null, null);
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseServiceHandle(handle);
            if (manager != IntPtr.Zero) CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// ImagePath di un driver kernel e' un percorso NT: "\??\C:\...",
    /// "\SystemRoot\System32\drivers\x.sys" o relativo alla cartella di Windows.
    /// </summary>
    internal static string? ResolveImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var path = imagePath.Trim().Trim('"');
        if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path[4..];
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(windows, path[12..]);
        else if (path.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(windows, path);
        else if (path.StartsWith(@"\", StringComparison.Ordinal))
            path = Path.Combine(windows, path.TrimStart('\\'));

        try { return Path.GetFullPath(path); }
        catch (Exception ex) { Diag.Crash("PawnIoService.ResolveImagePath", ex); return null; }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadProductVersion()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var product = machine.OpenSubKey(ProductKeyPath);
            var declared = product?.GetValue("Version") as string;
            if (!string.IsNullOrWhiteSpace(declared)) return declared.Trim();

            using var uninstall = machine.OpenSubKey(UninstallKeyPath);
            if (uninstall is null) return null;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                var display = entry?.GetValue("DisplayName") as string;
                if (display is null || !display.Contains("PawnIO", StringComparison.OrdinalIgnoreCase)) continue;
                var value = entry!.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }
        catch (Exception ex)
        {
            // Una versione illeggibile non e' un fallimento della diagnosi.
            Diag.Crash("PawnIoService.ReadProductVersion", ex);
            return null;
        }
    }

    private static string? ReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = info.ProductVersion ?? info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex)
        {
            Diag.Crash("PawnIoService.ReadFileVersion", ex);
            return null;
        }
    }

    private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;
}
