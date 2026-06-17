using Playhub.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

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

    public async Task<string> InstallAsync()
    {
        var script = Path.Combine(AppPaths.GamingModePackage, "install.ps1");
        if (!File.Exists(script))
        {
            return "Non trovo install.ps1 nel pacchetto Gaming Mode locale.";
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SourceDir \"{AppPaths.GamingModePackage}\"";
        var result = await ProcessService.RunAsync("powershell.exe", args, AppPaths.GamingModePackage);
        return result.Success ? "Gaming Mode installato e agente avviato." : result.Error + result.Output;
    }

    public async Task<string> UninstallAsync()
    {
        var script = Path.Combine(AppPaths.GamingModePackage, "uninstall.ps1");
        if (!File.Exists(script))
        {
            return "Non trovo uninstall.ps1 nel pacchetto Gaming Mode locale.";
        }

        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"";
        var result = await ProcessService.RunAsync("powershell.exe", args, AppPaths.GamingModePackage);
        return result.Success ? "Gaming Mode rimosso." : result.Error + result.Output;
    }

    private static string DeckyPluginSource =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "GamingModeDeckyPlugin", "gaming-mode");

    public bool IsDeckyPluginInstalled(string deckyPluginsPath) =>
        !string.IsNullOrWhiteSpace(deckyPluginsPath) &&
        File.Exists(Path.Combine(deckyPluginsPath, "gaming-mode", "plugin.json"));

    /// <summary>Installs (or updates) the Gaming Mode Decky companion plugin into homebrew/plugins.</summary>
    public Task<string> InstallDeckyPluginAsync(string deckyPluginsPath)
    {
        try
        {
            if (!Directory.Exists(DeckyPluginSource))
            {
                return Task.FromResult("Non trovo i file del plugin Gaming Mode nel pacchetto.");
            }

            if (string.IsNullOrWhiteSpace(deckyPluginsPath))
            {
                deckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
            }

            Directory.CreateDirectory(deckyPluginsPath);
            var dest = Path.Combine(deckyPluginsPath, "gaming-mode");
            CopyDirectory(DeckyPluginSource, dest);
            return Task.FromResult("Plugin Gaming Mode installato in DeckyLoader. Riavvia Steam per vederlo nel menu rapido.");
        }
        catch (Exception ex)
        {
            return Task.FromResult("Installazione del plugin non riuscita: " + ex.Message);
        }
    }

    public Task<string> RemoveDeckyPluginAsync(string deckyPluginsPath)
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

            return Task.FromResult("Plugin Gaming Mode rimosso da DeckyLoader.");
        }
        catch (Exception ex)
        {
            return Task.FromResult("Rimozione del plugin non riuscita: " + ex.Message);
        }
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
        GamingModeConfig config;
        if (!File.Exists(ConfigFile))
        {
            config = CreateDefaultConfig();
        }
        else
        {
            var json = await File.ReadAllTextAsync(ConfigFile);
            config = JsonSerializer.Deserialize<GamingModeConfig>(json, JsonOptions) ?? CreateDefaultConfig();
        }

        // Config corrotta (es. salvata prima del caricamento dei controlli): un
        // MaxVisibleMs a 0 è impossibile in una config valida → ripristina i default.
        if (config.Gaming?.Splash is null || config.Gaming.Splash.MaxVisibleMs <= 0)
        {
            config = CreateDefaultConfig();
            await WriteConfigAsync(config);
            return config;
        }

        if (NormalizeConfig(config))
        {
            await WriteConfigAsync(config);
        }

        return config;
    }

    public async Task SaveConfigAsync(GamingModeConfig config)
    {
        NormalizeConfig(config);
        await WriteConfigAsync(config);
    }

    private async Task WriteConfigAsync(GamingModeConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(ConfigFile, json);
    }

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
                AllowRemoteApi = true, // API dell'agente abilitate di default (servono al programma)
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

        return changed;
    }
}
