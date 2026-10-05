using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GamingMode.Models;
using GamingMode.Services;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int cx, int cy, uint flags);
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }

    [STAThread]
    static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PlayhubGamingModeChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var logger = new FileLogger(Path.Combine(directory, "agent.log"));
        using var focus = new GamingWindowFocusService(logger);
        var prioritize = typeof(GamingWindowFocusService).GetMethod("PrioritizeLaunchCurtainWindow", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Simulate the precise race: visible when enumerated, hidden by its owner
        // before the queued priority operation runs. No Steam window is touched.
        var cover = new Window { Left = -30000, Top = -30000, Width = 1, Height = 1,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        cover.Show();
        nint handle = new WindowInteropHelper(cover).Handle;
        cover.Hide();
        Assert(!IsWindowVisible(handle), "cover initially hidden");
        SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, 595u);
        Assert(IsWindowVisible(handle), "old priority flags reproduce hidden cover resurrection");
        cover.Show(); cover.Hide();
        for (int i = 0; i < 100; i++) prioritize.Invoke(focus, new object[] { handle });
        Assert(!IsWindowVisible(handle), "fixed priority preserves hidden cover across repeated calls");
        cover.Show();
        prioritize.Invoke(focus, new object[] { handle });
        Assert(IsWindowVisible(handle), "active game cover remains visible");
        cover.Close();

        var paths = (AppPaths)Activator.CreateInstance(typeof(AppPaths), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { directory }, culture: null)!;
        var store = new JsonStore(paths, logger);
        store.SaveConfig(new ModeConfig());
        using var cursor = new CursorAutoHideService(logger);
        using var volume = new SystemVolumeKeyService(logger);
        var manager = new ModeManager(paths, store, new ProcessTools(logger), new ShellTools(logger), cursor, focus, volume, logger);
        foreach (ModeKind mode in new[] { ModeKind.Desktop, ModeKind.Gaming })
        {
            if (mode == ModeKind.Desktop && GetShellWindow() == 0)
                throw new InvalidOperationException("Desktop fixture requires an existing shell; refusing to invoke recovery on this machine.");
            store.SaveState(new ModeState { CurrentMode = mode, LastAppliedAt = DateTimeOffset.UtcNow, LastAction = "fixture" });
            string before = File.ReadAllText(paths.StatePath);
            for (int i = 0; i < 3; i++)
                Assert(manager.SwitchToModeAsync(mode).GetAwaiter().GetResult().Ok, $"duplicate {mode} request {i + 1} succeeds without transition");
            Assert(File.ReadAllText(paths.StatePath) == before, $"duplicate {mode} leaves state unchanged");
        }
        Assert(!File.Exists(paths.LogPath) || !File.ReadAllText(paths.LogPath).Contains("splash", StringComparison.OrdinalIgnoreCase), "duplicate requests never create a splash");
        Assert(!manager.ConsumeDesktopSwitchRequest(), "duplicate requests do not ask Steam to exit Big Picture");
        Console.WriteLine("Gaming Mode regression checks complete. Fixture: " + directory);
    }
}
