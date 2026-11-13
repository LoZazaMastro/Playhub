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
    private static void Main(string[] args)
    {
        if (args is ["--process-lease-fixture"]) { Thread.Sleep(1500); return; }
        if (args is ["--replacement-proof"]) { VerifyReplacementProof(); return; }
        if (args.Length > 0 && args[0] == "--live-return")
        {
            if (args.Length != 2 || !uint.TryParse(args[1], out uint appId) || appId == 0)
            {
                Console.Error.WriteLine("Usage: GamingWindowFocusChecks --live-return <Steam app ID>");
                Environment.ExitCode = 2;
                return;
            }
            Environment.ExitCode = VerifyLiveReturn(appId);
            return;
        }
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
        VerifyReplacementProof();
        VerifyExactProcessExit();
        Console.WriteLine("Native window tests completed; no Steam/game lifecycle action was performed.");
    }

    private static void VerifyReplacementProof()
    {
        var returnType = typeof(GamingWindowFocusService).Assembly.GetType("GamingMode.Services.BigPictureGameReturn", true)!;
        var select = returnType.GetMethod("ReplacementAppId", BindingFlags.Static | BindingFlags.NonPublic)!;
        const uint appId = 2946503967;
        var source = new QamWindowIdentity(123, 50, 10, 50, 10);
        var replacement = source with { Handle = 124 };
        uint For(QamWindowIdentity candidate) => (uint)select.Invoke(null, [appId, source, candidate])!;
        Check(For(replacement) == appId, "A replacement native window preserves its existing Steam proof for the same process birth");
        Check(For(replacement with { GamePid = 51, OwnerPid = 51 }) == 0
            && For(replacement with { GameBirth = 11, OwnerBirth = 11 }) == 0,
            "A different process or reused PID cannot inherit another Steam proof");
        Check(For(replacement with { OwnerPid = 51 }) == 0 && For(replacement with { OwnerBirth = 11 }) == 0
            && For(replacement with { Handle = 0 }) == 0 && For(source) == 0,
            "Replacement proof requires a different exact native window owned by the verified game process");
    }

    private static int VerifyLiveReturn(uint appId)
    {
        string logPath = Path.Combine(Path.GetTempPath(), "PlayhubFocusChecks", Guid.NewGuid().ToString("N"), "live-return.log");
        using var focus = new GamingWindowFocusService(new FileLogger(logPath));
        using var canceled = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, eventArgs) => { eventArgs.Cancel = true; canceled.Cancel(); };
        Console.CancelKeyPress += stop;
        try
        {
            if (CaptureBigPicture() is not { } steam)
                return LiveFailure("No unique Big Picture window from the verified Steam installation.");
            Console.WriteLine($"TARGET hwnd=0x{steam.Handle:X} pid={steam.OwnerPid} birth={steam.OwnerBirth}; log={logPath}");
            Console.WriteLine($"Bring the already-running game {appId} to the foreground within 20 seconds. Wait for ARMED before closing it normally.");
            int version = focus.SteamFocusRecoveryVersion;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && !canceled.IsCancellationRequested)
            {
                nint window = OverlayWindowTools.GetForegroundWindowHandle();
                int pid = OverlayWindowTools.WindowProcessId(window);
                if (pid > 0 && pid != steam.OwnerPid)
                {
                    using var game = OpenLiveProcess(pid);
                    if (game is not null && focus.ArmBigPictureReturn(appId))
                    {
                        if (game.HasExited || OverlayWindowTools.GetForegroundWindowHandle() != window
                            || OverlayWindowTools.WindowProcessId(window) != pid || CaptureBigPicture() != steam)
                            return LiveFailure("Game or Big Picture identity changed while arming.");
                        Console.WriteLine($"ARMED appId={appId} hwnd=0x{window:X} pid={pid} birth={game.StartTime.ToFileTimeUtc()} recoveryVersion={version}");
                        return WaitForLiveReturn(focus, game, steam, version, canceled.Token);
                    }
                }
                canceled.Token.WaitHandle.WaitOne(200);
            }
            return LiveFailure(canceled.IsCancellationRequested ? "Canceled." : "Timed out waiting for the exact foreground game.");
        }
        finally { Console.CancelKeyPress -= stop; }
    }

    private static int WaitForLiveReturn(GamingWindowFocusService focus, Process game, QamWindowIdentity steam,
        int version, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan? exitedAt = null;
        while (elapsed.Elapsed < TimeSpan.FromSeconds(90) && !token.IsCancellationRequested)
        {
            bool exited = game.HasExited;
            if (exited && exitedAt is null)
            {
                exitedAt = elapsed.Elapsed;
                Console.WriteLine($"EXIT pid={game.Id} afterMs={exitedAt.Value.TotalMilliseconds:F0}");
            }
            int currentVersion = focus.SteamFocusRecoveryVersion;
            if (currentVersion != version)
            {
                nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
                Console.WriteLine($"RETURN recoveryVersion={currentVersion} foreground=0x{foreground:X} pid={OverlayWindowTools.WindowOwnerProcessId(foreground)}");
                if (!exited || currentVersion != version + 1 || foreground != steam.Handle || CaptureBigPicture() != steam)
                    return LiveFailure("Recovery did not resolve to the exact original Big Picture window after game exit.");
                Console.WriteLine("PASS exact Big Picture foreground. Use the controller to verify navigation without a mouse click.");
                return 0;
            }
            if (exitedAt is { } exit && elapsed.Elapsed - exit > TimeSpan.FromSeconds(5))
                return LiveFailure("Game exited; the recovery service did not confirm exact Big Picture foreground within 5 seconds.");
            token.WaitHandle.WaitOne(50);
        }
        return LiveFailure(token.IsCancellationRequested ? "Canceled." : "Timed out waiting for the game to close.");
    }

    private static QamWindowIdentity? CaptureBigPicture()
    {
        var adapter = typeof(GamingWindowFocusService).Assembly.GetType("GamingMode.Services.WindowsQamFocusAdapter", true)!;
        var capture = adapter.GetMethod("CaptureSteam", BindingFlags.Static | BindingFlags.NonPublic, [typeof(string)])!;
        return capture.Invoke(null, [null]) is QamWindowIdentity identity ? identity : null;
    }

    private static Process? OpenLiveProcess(int pid)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            _ = process.SafeHandle; // Keep this exact process identity across PID reuse.
            return process;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process?.Dispose();
            return null;
        }
    }

    private static int LiveFailure(string reason)
    {
        Console.Error.WriteLine($"FAIL {reason}");
        return 1;
    }

    private static void VerifyExactProcessExit()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!, "--process-lease-fixture") { UseShellExecute = false };
        using var child = Process.Start(start)!;
        Type leaseType = typeof(OverlayWindowTools).GetNestedType("ProcessLease", BindingFlags.NonPublic)!;
        using var lease = (IDisposable)leaseType.GetMethod("TryOpen", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [child.Id])!;
        var waitMethod = leaseType.GetMethod("WaitForExitAsync", Private)!;
        using var cancellation = new CancellationTokenSource();
        var cancelledWait = (Task)waitMethod.Invoke(lease, [cancellation.Token])!;
        Check(!cancelledWait.IsCompleted, "A living process does not trigger the exact exit notification");
        cancellation.Cancel();
        try { cancelledWait.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        Check(cancelledWait.IsCanceled && !child.HasExited, "Cancelling recovery does not end the observed game process");
        var completedWait = (Task)waitMethod.Invoke(lease, [CancellationToken.None])!;
        Check(completedWait.Wait(TimeSpan.FromSeconds(8)) && child.HasExited,
            "Exact Win32 process signal completes only when the tracked game exits");
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
