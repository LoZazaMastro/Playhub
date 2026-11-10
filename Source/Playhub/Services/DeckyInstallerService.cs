using Microsoft.Win32;
using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Playhub.DeckyHost;
using Playhub.Shared;

namespace Playhub.Services;

/// <summary>
/// Native DeckyLoader installer for Windows.
///
/// This is an original, clean-room implementation: it performs the documented
/// DeckyLoader-on-Windows install steps directly in Playhub and does NOT bundle
/// or execute any third-party installer binary. The PluginLoader build is the
/// official Windows pair from SteamDeckHomebrew/decky-loader releases.
/// Verify and stage both binaries before touching the running installation;
/// roll back the pair if the new loader fails to start.
/// </summary>
public sealed class DeckyInstallerService
{
    private const int DeckyPort = 1337;
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private static readonly SemaphoreSlim DesktopOperationGate = new(1, 1);
    private readonly DeckyWindowsReleaseClient _releases;

    public DeckyInstallerService() : this(SharedHttp) { }
    internal DeckyInstallerService(HttpClient http) => _releases = new(http, RuntimeInformation.OSArchitecture);

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

    public Task<IReadOnlyList<DeckyWindowsRelease>> GetWindowsReleasesAsync() => _releases.ListAsync();

    public Task<string> InstallLatestAsync() => InstallWindowsReleaseAsync(reinstall: true);
    public Task<string> InstallOrUpdateLatestAsync() => InstallWindowsReleaseAsync();
    public Task<string> InstallLatestConsoleAsync() => InstallWindowsReleaseAsync(console: true);
    public Task<string> InstallReleaseAsync(DeckyWindowsRelease release) => InstallWindowsReleaseAsync(release.Tag);

    private Task<string> InstallWindowsReleaseAsync(string? tag = null, bool? console = null, bool reinstall = false) =>
        RunDesktopOperationAsync(() => InstallWindowsReleaseCoreAsync(tag, console, reinstall));

    private async Task<string> InstallWindowsReleaseCoreAsync(string? tag, bool? console, bool reinstall)
    {
        var release = await _releases.GetAsync(tag).ConfigureAwait(false);
        var assets = release.Assets;
        var services = ServicesDir;
        var matches = await Task.Run(() => DeckyFileUpgrade.Matches(services, assets)).ConfigureAwait(false);
        IReadOnlyDictionary<DeckyWindowsReleaseClient.Asset, byte[]>? payload = null;
        if (!matches || reinstall)
        {
            var downloaded = await Task.WhenAll(assets.Select(async asset =>
                (Asset: asset, Bytes: await _releases.DownloadAsync(asset).ConfigureAwait(false)))).ConfigureAwait(false);
            payload = downloaded.ToDictionary(item => item.Asset, item => item.Bytes);
        }

        return await Task.Run(() => DeckyStartupGuard.RunExclusive(() =>
        {
            // Validate the listener before touching binaries. Never reuse or stop a
            // same-named process owned by another installation, session or user.
            var previous = CurrentListener();
            string loader = LoaderPath(console ?? (previous?.EndsWith(DeckyFileUpgrade.ConsoleName, StringComparison.OrdinalIgnoreCase) == true ||
                (previous is null && PreferConsole())));
            string runtimePointer = Path.Combine(services, "PlayhubRuntime", "current");
            DeckyFileUpgrade.RequireOrdinaryPath(runtimePointer);
            string? previousRuntime = File.Exists(runtimePointer) ? File.ReadAllText(runtimePointer) : null;
            string? startupCommand = null;
            void Prepare()
            {
                EnsureRuntimeHostInstalled();
                startupCommand = PrepareAutostart(loader);
                EnableSteamCefDebugging();
            }
            void Start()
            {
                StartVerified(loader);
                SetAutostart(startupCommand!);
            }
            void Restore()
            {
                if (previousRuntime is not null) WriteRuntimePointer(runtimePointer, previousRuntime);
                else
                {
                    DeckyFileUpgrade.RequireOrdinaryPath(runtimePointer);
                    if (File.Exists(runtimePointer)) File.Delete(runtimePointer);
                }
                if (previous is not null && DeckyStartupGuard.ListenerOwner(DeckyPort) is null) StartVerified(previous);
            }

            if (!reinstall && DeckyFileUpgrade.Matches(services, assets))
            {
                bool switchLoader = previous is not null && !string.Equals(previous, loader, StringComparison.OrdinalIgnoreCase);
                bool starting = false;
                try
                {
                    Prepare();
                    if (switchLoader) StopForReplacement();
                    starting = true;
                    Start();
                }
                catch
                {
                    if (starting && (switchLoader || previous is null)) StopOwnedLoaders();
                    Restore();
                    throw;
                }
                return tag is null ? "La versione installata di Decky è già la più recente." :
                    "Decky è pronto. Riavvia Steam per attivarlo.";
            }

            if (payload is null) throw new IOException("I file di Decky sono cambiati durante l'aggiornamento. Riprova.");
            DeckyFileUpgrade.Apply(services, payload, Prepare, StopForReplacement, Start, StopOwnedLoaders, Restore);
            RemoveLegacyDesktopShortcut();
            return "Decky è pronto. Riavvia Steam per attivarlo.";
        })).ConfigureAwait(false);
    }

