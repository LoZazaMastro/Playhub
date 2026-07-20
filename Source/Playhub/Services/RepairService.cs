using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed record RepairReport(int IssuesFixed, int IssuesFound, IReadOnlyList<string> Notes);

/// <summary>
/// "Risoluzione problemi": controlla i componenti installati da Playhub
/// (Gaming Mode, plugin Decky companion, agente, UWPHook, configurazione,
/// coerenza della shell di avvio) e ripara automaticamente ciò che non va.
/// </summary>
public sealed class RepairService
{
    private readonly GamingModeService _gamingMode;

    public RepairService(GamingModeService gamingMode)
    {
        _gamingMode = gamingMode;
    }

    public async Task<RepairReport> RunAsync(string deckyPluginsPath, IProgress<(double Percent, string Status)> progress)
    {
        var notes = new List<string>();
        var found = 0;
        var fixedCount = 0;

        // ---------- 1) File del pacchetto Playhub ----------
        progress.Report((0.05, "Controllo i file di Playhub…"));
        await Task.Delay(150);
        if (!Directory.Exists(AppPaths.GamingModePackage) ||
            !File.Exists(Path.Combine(AppPaths.GamingModePackage, "GamingMode.exe")))
        {
            found++;
            notes.Add("Il pacchetto Gaming Mode incluso in Playhub è incompleto: reinstalla Playhub per ripristinarlo.");
        }

        // ---------- 2) Integrità di Gaming Mode installato ----------
        progress.Report((0.15, "Controllo i componenti di Gaming Mode…"));
        var gamingModeBroken = await Task.Run(IsInstalledGamingModeBroken);
        if (gamingModeBroken)
        {
            found++;
            progress.Report((0.25, "Sistemo Gaming Mode…"));
            if (await Task.Run(ReinstallGamingMode))
            {
                fixedCount++;
                notes.Add("Gaming Mode è stato reinstallato/aggiornato.");
            }
            else
            {
                notes.Add("Gaming Mode non è stato riparato del tutto: riprova o reinstalla Playhub.");
            }
        }

        // ---------- 3) Configurazione di Gaming Mode ----------
        progress.Report((0.40, "Verifico la configurazione di Gaming Mode…"));
        try
        {
            // LoadConfigAsync ripara da solo config corrotte/troncate (backup o
            // default) e normalizza i watcher: basta caricare e risalvare.
            var config = await _gamingMode.LoadConfigAsync();
            await _gamingMode.SaveConfigAsync(config);
        }
        catch
        {
            found++;
            notes.Add("La configurazione di Gaming Mode non è leggibile né riparabile.");
        }

        // ---------- 4) Plugin Gaming Mode per DeckyLoader ----------
        progress.Report((0.55, "Controllo il plugin Gaming Mode per Decky…"));
        var pluginBroken = await Task.Run(() => IsDeckyCompanionBroken(deckyPluginsPath));
        if (pluginBroken)
        {
            found++;
            progress.Report((0.62, "Sistemo il plugin Gaming Mode per Decky…"));
            await _gamingMode.InstallDeckyPluginAsync(deckyPluginsPath);
            if (!await Task.Run(() => IsDeckyCompanionBroken(deckyPluginsPath)))
            {
                fixedCount++;
                notes.Add("Il plugin Gaming Mode per Decky è stato ripristinato.");
            }
            else
            {
                notes.Add("Il plugin Gaming Mode per Decky non è stato ripristinato.");
            }
        }

        // ---------- 5) Agente Gaming Mode ----------
        progress.Report((0.72, "Verifico l'agente Gaming Mode…"));
        if (_gamingMode.IsInstalled && !await _gamingMode.IsAgentHealthyAsync())
        {
            found++;
            progress.Report((0.78, "Riavvio l'agente Gaming Mode…"));
            _gamingMode.StartAgent();
            var healthy = false;
            for (var i = 0; i < 20 && !healthy; i++)
            {
                await Task.Delay(250);
                healthy = await _gamingMode.IsAgentHealthyAsync();
            }
            if (healthy)
            {
                fixedCount++;
                notes.Add("L'agente Gaming Mode è stato riavviato.");
            }
            else
            {
                notes.Add("L'agente Gaming Mode non risponde: prova a riavviare il PC.");
            }
        }

        // ---------- 6) Coerenza della shell di avvio ----------
        progress.Report((0.86, "Verifico la modalità di avvio…"));
        var shellNote = await Task.Run(RepairStartupShell);
        if (shellNote is not null)
        {
            found++;
            fixedCount++;
            notes.Add(shellNote);
        }

        // ---------- 7) UWPHook ----------
        progress.Report((0.92, "Controllo UWPHook…"));
        var uwpBroken = await Task.Run(IsUwpHookMissing);
        if (uwpBroken)
        {
            found++;
            progress.Report((0.95, "Installo UWPHook…"));
            if (await Task.Run(InstallUwpHookSilently))
            {
                fixedCount++;
                notes.Add("UWPHook è stato reinstallato.");
            }
            else
            {
                notes.Add("UWPHook non è stato reinstallato (pacchetto non disponibile).");
            }
        }

        progress.Report((1.0, "Controllo completato."));
        return new RepairReport(fixedCount, found, notes);
    }

