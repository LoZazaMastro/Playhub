using System;
using System.IO;

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
}
