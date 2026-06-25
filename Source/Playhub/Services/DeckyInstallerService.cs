using Microsoft.Win32;
using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.System;

namespace Playhub.Services;

/// <summary>
/// Native DeckyLoader installer for Windows.
///
/// This is an original, clean-room implementation: it performs the documented
/// DeckyLoader-on-Windows install steps directly in Playhub and does NOT bundle
/// or execute any third-party installer binary. The PluginLoader build is the
/// official one from SteamDeckHomebrew/decky-loader, fetched through nightly.link
/// (a public proxy that serves GitHub Actions artifacts without authentication).
///
/// Steps performed:
///   1. stop any running PluginLoader process
///   2. download the official "PluginLoader Win" artifact
///   3. create %USERPROFILE%\homebrew\services and extract into it
///   4. enable Steam CEF remote debugging (.cef-enable-remote-debugging)
///   5. (no desktop shortcut is created; any legacy one is removed)
///   6. register PluginLoader for autostart and launch it
/// </summary>
public sealed class DeckyInstallerService
{
    private const string Owner = "SteamDeckHomebrew";
    private const string Repo = "decky-loader";
    private const string ArtifactName = "PluginLoader%20Win.zip";

    private static readonly Uri WorkflowRunsUri = new(
        $"https://api.github.com/repos/{Owner}/{Repo}/actions/workflows/build-win.yml/runs?branch=main&status=success&per_page=20");

    // Latest main build, always available without auth via nightly.link.
    private static readonly string LatestZipUrl =
        $"https://nightly.link/{Owner}/{Repo}/workflows/build-win/main/{ArtifactName}";

    private readonly HttpClient _http = CreateHttpClient();

    /// <summary>True if DeckyLoader's PluginLoader is present under homebrew\services.</summary>
    public bool IsInstalled()
    {
        return File.Exists(Path.Combine(ServicesDir, "PluginLoader_noconsole.exe"))
            || File.Exists(Path.Combine(ServicesDir, "PluginLoader.exe"));
    }

