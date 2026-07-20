using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Playhub.Services;

/// <summary>
/// Genera il report diagnostico ("Serve aiuto?") salvato sul desktop.
/// Raccoglie tutto ciò che serve per capire errori, conflitti con altri
/// processi, cosa è stato avviato e come: sistema, Playhub, Gaming Mode
/// (config, stato, log, shell), Steam, Decky, Sunshine/Apollo, processi
/// attivi e software notoriamente conflittuale.
/// Il contenuto del report è volutamente in inglese (è materiale tecnico
/// destinato al supporto); solo la UI è localizzata.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly GamingModeService _gamingMode;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    // Software che notoriamente interferisce con overlay/fullscreen/controller.
    private static readonly string[] KnownConflictProcesses =
    {
        "RTSS", "MSIAfterburner", "EVGAPrecisionX", "SpecialK32", "SpecialK64",
        "obs64", "obs32", "Overwolf", "wallpaper32", "wallpaper64",
        "Playnite.FullscreenApp", "Playnite.DesktopApp", "BigPictureTV",
        "GameBar", "XboxGameBarWidgets", "Nahimic3", "NahimicSvc64", "flux",
        "DS4Windows", "HidHide", "reWASD", "JoyXoff", "AntiMicroX",
        "LosslessScaling", "CRU", "OneDrive"
    };

    public DiagnosticsService(GamingModeService gamingMode)
    {
        _gamingMode = gamingMode;
    }

    /// <summary>Crea il report sul desktop e restituisce il percorso completo.</summary>
    public async Task<string> CreateReportAsync(string settingsJsonPath)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("==================================================");
        sb.AppendLine(" PLAYHUB DIAGNOSTIC REPORT");
        sb.AppendLine("==================================================");
        sb.AppendLine("Generated : " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));

        AppendSection(sb, "SYSTEM", AppendSystemInfo);
        AppendSection(sb, "PLAYHUB", builder => AppendPlayhubInfo(builder, settingsJsonPath));
        AppendSection(sb, "GAMING MODE", AppendGamingModeInfo);
        await AppendSectionAsync(sb, "GAMING MODE AGENT (live)", AppendAgentStatusAsync);
        AppendSection(sb, "STARTUP & SHELL", AppendStartupAndShellInfo);
        AppendSection(sb, "STEAM", AppendSteamInfo);
        AppendSection(sb, "DECKY LOADER", AppendDeckyInfo);
        AppendSection(sb, "STREAMING (SUNSHINE / APOLLO / VIBEPOLLO)", AppendStreamingInfo);
        AppendSection(sb, "POTENTIAL CONFLICTS", AppendConflictInfo);
        AppendSection(sb, "RUNNING PROCESSES", AppendProcessList);
        AppendSection(sb, "LOG TAILS", AppendLogTails);

        sb.AppendLine();
        sb.AppendLine("=== END OF REPORT ===");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var fileName = "Playhub-Report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
        var fullPath = Path.Combine(desktop, fileName);
        await Task.Run(() => File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false)));
        return fullPath;
    }

    // ------------------------------------------------------------------ sezioni

    private static void AppendSection(StringBuilder sb, string title, Action<StringBuilder> writer)
    {
        sb.AppendLine();
        sb.AppendLine("---------- " + title + " ----------");
        try
        {
            writer(sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine("SECTION ERROR: " + ex.Message);
        }
    }

    private static async Task AppendSectionAsync(StringBuilder sb, string title, Func<StringBuilder, Task> writer)
    {
        sb.AppendLine();
        sb.AppendLine("---------- " + title + " ----------");
        try
        {
            await writer(sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine("SECTION ERROR: " + ex.Message);
        }
    }

    private static void AppendSystemInfo(StringBuilder sb)
    {
        sb.AppendLine("OS            : " + Environment.OSVersion.VersionString);
        sb.AppendLine("64-bit OS     : " + Environment.Is64BitOperatingSystem + " (process 64-bit: " + Environment.Is64BitProcess + ")");
        sb.AppendLine(".NET runtime  : " + RuntimeInformation.FrameworkDescription);
        sb.AppendLine("Architecture  : " + RuntimeInformation.OSArchitecture);
        sb.AppendLine("CPU           : " + (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "?") + " (" + Environment.ProcessorCount + " logical cores)");
        var memory = MemoryStatus.Query();
        if (memory is not null)
        {
            sb.AppendLine("RAM           : " + memory.Value.TotalMb + " MB total, " + memory.Value.AvailableMb + " MB free (" + memory.Value.LoadPercent + "% in use)");
        }
        sb.AppendLine("Uptime        : " + TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("Culture       : " + CultureInfo.CurrentCulture.Name + " (UI: " + CultureInfo.CurrentUICulture.Name + ")");
        sb.AppendLine("Primary screen: " + GetSystemMetrics(0) + "x" + GetSystemMetrics(1));
        sb.AppendLine("Virtual screen: " + GetSystemMetrics(78) + "x" + GetSystemMetrics(79) + " (" + GetSystemMetrics(80) + " monitor(s))");
        sb.AppendLine("Session state : shutting down = " + (GetSystemMetrics(0x2000) != 0));
    }

    private void AppendPlayhubInfo(StringBuilder sb, string settingsJsonPath)
    {
        sb.AppendLine("Version       : " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?"));
        sb.AppendLine("Base directory: " + AppContext.BaseDirectory);
        sb.AppendLine("Process ID    : " + Environment.ProcessId);
        sb.AppendLine("GamingMode pkg: " + Describe(AppPaths.GamingModePackage));
        sb.AppendLine("UWPHook pkg   : " + Describe(AppPaths.UwpHookPackage));
        sb.AppendLine("Decky inst pkg: " + Describe(AppPaths.DeckyInstallerPackage));
        AppendFileContent(sb, "settings.json", settingsJsonPath, maxChars: 8000);
    }

    private void AppendGamingModeInfo(StringBuilder sb)
    {
        sb.AppendLine("Installed     : " + _gamingMode.IsInstalled);
        if (File.Exists(_gamingMode.InstalledExe))
        {
            var info = FileVersionInfo.GetVersionInfo(_gamingMode.InstalledExe);
            var file = new FileInfo(_gamingMode.InstalledExe);
            sb.AppendLine("Agent exe     : " + _gamingMode.InstalledExe);
            sb.AppendLine("Agent version : " + (info.FileVersion ?? "?") + " (" + file.Length + " bytes, modified " + file.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")");
        }
        var packageExe = Path.Combine(AppPaths.GamingModePackage, "GamingMode.exe");
        if (File.Exists(packageExe))
        {
            var info = FileVersionInfo.GetVersionInfo(packageExe);
            var file = new FileInfo(packageExe);
            sb.AppendLine("Bundled exe   : " + (info.FileVersion ?? "?") + " (" + file.Length + " bytes)");
        }
        AppendFileContent(sb, "config.json", _gamingMode.ConfigFile, maxChars: 12000);
        var stateFile = Path.Combine(Path.GetDirectoryName(_gamingMode.ConfigFile) ?? "", "state.json");
        AppendFileContent(sb, "state.json", stateFile, maxChars: 4000);
    }

    private async Task AppendAgentStatusAsync(StringBuilder sb)
    {
        foreach (var endpoint in new[] { "/health", "/status" })
        {
            try
            {
                var response = await _http.GetAsync("http://127.0.0.1:47991" + endpoint);
                var body = await response.Content.ReadAsStringAsync();
                sb.AppendLine(endpoint + " -> HTTP " + (int)response.StatusCode);
                sb.AppendLine(Truncate(body, 8000));
            }
            catch (Exception ex)
            {
                sb.AppendLine(endpoint + " -> UNREACHABLE (" + ex.GetBaseException().Message + ")");
            }
        }
    }

    private void AppendStartupAndShellInfo(StringBuilder sb)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon"))
        {
            sb.AppendLine("Winlogon Shell (HKCU): " + ((key?.GetValue("Shell") as string) ?? "<not set - Explorer default>"));
        }

        var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        sb.AppendLine("Startup folder items :");
        if (Directory.Exists(startupDir))
        {
            foreach (var file in Directory.EnumerateFiles(startupDir))
            {
                sb.AppendLine("  - " + Path.GetFileName(file));
            }
        }

        sb.AppendLine("HKCU Run entries     :");
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            if (run is not null)
            {
                foreach (var name in run.GetValueNames())
                {
                    sb.AppendLine("  - " + name + " = " + run.GetValue(name));
                }
            }
        }
    }

    private static void AppendSteamInfo(StringBuilder sb)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            sb.AppendLine("SteamExe (registry): " + ((key?.GetValue("SteamExe") as string) ?? "<not found>"));
            sb.AppendLine("SteamPath          : " + ((key?.GetValue("SteamPath") as string) ?? "<not found>"));
        }
        foreach (var name in new[] { "steam", "steamwebhelper", "gameoverlayui" })
        {
            var processes = Process.GetProcessesByName(name);
            sb.AppendLine(name + " running    : " + (processes.Length > 0 ? processes.Length + " process(es), PID " + string.Join(", ", processes.Select(p => p.Id)) : "no"));
        }
    }

    private static void AppendDeckyInfo(StringBuilder sb)
    {
        var homebrew = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew");
        var plugins = Path.Combine(homebrew, "plugins");
        sb.AppendLine("homebrew dir  : " + Describe(homebrew));
        sb.AppendLine("Plugins       :");
        if (Directory.Exists(plugins))
        {
            foreach (var dir in Directory.EnumerateDirectories(plugins))
            {
                sb.AppendLine("  - " + Path.GetFileName(dir));
            }
        }
        var services = Path.Combine(homebrew, "services");
        sb.AppendLine("PluginLoader  : " + (File.Exists(Path.Combine(services, "PluginLoader_noconsole.exe")) || File.Exists(Path.Combine(services, "PluginLoader.exe")) ? "installed" : "not found"));
        var running = Process.GetProcessesByName("PluginLoader").Concat(Process.GetProcessesByName("PluginLoader_noconsole")).ToArray();
        sb.AppendLine("Loader running: " + (running.Length > 0 ? "yes, PID " + string.Join(", ", running.Select(p => p.Id)) : "no"));
    }

    private static void AppendStreamingInfo(StringBuilder sb)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(programFiles, "Sunshine", "sunshine.exe"),
            Path.Combine(programFiles, "LizardByte", "Sunshine", "sunshine.exe"),
            Path.Combine(programFiles, "Apollo", "sunshine.exe"),
            Path.Combine(programFiles, "Vibepollo", "sunshine.exe"),
            Path.Combine(localAppData, "Programs", "Apollo", "sunshine.exe"),
            Path.Combine(localAppData, "Programs", "Vibepollo", "sunshine.exe")
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                sb.AppendLine("Found         : " + path + " (v" + (info.FileVersion ?? "?") + ")");
            }
        }
        foreach (var name in new[] { "sunshine", "apollo", "vibepollo" })
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
            {
                sb.AppendLine(name + " running: yes, PID " + string.Join(", ", processes.Select(p => p.Id)));
            }
        }
    }

    private static void AppendConflictInfo(StringBuilder sb)
    {
        var found = false;
        foreach (var name in KnownConflictProcesses)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
            {
                found = true;
                sb.AppendLine("RUNNING: " + name + " (PID " + string.Join(", ", processes.Select(p => p.Id)) + ") - may interfere with overlays/fullscreen/controllers");
            }
        }
        if (!found)
        {
            sb.AppendLine("None of the known conflicting tools are running.");
        }
    }

    private static void AppendProcessList(StringBuilder sb)
    {
        sb.AppendLine("Name                             PID      RAM(MB)  Window title");
        foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var ram = process.WorkingSet64 / (1024 * 1024);
                var title = "";
                try { title = process.MainWindowTitle; } catch { }
                sb.AppendLine(process.ProcessName.PadRight(32).Substring(0, 32) + " " + process.Id.ToString(CultureInfo.InvariantCulture).PadRight(8) + " " + ram.ToString(CultureInfo.InvariantCulture).PadRight(8) + " " + Truncate(title, 60));
            }
            catch
            {
                // processo terminato nel frattempo: ignora
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void AppendLogTails(StringBuilder sb)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AppendFileTail(sb, "agent.log", Path.Combine(appData, "GamingMode", "agent.log"), lines: 120);
        AppendFileTail(sb, "playhub-safety.log", Path.Combine(appData, "GamingMode", "playhub-safety.log"), lines: 60);
        AppendFileTail(sb, "playhub_crash.txt", Path.Combine(AppContext.BaseDirectory, "playhub_crash.txt"), lines: 80);
    }

    // ------------------------------------------------------------------ helper

    private static void AppendFileContent(StringBuilder sb, string label, string path, int maxChars)
    {
        sb.AppendLine("--- " + label + " (" + path + ") ---");
        try
        {
            sb.AppendLine(File.Exists(path) ? Truncate(File.ReadAllText(path), maxChars) : "<file not found>");
        }
        catch (Exception ex)
        {
            sb.AppendLine("<unreadable: " + ex.Message + ">");
        }
    }

    private static void AppendFileTail(StringBuilder sb, string label, string path, int lines)
    {
        sb.AppendLine("--- " + label + " (last " + lines + " lines of " + path + ") ---");
        try
        {
            if (!File.Exists(path))
            {
                sb.AppendLine("<file not found>");
                return;
            }
            foreach (var line in File.ReadLines(path).TakeLast(lines))
            {
                sb.AppendLine(line);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("<unreadable: " + ex.Message + ">");
        }
    }

    private static string Describe(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? dir + " (present)" : dir + " (MISSING)";
        }
        catch
        {
            return dir;
        }
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? "";
        }
        return value.Length <= maxChars ? value : value[..maxChars] + "\n<...truncated...>";
    }

    private readonly record struct MemoryInfo(ulong TotalMb, ulong AvailableMb, uint LoadPercent);

    private static class MemoryStatus
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        public static MemoryInfo? Query()
        {
            try
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (!GlobalMemoryStatusEx(ref status))
                {
                    return null;
                }
                return new MemoryInfo(status.TotalPhys / (1024 * 1024), status.AvailPhys / (1024 * 1024), status.MemoryLoad);
            }
            catch
            {
                return null;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
