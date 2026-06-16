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
        if (!File.Exists(AppPaths.SettingsFile))
        {
            Current.PluginRoot = AppPaths.LocalPluginRoot;
            Current.DeckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
            await SaveAsync();
            return Current;
        }

        var json = await File.ReadAllTextAsync(AppPaths.SettingsFile);
        Current = JsonSerializer.Deserialize<PlayhubSettings>(json, JsonOptions) ?? new PlayhubSettings();
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

        Current.Language = LocalizationService.NormalizeLanguageKey(Current.Language);

        return Current;
    }

    public async Task SaveAsync()
    {
        AppPaths.EnsureRoots();
        Current.Backdrop = NormalizeBackdrop(Current.Backdrop);
        Current.Language = LocalizationService.NormalizeLanguageKey(Current.Language);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(AppPaths.SettingsFile, json);
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