    public Task<string> RemoveAsync() =>
        RunDesktopOperationAsync(() => Task.Run(() => DeckyStartupGuard.RunExclusive(Remove)));

    private string Remove()
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
            return "Decky è stato rimosso. I plugin installati restano al loro posto.";
        }

        return "Decky non era installato. I collegamenti rimasti sono stati rimossi.";
    }

    public Task<bool> RestartWithSteamAsync(SteamService steam) =>
        RunDesktopOperationAsync(() => Task.Run(() => RestartSequence.SteamAndDeckyAsync(IsInstalled(),
            () => DeckyStartupGuard.RunExclusive(() => { StopForReplacement(); return true; }), steam.RestartSteamAsync,
            () => SetupAutostartAndLaunch(), milliseconds => Task.Delay(milliseconds),
            error => _startupError = error.Message)));

    // Keep desktop install, removal and restart ordered across awaits. The
    // thread-affine startup mutex is acquired only within synchronous sections.
    internal static async Task<T> RunDesktopOperationAsync<T>(Func<Task<T>> operation)
    {
        await DesktopOperationGate.WaitAsync().ConfigureAwait(false);
        try { return await operation().ConfigureAwait(false); }
        finally { DesktopOperationGate.Release(); }
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

            string marker = Path.Combine(steam, ".cef-enable-remote-debugging");
            if (!File.Exists(marker)) File.WriteAllText(marker, string.Empty);
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

    private string _startupError = "";
    private bool SetupAutostartAndLaunch()
    {
        _startupError = "";
        try
        {
            return DeckyStartupGuard.RunExclusive(() =>
            {
                EnsureRuntimeHostInstalled();
                bool console = PreferConsole();
                string loader = CurrentListener() ?? LoaderPath(console);
                if (!File.Exists(loader)) loader = LoaderPath(!console);
                if (!File.Exists(loader)) throw new IOException("Non trovo PluginLoader nei file di Decky. Reinstalla Decky.");
                string command = PrepareAutostart(loader);
                StartVerified(loader);
                SetAutostart(command);
                return true;
            });
        }
        catch (Exception error)
        {
            _startupError = error.Message;
            return false;
        }
    }

    private static string LoaderPath(bool console) => DeckyFileUpgrade.GetLoaderPath(ServicesDir,
        console ? DeckyFileUpgrade.ConsoleName : DeckyFileUpgrade.BackgroundName);

    private static bool PreferConsole()
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        string command = (run?.GetValue("DeckyLoader") as string ?? "").Trim();
        return command.EndsWith(" -Loader " + DeckyFileUpgrade.ConsoleName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Trim('"'), LoaderPath(console: true), StringComparison.OrdinalIgnoreCase);
    }

    private static string? CurrentListener()
    {
        if (DeckyStartupGuard.ListenerOwner(DeckyPort) is not int listener) return null;
        foreach (bool console in new[] { false, true })
        {
            var loader = LoaderPath(console);
            if (DeckyProcessTree.FindRoot(listener, loader) is not null) return loader;
        }
        throw new IOException("La porta 1337 è occupata da un'altra installazione o applicazione. Playhub non l'ha fermata.");
    }

    private static string PrepareAutostart(string loader)
    {
        DeckyFileUpgrade.RequireOrdinaryPath(Path.Combine(ServicesDir, "Playhub-StartDecky.ps1"));
        return DeckyStartupGuard.CreateCommand(loader);
    }

    private static void SetAutostart(string command)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? throw new IOException("Impossibile impostare l'avvio automatico di Decky.");
        run.SetValue("DeckyLoader", command, RegistryValueKind.String);
    }

    private static void StartVerified(string loader)
    {
        _ = CurrentListener();
        if (!File.Exists(loader) || !DeckyStartupGuard.StartOrReuse(new ProcessStartInfo
        {
            FileName = loader,
            WorkingDirectory = ServicesDir,
            UseShellExecute = false
        }) || !string.Equals(CurrentListener(), loader, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Il loader Decky atteso non ha aperto la porta 1337.");
    }

    private void StopForReplacement()
    {
        _ = CurrentListener();
        StopOwnedLoaders();
        if (DeckyStartupGuard.ListenerOwner(DeckyPort) is not null)
            throw new IOException("Decky non si è ancora fermato. Riprova dopo la chiusura del loader.");
    }

    private void StopOwnedLoaders()
    {
        KillLoaders();
        var console = LoaderPath(console: true);
        var background = LoaderPath(console: false);
        if (DeckyProcessTree.HasLiveLoader(console) || DeckyProcessTree.HasLiveLoader(background))
            throw new IOException("Decky non si è ancora fermato. Riprova dopo la chiusura del loader.");
        WaitForLifetimeHostExit(console);
        WaitForLifetimeHostExit(background);
    }

    private static void WaitForLifetimeHostExit(string loader)
    {
        // The wrapper releases its per-loader mutex only after PyInstaller's
        // private extraction cleanup. A replacement started before then exits
        // immediately as a duplicate even when the previous listener is gone.
        string name = "Local\\Playhub.DeckyHost." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loader.ToUpperInvariant())));
        var deadline = Stopwatch.StartNew();
        while (Mutex.TryOpenExisting(name, out var host))
        {
            host.Dispose();
            if (deadline.Elapsed >= TimeSpan.FromSeconds(5))
                throw new IOException("Il servizio di avvio di Decky sta ancora chiudendo il loader. Riprova.");
            Thread.Sleep(100);
        }
    }

    /// <summary>Repair the standalone Decky lifetime helper without requiring Gaming Mode.</summary>
    public void EnsureRuntimeHostInstalled()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Assets", "DeckyRuntime");
        string runtime = Path.Combine(ServicesDir, "PlayhubRuntime");
        DeckyFileUpgrade.RequireOrdinaryPath(runtime);
        if (!File.Exists(Path.Combine(source, "Playhub-DeckyHost.exe")))
            throw new IOException("Il runtime Decky di Playhub è mancante. Reinstalla Playhub per ripristinarlo.");
        string[] files = Directory.GetFiles(source).OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file) + "\0"));
            using var stream = File.OpenRead(file);
            var buffer = new byte[65536]; int length;
            while ((length = stream.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, length));
        }
        string version = Convert.ToHexString(hash.GetHashAndReset());
        string destination = Path.Combine(runtime, version);
        DeckyFileUpgrade.RequireOrdinaryPath(destination);
        Directory.CreateDirectory(destination);
        foreach (string file in files)
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            DeckyFileUpgrade.RequireOrdinaryPath(target);
            if (File.Exists(target) && File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(target))) continue;
            // Hash-named versions are immutable. A live host can keep its DLLs;
            // a future Decky start selects the newly completed version.
            File.Copy(file, target, overwrite: true);
        }
        WriteRuntimePointer(Path.Combine(runtime, "current"), version);
    }

    private static void WriteRuntimePointer(string pointer, string version)
    {
        DeckyFileUpgrade.RequireOrdinaryPath(pointer);
        if (File.Exists(pointer) && File.ReadAllText(pointer) == version) return;
        string temporary = Path.Combine(Path.GetDirectoryName(pointer)!, ".current-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(temporary, version, new UTF8Encoding(false)); File.Move(temporary, pointer, overwrite: true); }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }

    private void KillLoaders()
    {
        Playhub.DeckyHost.DeckyProcessTree.StopInstallation(ServicesDir);
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
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        return client;
    }
}
