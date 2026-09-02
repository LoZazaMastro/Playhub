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
    string Language,
    bool PreserveExistingShortcuts = false);

/// <summary>
/// Logica di installazione/disinstallazione di Playhub (per-utente, niente UAC).
/// Registra l'app in "App installate" così è disinstallabile dal menu Start.
/// </summary>
public static class Installer
{
    public const string AppName = "Playhub";
    public const string AppVersion = "1.3.0";
    public const string Publisher = "Andrea Sgarro (LoZazaMastro)";
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
            StopRunningPlayhub();
            Directory.CreateDirectory(options.InstallDir);

            ExtractPayload(options.InstallDir, progress);

            progress.Report((0.87, Loc.T("InstallingUWPHook")));
            InstallUWPHookSilently(options.InstallDir);

            progress.Report((0.89, Loc.T("InstallingGamingMode")));
            InstallOrUpdateGamingModeSilently(options.InstallDir);

            var exePath = Path.Combine(options.InstallDir, AppExeName);
            var iconPath = exePath;

            progress.Report((0.90, Loc.T("CreatingShortcuts")));
            if (!options.PreserveExistingShortcuts)
            {
                if (options.StartMenuShortcut)
                    Shortcuts.Create(StartMenuShortcut, exePath, options.InstallDir, iconPath);
                if (options.DesktopShortcut)
                    Shortcuts.Create(DesktopShortcut, exePath, options.InstallDir, iconPath);
            }

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

