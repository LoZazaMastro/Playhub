using Playhub.Models;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PlayhubSettings Current { get; private set; } = new();

    public async Task<PlayhubSettings> LoadAsync()
    {
        AppPaths.EnsureRoots();

        // Carica il file principale e, se mancante o corrotto (es. blackout a metà
        // scrittura), ricade sul backup dell'ultima versione valida.
        var loaded = AppPaths.ReadJsonWithBackup<PlayhubSettings>(AppPaths.SettingsFile, JsonOptions);
        if (loaded is null)
        {
            // Primo avvio reale (nessun file e nessun backup): crea le impostazioni di default.
            Current = new PlayhubSettings
            {
                Language = LocalizationService.DetectSystemLanguage(),
                PluginRoot = AppPaths.LocalPluginRoot,
                DeckyPluginsPath = AppPaths.DefaultDeckyPluginsPath
            };
            await SaveAsync();
            return Current;
        }

        Current = loaded;
        Current.SteamGridDbGameOverrides ??= new();
        Current.SteamGridDbTitleOverrides ??= new();
        Current.SteamGridDbArtworkDisabled ??= new();
        Current.ExecutableGameFolders ??= new();
        Current.ExecutableGameFiles ??= new();
        if (!string.IsNullOrWhiteSpace(Current.ExecutableGamesFolder) &&
            !Current.ExecutableGameFolders.Contains(Current.ExecutableGamesFolder, StringComparer.OrdinalIgnoreCase))
        {
            Current.ExecutableGameFolders.Add(Current.ExecutableGamesFolder);
        }
        if (string.IsNullOrWhiteSpace(Current.PluginRoot) || !Directory.Exists(Current.PluginRoot))
        {
            Current.PluginRoot = AppPaths.LocalPluginRoot;
        }

        if (string.IsNullOrWhiteSpace(Current.DeckyPluginsPath))
        {
            Current.DeckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
        }

        if (string.IsNullOrWhiteSpace(Current.Backdrop))
        {
            Current.Backdrop = "mica";
        }
        else
        {
            Current.Backdrop = NormalizeBackdrop(Current.Backdrop);
        }

        var storedLanguage = Current.Language;
        Current.Language = LocalizationService.NormalizeLanguageKey(Current.Language);
        if (!string.Equals(storedLanguage, Current.Language, StringComparison.OrdinalIgnoreCase))
        {
            // Migra definitivamente i vecchi valori "auto" verso la lingua
            // esplicita rilevata, così l'opzione rimossa non resta nel file.
            await SaveAsync();
        }

        return Current;
    }

    public async Task SaveAsync()
    {
        AppPaths.EnsureRoots();
        Current.Backdrop = NormalizeBackdrop(Current.Backdrop);
        Current.Language = LocalizationService.NormalizeLanguageKey(Current.Language);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await AppPaths.WriteAtomicAsync(AppPaths.SettingsFile, json);
    }

    private static string NormalizeBackdrop(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "acrylic";
        }

        var key = value.Trim().ToLowerInvariant();
        return key switch
        {
            "mica" => "mica",
            "acrylic" => "acrylic",
            "sfondo pieno" or "solid" or "solidbackground" or "sfondopieno" => "solid",
            _ => "acrylic"
        };
    }
}
