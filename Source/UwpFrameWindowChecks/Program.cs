using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using GamingMode.Services;

internal static class Program
{
    private static readonly Native.WindowProcedure Procedure = WindowProcedure;
    private static int _checks;
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        _checks++;
        Console.WriteLine("PASS " + reason);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--lease-fixture" })) { Thread.Sleep(400); return 31; }
        if (args.Length is 3 or 4 && args[0] == "--child")
        {
            var child = Create(args[2], (nint)long.Parse(args[1]), true);
            if (args.Length == 4)
                for (int index = 1; index < int.Parse(args[3]); index++)
                    if (Native.CreateWindowEx(0, args[2], "", 0x40000000u, 0, 0, 10, 10, (nint)long.Parse(args[1]), 0, Native.GetModuleHandle(null), 0) == 0)
                        throw new Exception("Native child batch failed");
            Console.WriteLine(child.ToInt64()); Console.Out.Flush();
            Pump(); return 0;
        }
        using var frame = new Frame();
        Check(frame.Exists, "Real native untitled frame exists offscreen");
        Check(!OverlayWindowTools.Enumerate().Any(window => window.Handle == frame.Handle), "Empty service frame without game child stays excluded");
        using var ordinary = new Child(frame.Handle, "PlayhubFixtureServiceChild");
        Check(!Read(pid => pid == ordinary.Pid).Any(window => window.Handle == frame.Handle), "Unrelated cross-process child does not turn an empty frame into a game");
        using var game = new Child(frame.Handle, "Windows.UI.Core.CoreWindow");
        Check(OverlayWindowTools.WindowProcessId(frame.Handle) == game.Pid, "Actual cross-process CoreWindow has priority over first unrelated child");
        Check(!Read(_ => false).Any(window => window.Handle == frame.Handle), "Unverified package identity never exposes an empty frame");
        var visible = Read(pid => pid == game.Pid).Single(window => window.Handle == frame.Handle);
        Check(visible.ProcessId == game.Pid && visible.Title == game.ProcessName && !string.IsNullOrWhiteSpace(visible.Title), "Untitled packaged CoreWindow maps to real child PID and process display fallback");
        var fallback = ReadFallback(pid => pid == game.Pid).Single(window => window.Handle == frame.Handle);
        Check(fallback.ProcessId == game.Pid && fallback.Title == game.ProcessName,
            "FindWindowEx discovers the actual cross-process frame and CoreWindow when desktop enumeration is absent");
        Check(Read(pid => pid == game.Pid).Count(window => window.Handle == frame.Handle) == 1,
            "Desktop enumeration plus FindWindowEx produces one card per frame");
        using (var crowded = new Child(frame.Handle, "PlayhubFixtureCrowdedChild", 128))
            Check(!ReadFallback(_ => true).Any(window => window.Handle == frame.Handle), "An excessive native child tree is refused at the bounded discovery limit");
        var stale = DashboardApi.CloseWindowAsync(frame.Handle.ToString(), (int)ordinary.Pid).GetAwaiter().GetResult();
        Check(!stale.Ok && stale.Reason == "windowChanged" && frame.Exists, "Wrong unrelated child PID cannot close game frame");
        using (var secondGame = new Child(frame.Handle, "Windows.UI.Core.CoreWindow"))
        {
            Check(!Read(_ => true).Any(window => window.Handle == frame.Handle), "Two different CoreWindow processes make the empty frame ambiguous and excluded");
            Check(!ReadFallback(_ => true).Any(window => window.Handle == frame.Handle), "FindWindowEx-only path also refuses two different hosted CoreWindow processes");
        }
        var closed = DashboardApi.CloseWindowAsync(frame.Handle.ToString(), (int)game.Pid).GetAwaiter().GetResult();
        Check(closed.Ok && closed.Closed && !closed.Pending && !frame.Exists, "Close accepts actual hosted game PID and confirms raw frame destruction");
        using var leaseChild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--lease-fixture") { UseShellExecute = false, CreateNoWindow = true })!;
        var leaseType = typeof(OverlayWindowTools).GetNestedType("ProcessLease", BindingFlags.NonPublic)!;
        using var lease = (IDisposable)leaseType.GetMethod("TryOpen", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { leaseChild.Id })!;
        bool Property(string name) => (bool)leaseType.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        var created = (long)leaseType.GetProperty("CreatedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        Check(Property("Alive") && created == leaseChild.StartTime.ToUniversalTime().ToFileTimeUtc(), "Native process lease retains the exact fixture birth identity");
        leaseChild.WaitForExit();
        Check(leaseChild.ExitCode == 31 && !Property("Alive") && Property("Exited"), "Retained lease observes original process exit without attaching to a replacement PID");
        Console.WriteLine($"PASS {_checks} native cross-process frame checks. Package-identity reader is isolated; no installed frame/game/input was controlled.");
        return 0;
    }