        throw new FileNotFoundException(Loc.T("PackageError"));
    }

    private static bool TryExtractAppendedPayload(string exePath, string installDir,
        IProgress<(double, string)> progress)
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
        if (length <= 0 || length > fs.Length - FooterSize)
            throw new InvalidDataException(Loc.T("PackageError"));

        fs.Seek(-(FooterSize + length), SeekOrigin.End);
        var buffer = new byte[length];
        fs.ReadExactly(buffer);
        using var ms = new MemoryStream(buffer, writable: false);
        ExtractZip(ms, installDir, progress);
        return true;
    }

    private static void ExtractZip(Stream zipStream, string destDir, IProgress<(double, string)> progress)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var destination = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = destination + Path.DirectorySeparatorChar;
        var entries = archive.Entries.Select(entry =>
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update contains a path outside its installation directory.");
            return (Entry: entry, Target: target, Relative: Path.GetRelativePath(destination, target));
        }).ToList();
        var work = Path.Combine(Path.GetDirectoryName(destination)!, ".playhub-update-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(work, "staged");
        var backup = Path.Combine(work, "backup");
        var replaced = new List<(string Target, string? Backup)>();
        var createdDirectories = new List<string>();
        var preserveRecovery = false;

        void EnsureDirectory(string path)
        {
            if (Directory.Exists(path)) return;
            var parent = Path.GetDirectoryName(path);
            if (parent is not null && !Directory.Exists(parent)) EnsureDirectory(parent);
            Directory.CreateDirectory(path);
            createdDirectories.Add(path);
        }

        try
        {
            // Validate and extract the whole package before replacing existing files.
            Directory.CreateDirectory(staged);
            foreach (var item in entries)
            {
                var file = Path.Combine(staged, item.Relative);
                if (string.IsNullOrEmpty(item.Entry.Name)) Directory.CreateDirectory(file);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    item.Entry.ExtractToFile(file, overwrite: false);
                }
            }

            var total = Math.Max(1, entries.Count);
            var done = 0;
            foreach (var item in entries)
            {
                if (string.IsNullOrEmpty(item.Entry.Name)) EnsureDirectory(item.Target);
                else
                {
                    EnsureDirectory(Path.GetDirectoryName(item.Target)!);
                    string? original = null;
                    if (File.Exists(item.Target))
                    {
                        original = Path.Combine(backup, item.Relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                        File.Move(item.Target, original);
                    }
                    replaced.Add((item.Target, original));
                    File.Move(Path.Combine(staged, item.Relative), item.Target);
                }
                done++;
                progress.Report((0.05 + 0.83 * done / total, Loc.T("CopyingFiles") + " " + done + "/" + total));
            }
        }
        catch (Exception installError)
        {
            var errors = new List<Exception>();
            foreach (var item in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(item.Target)) File.Delete(item.Target);
                    if (item.Backup is not null) File.Move(item.Backup, item.Target);
                }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var directory in createdDirectories.AsEnumerable().Reverse())
            {
                try { Directory.Delete(directory, recursive: false); } catch { }
            }
            if (errors.Count > 0)
            {
                preserveRecovery = true;
                throw new AggregateException("Update failed. Recovery files retained at " + backup,
                    new[] { installError }.Concat(errors));
            }
            throw;
        }
        finally
        {
            if (!preserveRecovery && Directory.Exists(work)) Directory.Delete(work, recursive: true);
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

    public static string ReadAppLanguage()
    {
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playhub", "settings.json");
            if (!File.Exists(file)) return "en";

            var obj = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            var language = obj?["Language"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            return Loc.Languages.Any(item =>
                string.Equals(item.Code, language, StringComparison.OrdinalIgnoreCase))
                    ? language!
                    : "en";
        }
        catch
        {
            return "en";
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

    public static async Task UninstallAsync(
        IProgress<(double Percent, string Status)> progress,
        bool removeData = false,
        bool removeUWPHook = false)
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

            if (removeUWPHook)
            {
                progress.Report((0.42, Loc.T("RemovingUWPHook")));
                RemoveUWPHook();
            }

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

    private static void StopRunningPlayhub()
    {
        var currentId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("Playhub"))
        {
            try
            {
                if (process.Id == currentId) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                throw new IOException(Loc.T("FilesInUse"));
            }
        }
    }

    private static void InstallUWPHookSilently(string installDir)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var installedExe = Path.Combine(appData, "Briano", "UWPHook", "UWPHook.exe");
        if (File.Exists(installedExe)) return;

        var setup = Path.Combine(installDir, "UWPHook", "UWPHook-Setup.exe");
        if (!File.Exists(setup)) return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = setup,
                Arguments = "/S",
                WorkingDirectory = Path.GetDirectoryName(setup)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(120000);

            // L'installazione è un componente interno di Playhub: non deve
            // aggiungere un'icona UWPHook separata sul desktop dell'utente.
            SafeDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "UWPHook.lnk"));
        }
        catch
        {
            // Il launcher completo è comunque incluso in Playhub e rimane il fallback.
        }
    }

    // Gaming Mode è parte integrante di Playhub: quando Playhub viene installato
    // o aggiornato, il companion Gaming Mode bundlato viene installato/aggiornato
    // automaticamente (l'install.ps1 del pacchetto gestisce stop agente, copia,
    // avvio automatico e riavvio dell'agente in modo idempotente).
    private static void InstallOrUpdateGamingModeSilently(string installDir)
    {
        try
        {
            var packageDir = Path.Combine(installDir, "Plugins", "Gaming Mode", "gaming-mode-win-x64");
            var packageExe = Path.Combine(packageDir, "GamingMode.exe");
            var script = Path.Combine(packageDir, "install.ps1");
            if (!File.Exists(packageExe) || !File.Exists(script)) return;

            // Se l'agente installato è già identico a quello nel pacchetto, salta.
            var installedExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GamingMode", "GamingMode.exe");
            if (File.Exists(installedExe))
            {
                try
                {
                    var installedInfo = FileVersionInfo.GetVersionInfo(installedExe);
                    var packageInfo = FileVersionInfo.GetVersionInfo(packageExe);
                    if (!string.IsNullOrWhiteSpace(installedInfo.FileVersion) &&
                        string.Equals(installedInfo.FileVersion, packageInfo.FileVersion, StringComparison.OrdinalIgnoreCase) &&
                        new FileInfo(installedExe).Length == new FileInfo(packageExe).Length)
                    {
                        return;
                    }
                }
                catch
                {
                    // In dubbio, reinstalla.
                }
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SourceDir \"{packageDir}\"",
                WorkingDirectory = packageDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(180000);
        }
        catch
        {
            // L'installazione di Playhub non deve fallire per il companion Gaming Mode.
        }
    }

    private static void RemoveUWPHook()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("UWPHook"))
            {
                try { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        var uninstaller = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Briano", "UWPHook", "uninstall.exe");
        if (!File.Exists(uninstaller)) return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uninstaller,
                Arguments = "/S",
                WorkingDirectory = Path.GetDirectoryName(uninstaller)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(120000);
        }
        catch { }
    }

    public static string FriendlyError(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => Loc.T("AccessDenied"),
            FileNotFoundException => Loc.T("PackageError"),
            IOException => Loc.T("FilesInUse"),
            InvalidDataException => Loc.T("PackageError"),
            _ => Loc.T("UnexpectedError")
        };
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
