using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlayhubSetup;

public enum SetupMode { Install, Uninstall }

public sealed record InstallOptions(
    string InstallDir,
    bool DesktopShortcut,
    bool StartMenuShortcut,
    string Language);

/// <summary>
/// Logica di installazione/disinstallazione di Playhub (per-utente, niente UAC).
/// Registra l'app in "App installate" così è disinstallabile dal menu Start.
/// </summary>
public static class Installer
{
    public const string AppName = "Playhub";
    public const string AppVersion = "1.0.1";
    public const string Publisher = "Andrea Sgarro (ZazaMastro)";
    public const string AppExeName = "Playhub.exe";
    public const string UninstallerName = "unins-playhub.exe";

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);

    private static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    private static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    private static string StartupShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppName + ".lnk");

    // ----------------------------------------------------------------- INSTALL
    public static async Task InstallAsync(InstallOptions options, IProgress<(double Percent, string Status)> progress)
    {
        await Task.Run(() =>
        {
            progress.Report((0.02, Loc.T("Preparing")));
            Directory.CreateDirectory(options.InstallDir);

            ExtractPayload(options.InstallDir, progress);

            var exePath = Path.Combine(options.InstallDir, AppExeName);
            var iconPath = exePath;

            progress.Report((0.90, Loc.T("CreatingShortcuts")));
            if (options.StartMenuShortcut)
                Shortcuts.Create(StartMenuShortcut, exePath, options.InstallDir, iconPath);
            if (options.DesktopShortcut)
                Shortcuts.Create(DesktopShortcut, exePath, options.InstallDir, iconPath);

            progress.Report((0.95, Loc.T("Registering")));
            CopySelfAsUninstaller(options.InstallDir);
            WriteUninstallRegistry(options.InstallDir, exePath);
            SetAppLanguage(options.Language);

            progress.Report((1.0, Loc.T("DoneTitle")));
        });
    }

    // Marcatore in coda all'exe self-extracting: [payload][Int64 lunghezza]["PLHB"].
    private static readonly byte[] PayloadMagic = { (byte)'P', (byte)'L', (byte)'H', (byte)'B' };
    private const int FooterSize = 12; // 8 (lunghezza) + 4 (magic)

    private static void ExtractPayload(string installDir, IProgress<(double, string)> progress)
    {
        // 1) Payload appeso in coda al nostro stesso eseguibile (setup single-file).
        var self = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(self) && File.Exists(self) &&
            TryExtractAppendedPayload(self, installDir, progress))
        {
            return;
        }

        // 2) payload.zip accanto all'eseguibile.
        var baseDir = AppContext.BaseDirectory;
        var sideZip = Path.Combine(baseDir, "payload.zip");
        if (File.Exists(sideZip))
        {
            using var fs = File.OpenRead(sideZip);
            ExtractZip(fs, installDir, progress);
            return;
        }

        // 3) Modalità sviluppo: copia la cartella dist_publish accanto al setup.
        var devFolder = Path.Combine(baseDir, "dist_publish");
        if (Directory.Exists(devFolder))
        {
            CopyDirectory(devFolder, installDir, progress);
            return;
        }

        throw new FileNotFoundException(
            "Nessun payload trovato. Esegui build-installer.bat per creare payload.zip.");
    }

    private static bool TryExtractAppendedPayload(string exePath, string installDir,
        IProgress<(double, string)> progress)
    {
        try
        {
            using var fs = File.Open(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= FooterSize) return false;

            fs.Seek(-FooterSize, SeekOrigin.End);
            var footer = new byte[FooterSize];
            fs.ReadExactly(footer);

            if (footer[8] != PayloadMagic[0] || footer[9] != PayloadMagic[1] ||
                footer[10] != PayloadMagic[2] || footer[11] != PayloadMagic[3])
            {
                return false;
            }

            long length = BitConverter.ToInt64(footer, 0);
            if (length <= 0 || length > fs.Length - FooterSize) return false;

            fs.Seek(-(FooterSize + length), SeekOrigin.End);
            var buffer = new byte[length];
            fs.ReadExactly(buffer);

            using var ms = new MemoryStream(buffer, writable: false);
            ExtractZip(ms, installDir, progress);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractZip(Stream zipStream, string destDir, IProgress<(double, string)> progress)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries;
        var total = entries.Count == 0 ? 1 : entries.Count;
        var done = 0;

        foreach (var entry in entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));

            // Protezione "zip slip".
            if (!targetPath.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }

            done++;
            // 0.05 → 0.88 durante l'estrazione.
            progress.Report((0.05 + 0.83 * done / total, Loc.T("CopyingFiles") + " " + done + "/" + total));
        }
    }

    private static void CopyDirectory(string source, string dest, IProgress<(double, string)> progress)
    {
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        var total = files.Length == 0 ? 1 : files.Length;
        var done = 0;
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            done++;
            progress.Report((0.05 + 0.83 * done / total, Loc.T("CopyingFiles") + " " + done + "/" + total));
        }
    }

    private static void SetAppLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playhub");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "settings.json");

            JsonObject obj = File.Exists(file)
                ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            obj["Language"] = languageCode;
            File.WriteAllText(file, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // L'installazione non deve fallire se non si riesce a scrivere la lingua.
        }
    }

    private static void CopySelfAsUninstaller(string installDir)
    {
        try
        {
            var self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(self) || !File.Exists(self)) return;
            var target = Path.Combine(installDir, UninstallerName);
            File.Copy(self, target, overwrite: true);
        }
        catch
        {
            // Non bloccare l'installazione se la copia dell'uninstaller fallisce.
        }
    }

    private static void WriteUninstallRegistry(string installDir, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        if (key is null) return;

        var uninstaller = Path.Combine(installDir, UninstallerName);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --uninstall --silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", DirectorySizeKb(installDir), RegistryValueKind.DWord);
    }

    private static int DirectorySizeKb(string dir)
    {
        try
        {
            long bytes = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            return (int)Math.Min(int.MaxValue, bytes / 1024);
        }
        catch
        {
            return 0;
        }
    }

    public static void LaunchApp(string installDir)
    {
        var exe = Path.Combine(installDir, AppExeName);
        if (File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = installDir });
        }
    }

    // --------------------------------------------------------------- UNINSTALL
    public static string ReadInstallDir() =>
        Registry.CurrentUser.OpenSubKey(UninstallKey)?.GetValue("InstallLocation") as string
            ?? DefaultInstallDir;

    public static async Task UninstallAsync(IProgress<(double Percent, string Status)> progress, bool removeData = false)
    {
        await Task.Run(() =>
        {
            var installDir = ReadInstallDir();

            progress.Report((0.15, Loc.T("RemovingShortcuts")));
            SafeDelete(StartMenuShortcut);
            SafeDelete(DesktopShortcut);
            SafeDelete(StartupShortcut);

            progress.Report((0.35, Loc.T("RemovingRegistration")));
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

            // Gaming Mode è integrato in Playhub: va SEMPRE rimosso (agente,
            // avvio automatico, scorciatoie), come faceva il suo uninstaller.
            RemoveGamingMode();

            if (removeData)
            {
                progress.Report((0.45, Loc.T("RemovingData")));
                RemoveUserData();
            }

            progress.Report((0.55, Loc.T("RemovingFiles")));
            DeleteInstallFilesExceptSelf(installDir);

            progress.Report((0.95, Loc.T("Cleanup")));
            ScheduleFolderRemoval(installDir);

            progress.Report((1.0, Loc.T("UninstallDone")));
        });
    }

    private static void DeleteInstallFilesExceptSelf(string installDir)
    {
        if (!Directory.Exists(installDir)) return;
        var self = Path.GetFullPath(Environment.ProcessPath ?? "");

        foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase))
                continue; // l'uninstaller in esecuzione non può cancellare se stesso
            SafeDelete(file);
        }
    }

    private static void ScheduleFolderRemoval(string installDir)
    {
        // Un processo cmd staccato attende l'uscita dell'uninstaller e poi
        // rimuove l'intera cartella (compreso l'uninstaller stesso).
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{installDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch
        {
        }
    }

    // Rimozione SEMPRE eseguita del companion Gaming Mode (è parte di Playhub),
    // equivalente al suo uninstaller originale.
    private static void RemoveGamingMode()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("GamingMode"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        DeleteDir(Path.Combine(localAppData, "GamingMode"));                 // agente installato
        SafeDelete(Path.Combine(startup, "Gaming Mode Agent.lnk"));          // avvio automatico
        SafeDelete(Path.Combine(desktop, "Gaming Mode.lnk"));               // scorciatoia desktop
        DeleteDir(Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs", "Gaming Mode")); // menu Start
    }

    // Rimozione OPZIONALE dei dati/impostazioni (casella "Rimuovi anche i dati").
    private static void RemoveUserData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Dati di Playhub (settings.json, backup, downloads/cache).
        DeleteDir(Path.Combine(appData, "Playhub"));
        DeleteDir(Path.Combine(localAppData, "Playhub"));

        // Impostazioni del Gaming Mode (l'agente è già rimosso a parte).
        DeleteDir(Path.Combine(appData, "GamingMode"));
    }

    private static void DeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
