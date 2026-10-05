using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Playhub.Services;

internal interface ISteamLibraryRuntime
{
    (bool Running, bool BigPicture) ReadState();
    bool HasActiveGame();
    string SteamExecutable();
    bool FileExists(string path);
    void Launch(string executable, string arguments, bool hidden);
    Task Delay(int milliseconds);
    void Warn(string message);
}

internal static class SteamLibraryEditSession
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Steam owns an in-memory shortcut list and rewrites it on exit. Never race
    // its writer or allow two import buttons to overwrite one another.
    internal static Task<T> RunAsync<T>(Func<Task<T>> edit) => RunAsync(edit, new SteamLibraryRuntime());

    internal static async Task<T> RunAsync<T>(Func<Task<T>> edit, ISteamLibraryRuntime runtime)
    {
        await Gate.WaitAsync();
        var shutdownRequested = false;
        var bigPicture = false;
        var executable = "";
        try
        {
            var state = runtime.ReadState();
            bigPicture = state.BigPicture;
            // Path lookup failures must release the import gate, too.
            executable = runtime.SteamExecutable();
            if (state.Running)
            {
                if (runtime.HasActiveGame())
                    throw new IOException("A game is running in Steam. Close the game before importing changes.");
                if (!runtime.FileExists(executable)) throw new FileNotFoundException("Steam executable not found.", executable);
                runtime.Launch(executable, "-shutdown", hidden: true);
                shutdownRequested = true;
                for (var i = 0; i < 120 && runtime.ReadState().Running; i++) await runtime.Delay(250);
                if (runtime.ReadState().Running)
                    throw new TimeoutException("Steam is still running. Close the active Steam dialog, then import again.");
            }
            return await edit();
        }
        finally
        {
            try
            {
                if (shutdownRequested && !runtime.ReadState().Running)
                {
                    if (!runtime.FileExists(executable)) throw new FileNotFoundException("Steam executable not found.", executable);
                    // The process can disappear just before its singleton lock
                    // is released. A launch in that interval silently exits.
                    await runtime.Delay(1200);
                    if (!runtime.ReadState().Running)
                    {
                        runtime.Launch(executable, bigPicture ? "-gamepadui" : "", hidden: false);
                        await runtime.Delay(1500);
                        if (!runtime.ReadState().Running)
                        {
                            runtime.Launch(executable, bigPicture ? "-gamepadui" : "", hidden: false);
                            await runtime.Delay(1500);
                            if (!runtime.ReadState().Running) runtime.Warn("Steam did not reopen after the library import. Start Steam from its shortcut.");
                        }
                    }
                }
            }
            catch (Exception error)
            {
                // Restart failure must preserve the successful library write,
                // or the original import exception when writing failed.
                try { runtime.Warn("Steam restart after library import failed: " + error.Message); } catch { }
            }
            finally { Gate.Release(); }
        }
    }

}

internal sealed class SteamLibraryRuntime : ISteamLibraryRuntime
{
    public (bool Running, bool BigPicture) ReadState()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            var bigPicture = false;
            foreach (var process in processes)
            {
                try { bigPicture |= process.MainWindowTitle.Contains("Big Picture", StringComparison.OrdinalIgnoreCase); }
                catch (InvalidOperationException) { }
            }
            // The Big Picture window belongs to steamwebhelper on current
            // clients; steam.exe can have neither a window nor foreground flag.
            var renderers = Process.GetProcessesByName("steamwebhelper");
            try
            {
                foreach (var renderer in renderers)
                {
                    try
                    {
                        var title = renderer.MainWindowTitle;
                        bigPicture |= title.Contains("Big Picture", StringComparison.OrdinalIgnoreCase);
                    }
                    catch (InvalidOperationException) { }
                }
            }
            finally { foreach (var renderer in renderers) renderer.Dispose(); }
            using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            bigPicture |= steam?.GetValue("BigPictureInForeground") is int foreground && foreground != 0;
            return (processes.Length > 0, bigPicture);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public bool HasActiveGame()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("RunningAppID") is { } running && Convert.ToInt64(running) != 0) return true;
        using var apps = steam?.OpenSubKey("Apps");
        if (apps is null) return false;
        foreach (var name in apps.GetSubKeyNames())
        {
            using var app = apps.OpenSubKey(name);
            if (app?.GetValue("Running") is { } value && Convert.ToInt64(value) != 0) return true;
        }
        return false;
    }

    public string SteamExecutable() => Path.Combine(UwpHookSteamManager.GetSteamFolder() ?? "", "steam.exe");
    public bool FileExists(string path) => File.Exists(path);
    public void Launch(string executable, string arguments, bool hidden) => ProcessService.StartDetached(executable, arguments, hidden: hidden);
    public Task Delay(int milliseconds) => Task.Delay(milliseconds);
    public void Warn(string message)
    {
        Trace.TraceWarning(message);
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "playhub-safety.log"), $"{DateTimeOffset.Now:O} [Steam import] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