    // Confronta ogni file del pacchetto bundlato con la copia installata in
    // LocalAppData\GamingMode: file mancanti o di dimensione diversa = da riparare.
    private bool IsInstalledGamingModeBroken()
    {
        try
        {
            var package = AppPaths.GamingModePackage;
            if (!Directory.Exists(package) || !File.Exists(Path.Combine(package, "GamingMode.exe")))
            {
                return false; // pacchetto assente: non possiamo confrontare nulla
            }

            var installDir = _gamingMode.InstallDir;
            if (!File.Exists(_gamingMode.InstalledExe))
            {
                return true;
            }

            foreach (var file in Directory.GetFiles(package, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(package, file);
                var installed = Path.Combine(installDir, rel);
                if (!File.Exists(installed) ||
                    new FileInfo(installed).Length != new FileInfo(file).Length)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool ReinstallGamingMode()
    {
        try
        {
            var package = AppPaths.GamingModePackage;
            var script = Path.Combine(package, "install.ps1");
            if (!File.Exists(script))
            {
                return false;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SourceDir \"{package}\"",
                WorkingDirectory = package,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(180000);
            return !IsInstalledGamingModeBroken();
        }
        catch
        {
            return false;
        }
    }

    // Il companion Decky viene controllato solo se l'utente ha la cartella dei
    // plugin Decky: file mancanti o di dimensione diversa = da ripristinare.
    private static bool IsDeckyCompanionBroken(string deckyPluginsPath)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(deckyPluginsPath)
                ? AppPaths.DefaultDeckyPluginsPath
                : deckyPluginsPath;
            if (!Directory.Exists(root))
            {
                return false; // Decky non è in uso: niente da controllare
            }

            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "GamingModeDeckyPlugin", "gaming-mode");
            if (!Directory.Exists(source))
            {
                return false; // pacchetto assente nel bundle
            }

            var dest = Path.Combine(root, "gaming-mode");
            if (!Directory.Exists(dest))
            {
                return true;
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var installed = Path.Combine(dest, rel);
                if (!File.Exists(installed) ||
                    new FileInfo(installed).Length != new FileInfo(file).Length)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // La shell di Windows (HKCU Winlogon\Shell) deve rispecchiare la modalità
    // predefinita: Gaming = agente come shell, Desktop = nessun valore (Explorer).
    private string? RepairStartupShell()
    {
        try
        {
            var configFile = _gamingMode.ConfigFile;
            if (!File.Exists(configFile))
            {
                return null;
            }

            string defaultMode = "";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configFile));
                if (doc.RootElement.TryGetProperty("defaultMode", out var mode))
                {
                    defaultMode = mode.GetString() ?? "";
                }
            }
            catch
            {
                return null;
            }

            const string winlogon = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
            using var key = Registry.CurrentUser.CreateSubKey(winlogon);
            if (key is null)
            {
                return null;
            }

            var shell = key.GetValue("Shell") as string ?? "";
            var isGamingShell = shell.Contains("GamingMode", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(defaultMode, "Desktop", StringComparison.OrdinalIgnoreCase) && isGamingShell)
            {
                key.DeleteValue("Shell", throwOnMissingValue: false);
                return "La shell di avvio puntava a Gaming Mode con predefinita Desktop: ripristinata.";
            }

            if (string.Equals(defaultMode, "Gaming", StringComparison.OrdinalIgnoreCase) &&
                !isGamingShell && File.Exists(_gamingMode.InstalledExe))
            {
                key.SetValue("Shell", $"\"{_gamingMode.InstalledExe}\" shell", RegistryValueKind.String);
                return "La shell di avvio non puntava a Gaming Mode con predefinita Gaming: sistemata.";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUwpHookMissing()
    {
        try
        {
            var installedExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Briano", "UWPHook", "UWPHook.exe");
            if (File.Exists(installedExe))
            {
                return false;
            }
            // Da riparare solo se il setup bundlato esiste davvero.
            return File.Exists(Path.Combine(AppPaths.UwpHookPackage, "UWPHook-Setup.exe"));
        }
        catch
        {
            return false;
        }
    }

    private static bool InstallUwpHookSilently()
    {
        try
        {
            var setup = Path.Combine(AppPaths.UwpHookPackage, "UWPHook-Setup.exe");
            if (!File.Exists(setup))
            {
                return false;
            }

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
            return !IsUwpHookMissing();
        }
        catch
        {
            return false;
        }
    }
}
