using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GamingMode.Services;

internal static class Program
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    private static void Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "PlayhubFocusChecks", Guid.NewGuid().ToString("N"));
        var logger = new FileLogger(Path.Combine(folder, "focus.log"));
        var ctor = typeof(GamingWindowFocusService).GetConstructor(Private, null,
            new[] { typeof(FileLogger), typeof(Func<int, bool>) }, null)!;
        using var focus = (GamingWindowFocusService)ctor.Invoke(new object[] { logger, (Func<int, bool>)(pid => pid == Environment.ProcessId) });
        var reconcile = typeof(GamingWindowFocusService).GetMethod("ApplyToCandidateWindows", Private)!;
        var apply = typeof(GamingWindowFocusService).GetMethod("ApplyToWindow", Private)!;
        var tracked = (IDictionary)typeof(GamingWindowFocusService).GetField("_gameWindows", Private)!.GetValue(focus)!;
        var resized = (IDictionary)typeof(GamingWindowFocusService).GetField("_appliedWindows", Private)!.GetValue(focus)!;
        typeof(GamingWindowFocusService).GetField("_initialScan", Private)!.SetValue(focus, true);
        typeof(GamingWindowFocusService).GetField("_applyBorderlessFullscreen", Private)!.SetValue(focus, false);
        var sample = new Window { Title = "Playhub focus fixture", Width = 320, Height = 220,
            Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        sample.Show();
        nint window = new WindowInteropHelper(sample).Handle;
        // Calling the fullscreen operation for an unregistered ordinary window
        // must leave it untouched, even though it meets size/style criteria.
        apply.Invoke(focus, new object[] { window });
        Check(resized.Count == 0, "Untracked application is never resized");
        reconcile.Invoke(focus, null);
        Check(tracked.Contains(window), "Game window is tracked with borderless disabled");
        Check(resized.Count == 0, "Disabled borderless preserves native window geometry");
        sample.Hide();
        reconcile.Invoke(focus, null);
        Check(!tracked.Contains(window), "Hidden game frame is removed from the active session");
        Check(focus.SteamFocusRecoveryVersion == 0, "Background closure does not request Steam foreground");
        sample.Close();
        using var observerOnly = new GamingWindowFocusService(logger);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        observerOnly.Start(false);
        Thread.Sleep(3000);
        process.Refresh();
        Console.WriteLine($"Idle focus observer: {(process.TotalProcessorTime - cpuBefore).TotalMilliseconds:F1} ms CPU / 3000 ms wall time (includes initialization).");
        cpuBefore = process.TotalProcessorTime;
        Thread.Sleep(3000);
        process.Refresh();
        Console.WriteLine($"Idle focus observer steady state: {(process.TotalProcessorTime - cpuBefore).TotalMilliseconds:F1} ms CPU / 3000 ms wall time.");
        var stop = Stopwatch.StartNew();
        observerOnly.Stop();
        Check(!observerOnly.Running && stop.ElapsedMilliseconds < 1000, "Event worker cancels and releases hooks promptly");
        using (var closable = new CloseFixture(false))
        {
            var stale = DashboardApi.CloseWindowAsync(closable.Handle.ToString(), Environment.ProcessId + 1).GetAwaiter().GetResult();
            Check(!stale.Ok && stale.Reason == "windowChanged", "Stale process identity cannot close a different window");
            var closed = DashboardApi.CloseWindowAsync(closable.Handle.ToString(), Environment.ProcessId).GetAwaiter().GetResult();
            Check(closed.Ok && closed.Closed && !closed.Pending, "Graceful close waits for actual HWND disappearance");
            var repeated = DashboardApi.CloseWindowAsync(closable.Handle.ToString()).GetAwaiter().GetResult();
            Check(repeated.Closed, "Repeated close of a gone window is idempotent");
        }
        using (var rejecting = new CloseFixture(true))
        {
            var pending = DashboardApi.CloseWindowAsync(rejecting.Handle.ToString(), Environment.ProcessId).GetAwaiter().GetResult();
            Check(pending.Ok && !pending.Closed && pending.Pending, "Application confirmation or refused close remains pending");
        }
        var invalid = DashboardApi.CloseWindowAsync("not-a-handle").GetAwaiter().GetResult();
        Check(!invalid.Ok && invalid.Reason == "invalidWindow", "Malformed close handle is rejected");
        Console.WriteLine("Native window tests completed; no Steam/game lifecycle action was performed.");
    }
    private sealed class CloseFixture : IDisposable
    {
        private readonly Thread _thread;
        private readonly TaskCompletionSource<nint> _ready = new();
        private Window? _window;
        private bool _reject;
        public nint Handle => _ready.Task.GetAwaiter().GetResult();
        public CloseFixture(bool reject)
        {
            _reject = reject;
            _thread = new Thread(() =>
            {
                _window = new Window { Title = "Playhub close fixture", Width = 240, Height = 180,
                    Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
                _window.Closing += (_, e) => e.Cancel = _reject;
                _window.Show();
                _ready.SetResult(new WindowInteropHelper(_window).Handle);
                Dispatcher.Run();
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        public void Dispose()
        {
            _window!.Dispatcher.Invoke(() => { _reject = false; _window.Close(); });
            _window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            _thread.Join(1000);
        }
    }
}