    public bool IsDeveloperModeEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
        var value = key?.GetValue("AllowDevelopmentWithoutDevLicense");
        return value is int intValue && intValue == 1;
    }

    public async Task OpenDeveloperSettingsAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:developers"));
    }

    public async Task<IReadOnlyList<DeckyBuildRun>> GetMainBuildsAsync()
    {
        using var response = await _http.GetAsync(WorkflowRunsUri);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var runs = new List<DeckyBuildRun>();

        foreach (var item in doc.RootElement.GetProperty("workflow_runs").EnumerateArray())
        {
            runs.Add(new DeckyBuildRun
            {
                Id = item.GetProperty("id").GetInt64(),
                Title = item.GetProperty("display_title").GetString() ?? "",
                HeadSha = item.GetProperty("head_sha").GetString() ?? "",
                CreatedAt = NormalizeDate(item.GetProperty("created_at").GetString()),
                UpdatedAt = NormalizeDate(item.GetProperty("updated_at").GetString()),
                Url = item.GetProperty("html_url").GetString() ?? "",
                ArtifactsUrl = item.GetProperty("artifacts_url").GetString() ?? ""
            });
        }

        return runs;
    }

    /// <summary>Installs the latest main build.</summary>
    public Task<string> InstallLatestAsync() => InstallFromUrlAsync(LatestZipUrl, "ultima versione");

    /// <summary>Installs a specific build run, falling back to the latest if that run isn't mirrored.</summary>
    public async Task<string> InstallBuildAsync(DeckyBuildRun build)
    {
        var runUrl = $"https://nightly.link/{Owner}/{Repo}/actions/runs/{build.Id}/{ArtifactName}";
        var bytes = await TryDownloadAsync(runUrl);
        if (bytes is null)
        {
            // Older runs may not be mirrored; use the latest main build instead.
            return await InstallFromUrlAsync(LatestZipUrl, "ultima versione");
        }

        return await InstallFromBytesAsync(bytes, $"build {build.Id}");
    }

    private async Task<string> InstallFromUrlAsync(string url, string label)
    {
        var bytes = await TryDownloadAsync(url);
        if (bytes is null)
        {
            return "Non riesco a scaricare DeckyLoader (rete non disponibile o artifact assente). Riprova più tardi.";
        }

        return await InstallFromBytesAsync(bytes, label);
    }

    private async Task<string> InstallFromBytesAsync(byte[] zipBytes, string label)
    {
        if (zipBytes.Length < 500_000)
        {
            return "Il download di DeckyLoader sembra incompleto. Riprova.";
        }

        KillLoaders();

        var servicesDir = ServicesDir;
        Directory.CreateDirectory(servicesDir);

        Directory.CreateDirectory(AppPaths.DownloadsRoot);
        var zipPath = Path.Combine(AppPaths.DownloadsRoot, "PluginLoaderWin.zip");
        await File.WriteAllBytesAsync(zipPath, zipBytes);

        ZipFile.ExtractToDirectory(zipPath, servicesDir, overwriteFiles: true);
        try { File.Delete(zipPath); } catch { }

        var notes = new List<string>();
        notes.Add(EnableSteamCefDebugging() ? "debug CEF attivato" : "debug CEF non riuscito");
        notes.Add(SetupAutostartAndLaunch() ? "autostart impostato e loader avviato" : "autostart non riuscito");
        RemoveLegacyDesktopShortcut();

        return $"DeckyLoader installato ({label}): {string.Join(", ", notes)}. " +
               "Chiudi e riapri Steam per attivare DeckyLoader.";
    }

    public Task<string> RemoveAsync()
    {
        KillLoaders();

        // Autostart registry entry
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue("DeckyLoader", throwOnMissingValue: false);
        }
        catch { }

        // Remove any legacy "Steam (Decky)" desktop shortcut from older installs.
        RemoveLegacyDesktopShortcut();

        // CEF debug flag
        try
        {
            var steam = FindSteamPath();
            if (steam is not null)
            {
                var cef = Path.Combine(steam, ".cef-enable-remote-debugging");
                if (File.Exists(cef)) File.Delete(cef);
            }
        }
        catch { }

        if (Directory.Exists(ServicesDir))
        {
            try { Directory.Delete(ServicesDir, recursive: true); } catch { }
            return Task.FromResult("DeckyLoader rimosso. I plugin installati restano in homebrew/plugins.");
        }

        return Task.FromResult("DeckyLoader non risultava installato (puliti comunque scorciatoia e autostart).");
    }

    public async Task<bool> RestartWithSteamAsync(SteamService steam)
    {
        if (!IsInstalled())
        {
            return false;
        }

        KillLoaders();
        await steam.RestartSteamAsync();

        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (Process.GetProcessesByName("steam").Length > 0)
            {
                await Task.Delay(1200);
                return SetupAutostartAndLaunch();
            }
            await Task.Delay(500);
        }

        return false;
    }

    private async Task<byte[]?> TryDownloadAsync(string url)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    private bool EnableSteamCefDebugging()
    {
        try
        {
            var steam = FindSteamPath();
            if (steam is null || !Directory.Exists(steam))
            {
                return false;
            }

            File.WriteAllText(Path.Combine(steam, ".cef-enable-remote-debugging"), string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Deletes the obsolete "Steam (Decky)" desktop shortcut if a previous version created one.
    private static void RemoveLegacyDesktopShortcut()
    {
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Steam (Decky).lnk");
            if (File.Exists(lnk))
            {
                File.Delete(lnk);
            }
        }
        catch
        {
        }
    }

    private bool SetupAutostartAndLaunch()
    {
        try
        {
            var loader = Path.Combine(ServicesDir, "PluginLoader_noconsole.exe");
            if (!File.Exists(loader))
            {
                return false;
            }

            // Remove every existing PluginLoader autostart entry / running instance first,
            // otherwise two loaders try to bind port 1337 at boot (WinError 10048).
            CleanExistingAutostart();

            using (var run = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                run?.SetValue("DeckyLoader", "\"" + loader + "\"");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = loader,
                WorkingDirectory = ServicesDir,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Removes any leftover PluginLoader autostart so only a single instance ever runs.
    // Cleans: running processes, HKCU Run entries that point at PluginLoader, and
    // Startup-folder shortcuts named after Decky/PluginLoader.
    private void CleanExistingAutostart()
    {
        KillLoaders();

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run is not null)
            {
                foreach (var name in run.GetValueNames())
                {
                    var value = run.GetValue(name) as string ?? string.Empty;
                    if (value.Contains("PluginLoader", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Decky", StringComparison.OrdinalIgnoreCase))
                    {
                        try { run.DeleteValue(name, throwOnMissingValue: false); } catch { }
                    }
                }
            }
        }
        catch { }

        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startup))
            {
                foreach (var lnk in Directory.GetFiles(startup, "*.lnk"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(lnk);
                    if (fileName.Contains("Decky", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("PluginLoader", StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(lnk); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    private void KillLoaders()
    {
        foreach (var name in new[] { "PluginLoader", "PluginLoader_noconsole" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }
    }

    private static string ServicesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "services");

    private static string? FindSteamPath()
    {
        foreach (var sub in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(sub);
                if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                {
                    return path;
                }
            }
            catch { }
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        return client;
    }

    private static string NormalizeDate(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var date))
        {
            return date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        return value ?? "";
    }
}
