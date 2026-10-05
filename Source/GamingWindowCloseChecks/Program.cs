using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GamingMode.Services;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        _checks++;
        Console.WriteLine("PASS " + reason);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--exit-fixture" })) return;
        using (var hidden = new Fixture(reject: true))
        {
            hidden.Hide();
            Check(!OverlayWindowTools.HasVisibleWindow(hidden.Handle) && hidden.Exists, "Hidden HWND still belongs to live fixture");
            var changed = Close(hidden, Environment.ProcessId + 1);
            Check(!changed.Ok && !changed.Closed && changed.Reason == "windowChanged" && hidden.CloseRequests == 0, "Hidden window still enforces expected owner before posting close");
            var result = Close(hidden);
            Check(result.Ok && !result.Closed && result.Pending && hidden.Exists && hidden.CloseRequests == 1, "Hidden refusing window receives one close request and never reports alreadyClosed");
        }
        using (var cloaked = new Fixture(reject: true))
        {
            cloaked.Cloak();
            Check(!OverlayWindowTools.HasVisibleWindow(cloaked.Handle) && cloaked.Exists, "Real DWM-cloaked HWND remains alive");
            var result = Close(cloaked);
            Check(result.Ok && !result.Closed && result.Pending && cloaked.Exists, "Cloaked refusing window never reports closure");
        }
        using (var hiding = new Fixture(reject: true, hideOnClose: true))
        {
            var result = Close(hiding);
            Check(result.Pending && !result.Closed && hiding.Exists && !OverlayWindowTools.HasVisibleWindow(hiding.Handle), "Application hiding after WM_CLOSE is still pending");
        }
        using (var blocked = new Fixture(reject: true))
        {
            blocked.BlockDispatcher();
            var result = Close(blocked);
            Check(result.Pending && !result.Closed && blocked.Exists, "Nonresponsive owner does not turn posted WM_CLOSE into closure success");
        }
        using (var closable = new Fixture(reject: false))
        {
            closable.Hide();
            var result = Close(closable);
            Check(result.Ok && result.Closed && !result.Pending && !closable.Exists, "Hidden responsive window really closes");
            var ambiguous = Close(closable);
            Check(ambiguous.Pending && !ambiguous.Closed, "Missing old HWND with surviving expected process cannot claim alreadyClosed");
            var repeated = DashboardApi.CloseWindowAsync(closable.Handle.ToString()).GetAwaiter().GetResult();
            Check(repeated.Closed && repeated.Reason == "alreadyClosed", "Gone HWND without surviving expected ownership is idempotent");
            using var exited = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--exit-fixture")
                { UseShellExecute = false, CreateNoWindow = true })!;
            exited.WaitForExit();
            var completed = Close(closable, exited.Id);
            Check(completed.Closed && !completed.Pending && completed.Reason == "alreadyClosed", "Gone HWND and exited expected process confirms completed closure");
        }
        Check(!DashboardApi.CloseWindowAsync("0", Environment.ProcessId).GetAwaiter().GetResult().Ok, "Zero handle rejected");
        Console.WriteLine($"PASS {_checks} real isolated window close checks; no installed window, game or process was controlled.");
    }

    private static DashboardApi.WindowCloseResult Close(Fixture fixture, int? expected = null)
        => DashboardApi.CloseWindowAsync(fixture.Handle.ToString(), expected ?? Environment.ProcessId).GetAwaiter().GetResult();

    private sealed class Fixture : IDisposable
    {
        private readonly Thread _thread;
        private readonly TaskCompletionSource<nint> _ready = new();
        private readonly ManualResetEventSlim _release = new(false);
        private Window? _window;
        private bool _reject;
        private bool _closed;
        private int _closeRequests;
        public nint Handle => _ready.Task.GetAwaiter().GetResult();
        public bool Exists => OverlayWindowTools.WindowOwnerProcessId(Handle) == Environment.ProcessId;
        public int CloseRequests => Volatile.Read(ref _closeRequests);

        public Fixture(bool reject, bool hideOnClose = false)
        {
            _reject = reject;
            _thread = new Thread(() =>
            {
                try
                {
                    _window = new Window { Title = "Playhub close regression fixture", Width = 240, Height = 180,
                        Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
                    _window.Closing += (_, e) => { Interlocked.Increment(ref _closeRequests); e.Cancel = _reject; if (_reject && hideOnClose) _window.Hide(); };
                    _window.Closed += (_, _) => _closed = true;
                    _window.Show();
                    _ready.SetResult(new WindowInteropHelper(_window).Handle);
                    Dispatcher.Run();
                }
                catch (Exception error) { _ready.TrySetException(error); }
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ = Handle;
        }

        public void Hide() => _window!.Dispatcher.Invoke(_window.Hide);
        public void Cloak()
        {
            int cloak = 1;
            int result = DwmSetWindowAttribute(Handle, 13, ref cloak, sizeof(int));
            if (result != 0) Marshal.ThrowExceptionForHR(result);
        }
        public void BlockDispatcher()
        {
            using var blocked = new ManualResetEventSlim(false);
            _window!.Dispatcher.BeginInvoke(() => { blocked.Set(); _release.Wait(TimeSpan.FromSeconds(10)); });
            if (!blocked.Wait(TimeSpan.FromSeconds(2))) throw new Exception("Fixture did not block its dispatcher");
        }
        public void Dispose()
        {
            _release.Set();
            _window!.Dispatcher.Invoke(() => { _reject = false; if (!_closed) _window.Close(); });
            _window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            if (!_thread.Join(2000)) throw new Exception("Fixture dispatcher did not stop");
            _release.Dispose();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