    private static IReadOnlyList<OverlayWindowInfo> Read(Func<uint, bool> identity)
        => (IReadOnlyList<OverlayWindowInfo>)typeof(OverlayWindowTools)
            .GetMethod("Enumerate", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Func<uint, bool>) }, null)!
            .Invoke(null, new object[] { identity })!;

    private static IReadOnlyList<OverlayWindowInfo> ReadFallback(Func<uint, bool> identity)
        => (IReadOnlyList<OverlayWindowInfo>)typeof(OverlayWindowTools)
            .GetMethod("Enumerate", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Func<uint, bool>), typeof(bool) }, null)!
            .Invoke(null, new object[] { identity, false })!;

    private static nint Create(string className, nint parent, bool child)
    {
        var klass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = Procedure,
            Instance = Native.GetModuleHandle(null), Name = className };
        if (Native.RegisterClassEx(ref klass) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        nint window = Native.CreateWindowEx(child ? 0u : 0x40000u, className, "", child ? 0x40000000u : 0x00CF0000u,
            child ? 0 : -30000, child ? 0 : -30000, 360, 240, parent, 0, klass.Instance, 0);
        if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Native.ShowWindow(window, 4);
        return window;
    }
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == 2) { Native.PostQuitMessage(0); return 0; }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }
    private static void Pump() { while (Native.GetMessage(out var message, 0, 0, 0) > 0) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); } }

    private sealed class Frame : IDisposable
    {
        private readonly Thread _thread;
        private readonly TaskCompletionSource<nint> _ready = new();
        public nint Handle => _ready.Task.GetAwaiter().GetResult();
        public bool Exists => OverlayWindowTools.WindowOwnerProcessId(Handle) == Environment.ProcessId;
        public Frame()
        {
            _thread = new Thread(() => { try { _ready.SetResult(Create("ApplicationFrameWindow", 0, false)); Pump(); } catch (Exception error) { _ready.TrySetException(error); } }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA); _thread.Start(); _ = Handle;
        }
        public void Dispose() { if (Exists) Native.PostMessage(Handle, 0x10, 0, 0); if (!_thread.Join(2000)) throw new Exception("Frame fixture did not stop"); }
    }
    private sealed class Child : IDisposable
    {
        private readonly Process _process;
        private readonly nint _handle;
        public uint Pid => (uint)_process.Id;
        public string ProcessName => _process.ProcessName;
        public Child(nint parent, string className, int count = 1)
        {
            _process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--child {parent.ToInt64()} {className} {count}")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var line = _process.StandardOutput.ReadLineAsync();
            if (!line.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Child fixture did not create its native window");
            _handle = (nint)long.Parse(line.Result ?? throw new Exception("Child fixture ended before creation"));
        }
        public void Dispose()
        {
            Native.PostMessage(_handle, 0x10, 0, 0);
            if (!_process.WaitForExit(2000)) throw new Exception("Child fixture did not stop");
            _process.Dispose();
        }
    }

    private static class Native
    {
        internal delegate nint WindowProcedure(nint h, uint m, nint w, nint l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
        { public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu, Name; public nint SmallIcon; }
        [StructLayout(LayoutKind.Sequential)] internal struct Message
        { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass klass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint parameter);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint w, nint l);
        [DllImport("user32.dll")] internal static extern nint DefWindowProc(nint window, uint message, nint w, nint l);
        [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    }
}
