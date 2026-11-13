using System.Diagnostics;
using System.Runtime.InteropServices;
using GamingMode.Services;

// No SendInput, controller driver, foreground activation, mode endpoint or Steam.
// Native lifetimes and delivery are real; foreground is a private fixture model.
internal static class NativeReturnChecks
{
    private const uint Pad = 0x802A, Count = 0x802B, Quit = 0x802C;
    private static readonly WndProc Callback = WindowProcedure;
    private static int _received;
    internal static int WindowChild()
    {
        var wc = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Callback, Name = "PlayhubReturnFixture" };
        if (RegisterClassEx(ref wc) == 0) return 2;
        nint hwnd = CreateWindowEx(0, wc.Name, "Private test target", 0, 0, 0, 0, 0, -3, 0, 0, 0);
        if (hwnd == 0) return 3;
        Console.WriteLine(hwnd.ToInt64()); Console.Out.Flush();
        while (GetMessage(out var message, 0, 0, 0) > 0) DispatchMessage(ref message);
        DestroyWindow(hwnd);
        return 0;
    }
    private static nint WindowProcedure(nint hwnd, uint message, nint w, nint l)
    {
        if (message == Pad) return ++_received;
        if (message == Count) return _received;
        if (message == Quit) { PostQuitMessage(0); return 0; }
        return DefWindowProc(hwnd, message, w, l);
    }
    private static Process Child(string argument) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, argument)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
    private static long Send(nint window, uint message)
    {
        if (SendMessageTimeout(window, message, 0, 0, 2, 1000, out nuint value) == 0)
            throw new Exception("Private fixture message timed out");
        return (long)value;
    }
    private static bool OwnerCurrent(nint hwnd, Process retained, long expectedBirth, uint expectedPid)
    {
        return !retained.HasExited && retained.StartTime.ToFileTimeUtc() == expectedBirth && IsWindow(hwnd)
            && GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == expectedPid && pid == retained.Id;
    }
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native fixture requires Windows");
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        foreach (string mode in new[] { "Desktop", "Gaming" })
        {
            using var target = Child("--native-return-window");
            nint hwnd = nint.Parse(target.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()!);
            long birth = target.StartTime.ToFileTimeUtc();
            try
            {
                using var game = Child("--native-return-game");
                _ = game.Handle; // Retain the original native process handle through exit.
                var policy = new BigPictureReturnPolicy();
                policy.Observe(ReturnForeground.Game, true);
                Check(!policy.UsedCompanion, mode + ": game foreground alone is not evidence of companion use");
                policy.Observe(ReturnForeground.Companion, false);
                Check(!policy.UsedCompanion, mode + ": seeing the companion only during teardown is insufficient");
                policy.Observe(ReturnForeground.Companion, true);
                policy.Observe(ReturnForeground.Game, true);
                Check(policy.UsedCompanion, mode + ": companion use remains recorded after resuming the original game");
                Check(!policy.AllowsReturn(ReturnForeground.Companion, game.HasExited), mode + ": live original process cannot confirm return");
                Check(game.WaitForExit(5000) && game.ExitCode == 37, mode + ": exact isolated game process exit observed");
                bool returned = policy.AllowsReturn(ReturnForeground.Companion, game.HasExited);
                int version = returned ? 1 : 0;
                long expires = Environment.TickCount64 + 15000;
                bool Confirm(bool front, long ownerBirth, uint ownerPid, int request = 1, long? now = null) =>
                    BigPictureReturnPolicy.IsConfirmed(request, version, now ?? Environment.TickCount64, expires,
                        front, OwnerCurrent(hwnd, target, ownerBirth, ownerPid));
                bool Deliver(bool confirmed) { if (!confirmed) return false; Send(hwnd, Pad); return true; }
                Check(!Deliver(Confirm(false, birth, (uint)target.Id)) && Send(hwnd, Count) == 0,
                    mode + ": activation request without foreground confirmation delivers no pad event");
                Check(!Deliver(Confirm(true, birth + 1, (uint)target.Id)) && !Deliver(Confirm(true, birth, (uint)Environment.ProcessId))
                    && Send(hwnd, Count) == 0, mode + ": mismatched owner or birth delivers no event");
                Check(!Deliver(Confirm(true, birth, (uint)target.Id, request: 2))
                    && !Deliver(Confirm(true, birth, (uint)target.Id, now: expires)), mode + ": stale version and expired confirmation rejected");
                Check(Deliver(Confirm(true, birth, (uint)target.Id)) && Send(hwnd, Count) == 1,
                    mode + ": first intended semantic pad event reaches only native fixture after confirmed return");
                policy.Observe(ReturnForeground.Foreign, false);
                Check(!policy.AllowsReturn(ReturnForeground.Companion, true), mode + ": foreign application choice prevents return");
                Send(hwnd, Quit);
                Check(target.WaitForExit(5000) && !Confirm(true, birth, (uint)target.Id), mode + ": destroyed owner lifetime invalidates confirmation");
            }
            finally { if (!target.HasExited) { Send(hwnd, Quit); target.WaitForExit(5000); } }
        }
        Console.WriteLine($"Native paired return fixture: {checks} checks. Foreground modeled; only private WM_APP delivery, no physical pad or Steam validation.");
    }
    private delegate nint WndProc(nint h, uint m, nint w, nint l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    { public uint Size, Style; public WndProc Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Window; public uint Id; public nuint W; public nint L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClass wc);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message m, nint h, uint min, uint max);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message m);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint h,uint m,nint w,nint l,uint flags,uint timeout,out nuint result);
}
