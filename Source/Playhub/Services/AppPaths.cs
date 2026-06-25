using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

public static class AppPaths
{
    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playhub");

    public static string LocalDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub");

    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");
    public static string DownloadsRoot => Path.Combine(LocalDataRoot, "downloads");
    public static string BackupsRoot => Path.Combine(AppDataRoot, "backups");
    public static string BundledPluginRoot => Path.Combine(AppContext.BaseDirectory, "Plugins");
    public static string LocalPluginRoot => ExistingDirectory(BundledPluginRoot, Environment.GetEnvironmentVariable("PLAYHUB_PLUGIN_ROOT") ?? "") ?? BundledPluginRoot;
    public static string BundledSteamCfg => Path.Combine(AppContext.BaseDirectory, "Assets", "Extra", "steam.cfg");
    public static string LocalSteamCfg => BundledSteamCfg;
    public static string GamingModePackage => ExistingDirectory(
        Path.Combine(BundledPluginRoot, "Gaming Mode", "gaming-mode-win-x64"),
        Path.Combine(Environment.GetEnvironmentVariable("PLAYHUB_PLUGIN_ROOT") ?? "", "Gaming Mode", "gaming-mode-win-x64")) ?? Path.Combine(BundledPluginRoot, "Gaming Mode", "gaming-mode-win-x64");
    public static string DeckyInstallerPackage => ExistingDirectory(
        Path.Combine(BundledPluginRoot, "DeckyLoader Installer", "Decky.Loader.Installer"),
        Path.Combine(Environment.GetEnvironmentVariable("PLAYHUB_PLUGIN_ROOT") ?? "", "DeckyLoader Installer", "Decky.Loader.Installer")) ?? Path.Combine(BundledPluginRoot, "DeckyLoader Installer", "Decky.Loader.Installer");
    public static string UwpHookPackage => ExistingDirectory(
        Path.Combine(AppContext.BaseDirectory, "UWPHook"),
        Path.Combine(BundledPluginRoot, "UWPHook"),
        Path.Combine(Environment.GetEnvironmentVariable("PLAYHUB_PLUGIN_ROOT") ?? "", "UWPHook")) ?? Path.Combine(AppContext.BaseDirectory, "UWPHook");
    public static string DefaultDeckyPluginsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "homebrew",
        "plugins");

    public static void EnsureRoots()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LocalDataRoot);
        Directory.CreateDirectory(DownloadsRoot);
        Directory.CreateDirectory(BackupsRoot);
    }

    private static string? ExistingDirectory(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    // Scrittura atomica e resistente ai blackout: il contenuto viene scritto su un
    // file temporaneo (con flush forzato su disco), poi sostituisce il file di
    // destinazione conservando una copia ".bak" dell'ultima versione valida. In
    // questo modo un'interruzione di corrente a metà scrittura non può lasciare il
    // file principale troncato o vuoto (causa dei reset di tutte le opzioni).
    public static async Task WriteAtomicAsync(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        var bak = path + ".bak";

        var bytes = new UTF8Encoding(false).GetBytes(contents);
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(bytes);
            await fs.FlushAsync();
            try { fs.Flush(true); } catch { } // fsync: forza i buffer del SO su disco.
        }

        try
        {
            if (File.Exists(path))
            {
                // Atomico su NTFS; crea/aggiorna il backup dell'ultima versione valida.
                File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch
        {
            // Ripiego: sostituzione diretta (meno atomica, ma non perde il salvataggio).
            try
            {
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
            }
            catch
            {
            }
        }
    }

    // Lettura tollerante ai guasti: prova il file principale e, se manca o è
    // illeggibile/corrotto (JSON non valido), ricade sul backup ".bak".
    public static T? ReadJsonWithBackup<T>(string path, JsonSerializerOptions options) where T : class
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                var text = File.ReadAllText(candidate);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var value = JsonSerializer.Deserialize<T>(text, options);
                if (value is not null)
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        return null;
    }
}
