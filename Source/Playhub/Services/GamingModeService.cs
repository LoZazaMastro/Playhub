using Playhub.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed record GamingModeOperationResult(bool Success, string Message);

public sealed class GamingModeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamingMode");
    public string ConfigFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode", "config.json");
    public string InstalledExe => Path.Combine(InstallDir, "GamingMode.exe");
    public bool IsInstalled => File.Exists(InstalledExe);

    public async Task<GamingModeOperationResult> InstallAsync(string deckyPluginsPath)
    {
        var script = Path.Combine(AppPaths.GamingModePackage, "install.ps1");
        if (!File.Exists(script))
        {
            return new(false, "Non trovo install.ps1 nel pacchetto Gaming Mode locale.");
        }

        var companionPath = ResolveDeckyPluginPath(deckyPluginsPath);
        var companionDirectoryExisted = Directory.Exists(companionPath);
        var companionResult = InstallDeckyPlugin(deckyPluginsPath);
        if (!companionResult.Success)
        {
            return new(false, companionResult.Message);
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SourceDir \"{AppPaths.GamingModePackage}\"";
        var result = await ProcessService.RunAsync("powershell.exe", args, AppPaths.GamingModePackage);
        if (!result.Success)
        {
            // A first installation must never leave behind only half of the pair.
            if (!companionDirectoryExisted)
            {
                RemoveDeckyPlugin(deckyPluginsPath);
            }

            return new(false, result.Error + result.Output);
        }

        return new(true, "Gaming Mode e Companion per DeckyLoader sono pronti. Riavvia Steam per vedere il plugin nel menu rapido.");
    }

    public async Task<GamingModeOperationResult> UninstallAsync(string deckyPluginsPath)
    {
        var script = Path.Combine(AppPaths.GamingModePackage, "uninstall.ps1");
        if (!File.Exists(script))
        {
            return new(false, "Non trovo uninstall.ps1 nel pacchetto Gaming Mode locale.");
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"";
        var result = await ProcessService.RunAsync("powershell.exe", args, AppPaths.GamingModePackage);
        if (!result.Success)
        {
            // Keep the Companion available as an exit route until Gaming Mode is gone.
            return new(false, result.Error + result.Output);
        }

        var companionResult = RemoveDeckyPlugin(deckyPluginsPath);
        return companionResult.Success
            ? new(true, "Gaming Mode e Companion per DeckyLoader sono stati rimossi.")
            : new(false, companionResult.Message);
    }

    private static string DeckyPluginSource =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "GamingModeDeckyPlugin", "gaming-mode");

    public bool IsDeckyPluginInstalled(string deckyPluginsPath) =>
        !string.IsNullOrWhiteSpace(deckyPluginsPath) &&
        File.Exists(Path.Combine(deckyPluginsPath, "gaming-mode", "plugin.json"));

    /// <summary>
    /// Dice se l'agente installato e' diverso da quello che Playhub porta nel
    /// pacchetto.
    /// </summary>
    /// <remarks>
    /// Serve perché senza questo controllo l'agente restava indietro in
    /// silenzio: si installava una versione nuova di Playhub, ma l'agente in
    /// esecuzione era ancora quello di prima. Le correzioni sembravano non
    /// avere effetto, e non c'era modo di accorgersene se non leggendo la data
    /// dell'eseguibile.
    /// </remarks>
    public bool NeedsAgentUpdate()
    {
        try
        {
            var bundled = Path.Combine(AppPaths.GamingModePackage, "GamingMode.exe");
            if (!File.Exists(bundled)) return false;   // pacchetto assente: non si tocca niente
            if (!File.Exists(InstalledExe)) return true;

            var source = new FileInfo(bundled);
            var installed = new FileInfo(InstalledExe);
            return installed.Length != source.Length ||
                   installed.LastWriteTimeUtc < source.LastWriteTimeUtc;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Allinea l'agente a quello del pacchetto, in silenzio. Restituisce true
    /// solo se ha davvero aggiornato qualcosa.
    /// </summary>
    /// <remarks>
    /// Lo script di installazione ferma l'agente, sostituisce i file e lo
    /// riavvia da solo: qui basta chiamarlo. Va fatto a ogni avvio dell'app,
    /// come per il plugin di Decky.
    /// </remarks>
    public async Task<bool> SyncAgentAsync()
    {
        try
        {
            if (!NeedsAgentUpdate()) return false;

            var script = Path.Combine(AppPaths.GamingModePackage, "install.ps1");
            if (!File.Exists(script)) return false;

            var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SourceDir \"{AppPaths.GamingModePackage}\"";
            var result = await ProcessService.RunAsync("powershell.exe", args, AppPaths.GamingModePackage);
            if (!result.Success) return false;

            // Lo script riavvia l'agente, ma se per qualsiasi motivo non
            // rispondesse lo si riaccende qui: restare senza agente
            // significherebbe Gaming Mode muta.
            for (var i = 0; i < 12; i++)
            {
                if (await IsAgentHealthyAsync()) return true;
                await Task.Delay(250);
            }
            StartAgent();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Dice se il plugin installato in DeckyLoader e' diverso da quello che
    /// Playhub porta nel pacchetto (mancante, incompleto o piu' vecchio).
    /// </summary>
    /// <remarks>
    /// Una sola definizione di "da aggiornare", usata sia dal controllo
    /// all'avvio sia da "Ripara tutto": se le due logiche divergono, una delle
    /// due finisce per non aggiornare mai niente.
    /// </remarks>
    public static bool NeedsDeckyPluginUpdate(string deckyPluginsPath)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(deckyPluginsPath)
                ? AppPaths.DefaultDeckyPluginsPath
                : deckyPluginsPath;

            // Decky non e' in uso: non c'e' niente da aggiornare.
            if (!Directory.Exists(root))
            {
                return false;
            }

            // Pacchetto assente (build incompleta): meglio non toccare niente.
            if (!Directory.Exists(DeckyPluginSource))
            {
                return false;
            }

            var dest = Path.Combine(root, "gaming-mode");
            if (!Directory.Exists(dest))
            {
                return true;
            }

            foreach (var file in Directory.GetFiles(DeckyPluginSource, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(DeckyPluginSource, file);
                var installed = Path.Combine(dest, rel);
                if (!File.Exists(installed))
                {
                    return true;
                }

                var source = new FileInfo(file);
                var current = new FileInfo(installed);
                // La lunghezza da sola non basta: una modifica al bundle puo'
                // lasciarla identica. La data di scrittura sopravvive a
                // File.Copy, quindi il confronto regge fra una versione e
                // l'altra di Playhub.
                if (current.Length != source.Length ||
                    current.LastWriteTimeUtc < source.LastWriteTimeUtc)
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

    /// <summary>
    /// Allinea il plugin di DeckyLoader a quello del pacchetto, in silenzio.
    /// Restituisce true solo se ha davvero copiato qualcosa.
    /// </summary>
    /// <remarks>
    /// Va chiamata a ogni avvio dell'app. E' cosi' che il plugin arriva
    /// all'utente senza che debba premere niente: dopo l'installazione di
    /// Playhub, e dopo ogni aggiornamento. Prima esisteva solo il pulsante
    /// "Installa o aggiorna", quindi chi non lo premeva restava con la
    /// versione vecchia del plugin per sempre.
    /// </remarks>
    public Task<bool> SyncDeckyPluginAsync(string deckyPluginsPath) => Task.Run(() =>
    {
        try
        {
            if (!NeedsDeckyPluginUpdate(deckyPluginsPath))
            {
                return false;
            }

            return InstallDeckyPlugin(deckyPluginsPath).Success;
        }
        catch
        {
            return false;
        }
    });

    /// <summary>Installs (or updates) the Gaming Mode Decky companion plugin into homebrew/plugins.</summary>
    public Task<string> InstallDeckyPluginAsync(string deckyPluginsPath)
    {
        return Task.FromResult(InstallDeckyPlugin(deckyPluginsPath).Message);
    }

    private static GamingModeOperationResult InstallDeckyPlugin(string deckyPluginsPath)
    {
        try
        {
            if (!Directory.Exists(DeckyPluginSource))
            {
                return new(false, "Non trovo i file del plugin Gaming Mode nel pacchetto.");
            }

            if (string.IsNullOrWhiteSpace(deckyPluginsPath))
            {
                deckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
            }

            Directory.CreateDirectory(deckyPluginsPath);
            var dest = Path.Combine(deckyPluginsPath, "gaming-mode");
            CopyDirectory(DeckyPluginSource, dest);
            return new(true, "Plugin Gaming Mode installato in DeckyLoader. Riavvia Steam per vederlo nel menu rapido.");
        }
        catch (Exception ex)
        {
            return new(false, "Installazione del plugin non riuscita: " + ex.Message);
        }
    }

    public Task<string> RemoveDeckyPluginAsync(string deckyPluginsPath)
    {
        return Task.FromResult(RemoveDeckyPlugin(deckyPluginsPath).Message);
    }

    private static GamingModeOperationResult RemoveDeckyPlugin(string deckyPluginsPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deckyPluginsPath))
            {
                deckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
            }

            var dest = Path.Combine(deckyPluginsPath, "gaming-mode");
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, recursive: true);
            }

            return new(true, "Plugin Gaming Mode rimosso da DeckyLoader.");
        }
        catch (Exception ex)
        {
            return new(false, "Rimozione del plugin non riuscita: " + ex.Message);
        }
    }

    private static string ResolveDeckyPluginPath(string deckyPluginsPath)
    {
        var root = string.IsNullOrWhiteSpace(deckyPluginsPath)
            ? AppPaths.DefaultDeckyPluginsPath
            : deckyPluginsPath;
        return Path.Combine(root, "gaming-mode");
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, dest), overwrite: true);
        }
    }

    public void OpenCompanion()
    {
        if (File.Exists(InstalledExe))
        {
            ProcessService.StartDetached(InstalledExe, workingDirectory: InstallDir);
        }
        else
        {
            // Fallback: avvia l'eseguibile del Gaming Mode dal pacchetto bundle.
            // (Niente più Setup.exe: era un installer standalone ridondante.)
            var bundled = Path.Combine(AppPaths.GamingModePackage, "GamingMode.exe");
            if (File.Exists(bundled))
            {
                ProcessService.StartDetached(bundled, workingDirectory: AppPaths.GamingModePackage);
            }
        }
    }

    public void StartAgent()
    {
        if (File.Exists(InstalledExe))
        {
            ProcessService.StartDetached(InstalledExe, "agent", InstallDir, hidden: true);
        }
    }

    public async Task<bool> IsAgentHealthyAsync(int port = 47991)
    {
        try
        {
            var response = await _http.GetAsync($"http://127.0.0.1:{port}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Switch IMMEDIATO di modalità tramite l'agente locale, esattamente come fa
    // il plugin DeckyLoader (POST http://127.0.0.1:PORT/mode/<mode>/switch).
    // È l'agente a salvare la modalità ed eseguire il sign-out + cambio shell.
    public async Task<bool> SwitchModeAsync(string mode, int port = 47991)
    {
        var path = string.Equals(mode, "Gaming", StringComparison.OrdinalIgnoreCase)
            ? "/mode/gaming/switch"
            : "/mode/desktop/switch";
        return await PostAgentAsync(path, port);
    }

    // Imposta la modalità predefinita tramite l'agente, come il plugin
    // (POST http://127.0.0.1:PORT/default/<mode>).
    public async Task<bool> SetDefaultModeViaAgentAsync(string mode, int port = 47991)
    {
        var path = string.Equals(mode, "Gaming", StringComparison.OrdinalIgnoreCase)
            ? "/default/gaming"
            : "/default/desktop";
        return await PostAgentAsync(path, port);
    }

    public async Task<bool> OpenDashboardAsync(int port = 47991)
    {
        try
        {
            using var response = await _http.PostAsync($"http://127.0.0.1:{port}/dash/open", null);
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> PostAgentAsync(string path, int port)
    {
        try
        {
            using var response = await _http.PostAsync($"http://127.0.0.1:{port}{path}", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<GamingModeConfig> LoadConfigAsync()
    {
        var config = TryReadConfig(ConfigFile);

        // Config principale mancante, illeggibile o logicamente corrotta (es.
        // troncata da un blackout, o salvata a zero prima del caricamento dei
        // controlli): prova a recuperare il backup dell'ultima versione valida.
        if (!IsConfigValid(config))
        {
            var backup = TryReadConfig(ConfigFile + ".bak");
            if (IsConfigValid(backup))
            {
                config = backup;
                await WriteConfigAsync(config!); // ripristina il file principale dal backup
            }
        }

        // Se nemmeno il backup è valido, riparti dai default.
        if (!IsConfigValid(config))
        {
            config = CreateDefaultConfig();
            await WriteConfigAsync(config);
            return config;
        }

        if (NormalizeConfig(config!))
        {
            await WriteConfigAsync(config!);
        }

        return config!;
    }

    public async Task SaveConfigAsync(GamingModeConfig config)
    {
        NormalizeConfig(config);

        // SI RILEGGE IL FILE UN ISTANTE PRIMA DI RISCRIVERLO.
        //
        // Questo file lo scrivono in due: Playhub e l'agente Gaming Mode. La
        // scrittura e' atomica da entrambe le parti, ma "atomica" garantisce
        // solo che non resti un file a meta': l'ultimo che scrive sostituisce
        // comunque tutto. Se l'interfaccia aveva letto la configurazione dieci
        // minuti prima, salvando riportava indietro il file a com'era allora.
        //
        // Cosi' si vedevano sparire le cose cambiate nel frattempo dal plugin:
        // la combinazione da tastiera tornava a quella predefinita e le app
        // preferite della Dashboard svanivano.
        //
        // Qui si recuperano dal disco i campi che questo modello non conosce,
        // appena prima di scrivere: cio' che l'app gestisce lo decide l'app,
        // tutto il resto resta quello che c'e' adesso sul disco.
        try
        {
            var current = TryReadConfig(ConfigFile);
            if (current is not null)
            {
                config.Extra = current.Extra;
                if (config.Gaming is not null) config.Gaming.Extra = current.Gaming?.Extra;
                if (config.Safety is not null) config.Safety.Extra = current.Safety?.Extra;
            }
        }
        catch
        {
            // Se il file non e' leggibile si salva comunque: meglio la
            // configurazione dell'app che nessuna configurazione.
        }

        await WriteConfigAsync(config);
    }

    private async Task WriteConfigAsync(GamingModeConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        // Scrittura atomica con backup: un blackout a metà salvataggio non può più
        // azzerare la config (causa del reset di tutte le opzioni di Gaming Mode).
        await AppPaths.WriteAtomicAsync(ConfigFile, json);
    }

    private GamingModeConfig? TryReadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<GamingModeConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // Una config valida ha sempre la sezione Splash con un MaxVisibleMs > 0:
    // valori a 0/null indicano un file troncato o salvato prima del caricamento.
    private static bool IsConfigValid(GamingModeConfig? config) =>
        config?.Gaming?.Splash is not null && config.Gaming.Splash.MaxVisibleMs > 0;

    public async Task SetNextBootModeAsync(string mode)
    {
        var config = await LoadConfigAsync();
        config.NextBootMode = mode;
        await SaveConfigAsync(config);
    }

    public static GamingModeConfig CreateDefaultConfig()
    {
        return new GamingModeConfig
        {
            DefaultMode = "Desktop",
            Gaming = new GamingOptions
            {
                SteamArguments = "-gamepadui",
                DeckyRequired = true,
                SunshineRequired = true,
                DelaySteamAfterDeckyMs = 1500,
                CloseExplorerInGamingMode = true,
                AllowExplorerCloseInGamingMode = true,
                RestoreExplorerOnDesktop = true,
                EnsureInputCompatibilityInGamingMode = true,
                EnsureSunshineCompatibilityInGamingMode = true,
                AutoHideMouseCursorInGamingMode = true,
                AutoHideMouseCursorAfterMs = 500,
                BorderlessFullscreenWindowsInGamingMode = true,
                Splash = new SplashOptions
                {
                    Enabled = true,
                    MinVisibleMs = 1200,
                    MaxVisibleMs = 120000
                }
            },
            Safety = new SafetyOptions
            {
                ApiPort = 47991,
                // Playhub parla con l'agente solo in loopback (127.0.0.1): l'accesso
                // remoto (LAN) non serve, quindi è off di default per sicurezza.
                // Resta comunque attivabile dal toggle "Consenti API remote".
                AllowRemoteApi = false,
                RestartWithoutPrompt = true
            }
        };
    }

    private static bool NormalizeConfig(GamingModeConfig config)
    {
        var changed = false;

        if (config.Gaming is null)
        {
            config.Gaming = new GamingOptions();
            changed = true;
        }

        if (config.Safety is null)
        {
            config.Safety = new SafetyOptions();
            changed = true;
        }

        if (config.Gaming.Splash is null)
        {
            config.Gaming.Splash = new SplashOptions();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.Gaming.SteamArguments))
        {
            config.Gaming.SteamArguments = "-gamepadui";
            changed = true;
        }

        changed |= EnsureSafetyWatcher(config);
        changed |= EnsureFocusRescueWatcher(config);
        changed |= EnsureGameBarWatcher(config);

        return changed;
    }

    private const string SafetyWatcherName = "Playhub Desktop Safety";

    // Registra (e tiene aggiornato) il watcher di sicurezza tra i processi
    // personalizzati: l'agente lo lancia in Gaming Mode e riporta al Desktop
    // quando Steam si chiude, così l'utente non resta bloccato.
    private static bool EnsureSafetyWatcher(GamingModeConfig config)
    {
        string scriptPath;
        try
        {
            scriptPath = Path.Combine(AppContext.BaseDirectory, "Assets", "GamingMode", "desktop-safety.ps1");
        }
        catch
        {
            return false;
        }

        // L'agente risolve "path" come file su disco e NON cerca nel PATH di
        // sistema: va quindi indicato il percorso COMPLETO di powershell.exe,
        // altrimenti il processo viene saltato ("path was not found: powershell.exe").
        string powershellPath;
        try
        {
            powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
        }
        catch
        {
            powershellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"";
        var existing = config.Gaming.CustomStartupApps
            .FirstOrDefault(a => string.Equals(a.Name, SafetyWatcherName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            config.Gaming.CustomStartupApps.Insert(0, new StartupAppConfig
            {
                Name = SafetyWatcherName,
                Path = powershellPath,
                Arguments = args,
                ProcessName = "playhub-gm-safety",
                Enabled = true,
                StartMinimized = true
            });
            return true;
        }

        if (existing.Arguments != args || existing.Path != powershellPath || !existing.Enabled)
        {
            existing.Path = powershellPath;
            existing.Arguments = args;
            existing.Enabled = true;
            return true;
        }

        return false;
    }

    private const string FocusRescueName = "Playhub Focus Rescue";

    // Registra (e tiene aggiornato) l'helper "focus rescue" tra i processi
    // personalizzati: l'agente lo lancia in Gaming Mode. Quando il plugin Decky
    // segnala l'apertura del menu Steam/QAM sopra un gioco, l'helper porta la
    // finestra Big Picture in primo piano (il foreground lock di Windows lo
    // impedirebbe) SENZA toccare il borderless fullscreen dell'agente.
    // A differenza del watcher di sicurezza NON forza Enabled=true: chi vuole
    // può disattivarlo dal config e la scelta viene rispettata.
    private static bool EnsureFocusRescueWatcher(GamingModeConfig config)
    {
        string scriptPath;
        try
        {
            scriptPath = Path.Combine(AppContext.BaseDirectory, "Assets", "GamingMode", "focus-rescue.ps1");
        }
        catch
        {
            return false;
        }

        // Percorso COMPLETO di powershell.exe: l'agente non cerca nel PATH.
        string powershellPath;
        try
        {
            powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
        }
        catch
        {
            powershellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"";
        var existing = config.Gaming.CustomStartupApps
            .FirstOrDefault(a => string.Equals(a.Name, FocusRescueName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            config.Gaming.CustomStartupApps.Add(new StartupAppConfig
            {
                Name = FocusRescueName,
                Path = powershellPath,
                Arguments = args,
                ProcessName = "playhub-gm-focus",
                Enabled = true,
                StartMinimized = true
            });
            return true;
        }

        if (existing.Arguments != args || existing.Path != powershellPath)
        {
            existing.Path = powershellPath;
            existing.Arguments = args;
            return true;
        }

        return false;
    }

    private const string GameBarWatcherName = "Playhub Xbox Game Bar";

    // Registra (o rimuove) il watcher della Xbox Game Bar. Se il supporto Steam
    // Controller è attivo, la Game Bar resta fuori: il controller non espone un vero tasto Nexus
    // e Windows/Steam possono mostrare errori o interferire con l'avvio dei giochi.
    private static bool EnsureGameBarWatcher(GamingModeConfig config)
    {
        var existing = config.Gaming.CustomStartupApps
            .FirstOrDefault(a => string.Equals(a.Name, GameBarWatcherName, StringComparison.OrdinalIgnoreCase));

        // Toggle OFF: rimuovi la voce se presente.
        if (!config.Gaming.EnableXboxGameBar)
        {
            if (existing is not null)
            {
                config.Gaming.CustomStartupApps.Remove(existing);
                return true;
            }
            return false;
        }

        string scriptPath;
        try
        {
            scriptPath = Path.Combine(AppContext.BaseDirectory, "Assets", "GamingMode", "xbox-gamebar.ps1");
        }
        catch
        {
            return false;
        }

        // Percorso COMPLETO di powershell.exe: l'agente non cerca nel PATH.
        string powershellPath;
        try
        {
            powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
        }
        catch
        {
            powershellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"";

        if (existing is null)
        {
            config.Gaming.CustomStartupApps.Add(new StartupAppConfig
            {
                Name = GameBarWatcherName,
                Path = powershellPath,
                Arguments = args,
                ProcessName = "playhub-gm-gamebar",
                Enabled = true,
                StartMinimized = true
            });
            return true;
        }

        if (existing.Arguments != args || existing.Path != powershellPath || !existing.Enabled)
        {
            existing.Path = powershellPath;
            existing.Arguments = args;
            existing.Enabled = true;
            return true;
        }

        return false;
    }
}
