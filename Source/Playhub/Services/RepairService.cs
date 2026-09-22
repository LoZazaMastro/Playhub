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
/// collegamento di avvio) solo su richiesta esplicita, preservando le preferenze.
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
        var payloadReady = false;
        try
        {
            payloadReady = await Task.Run(() => GamingModeRepairPayload.IsCurrent(
                AppPaths.GamingModePackage, _gamingMode.InstallDir));
        }
        catch
        {
            found++;
            notes.Add("Il pacchetto Gaming Mode incluso in Playhub è incompleto: reinstalla Playhub per ripristinarlo.");
        }
        if (!payloadReady && found == 0)
        {
            found++;
            progress.Report((0.25, "Sistemo Gaming Mode…"));
            try
            {
                // Do not stop an active gaming session or run the standalone installer.
                await Task.Run(() => GamingModeRepairPayload.Restore(
                    AppPaths.GamingModePackage, _gamingMode.InstallDir));
                payloadReady = await Task.Run(() => GamingModeRepairPayload.IsCurrent(
                    AppPaths.GamingModePackage, _gamingMode.InstallDir));
                if (!payloadReady) throw new IOException("Verifica del payload non riuscita.");
                fixedCount++;
                notes.Add("Gaming Mode è stato reinstallato/aggiornato.");
            }
            catch (GamingModeRepairDeferredException)
            {
                notes.Add("Riparazione agente differita: reinstalla Playhub per aggiornare l'agente in uso. La sessione corrente non è stata interrotta.");
            }
            catch
            {
                notes.Add("Gaming Mode non è stato riparato del tutto: riprova o reinstalla Playhub.");
            }
        }

        // ---------- 3) Configurazione di Gaming Mode ----------
        progress.Report((0.40, "Verifico la configurazione di Gaming Mode…"));
        int? apiPort = null;
        try
        {
            // Read only: model round-trips normalize user choices and discard unknown fields.
            apiPort = GamingModeRepairPayload.ReadApiPort(_gamingMode.ConfigFile);
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
        if (payloadReady && apiPort.HasValue && !await _gamingMode.IsAgentHealthyAsync(apiPort.Value))
        {
            found++;
            progress.Report((0.78, "Riavvio l'agente Gaming Mode…"));
            _gamingMode.StartAgent();
            var healthy = false;
            for (var i = 0; i < 20 && !healthy; i++)
            {
                await Task.Delay(250);
                healthy = await _gamingMode.IsAgentHealthyAsync(apiPort.Value);
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

        // ---------- 6) Collegamento di avvio (nessuna modifica alla shell) ----------
        progress.Report((0.86, "Verifico la modalità di avvio…"));
        try
        {
            var startup = await Task.Run(() => GamingModeRepairPayload.CheckStartup(_gamingMode.InstalledExe, payloadReady));
            if (startup != GamingModeStartupRepair.Healthy)
            {
                found++;
                if (startup == GamingModeStartupRepair.Repaired)
                {
                    fixedCount++;
                    notes.Add("Il collegamento di avvio dell'agente è stato riparato. Preferenze e modalità predefinita conservate.");
                }
                else notes.Add("Avvio agente non riparato: collegamento assente o personalizzato. Reinstalla Playhub se desideri riattivare l'avvio automatico.");
            }
        }
        catch
        {
            found++;
            notes.Add("Avvio agente non riparato: collegamento assente o personalizzato. Reinstalla Playhub se desideri riattivare l'avvio automatico.");
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


    // Un'unica definizione di "plugin da aggiornare", condivisa con il
    // controllo che gira a ogni avvio dell'app: due copie della stessa logica
    // finiscono sempre per divergere.
    private static bool IsDeckyCompanionBroken(string deckyPluginsPath) =>
        GamingModeService.NeedsDeckyPluginUpdate(deckyPluginsPath);


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
