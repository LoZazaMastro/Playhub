using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace GamingMode.Services;

internal sealed class WindowsQamFocusAdapter : IQamFocusWindows, IDisposable
{
    private readonly GamingWindowFocusService _games;
    private readonly string? _configuredSteamPath;
    private readonly Thread _thread;
    private readonly WinEventCallback _callback;
    private Dispatcher? _dispatcher;
    private int _disposed;
    public event Action? Changed;
    public WindowsQamFocusAdapter(GamingWindowFocusService games, string? configuredSteamPath)
    {
        _games = games;
        _configuredSteamPath = configuredSteamPath;
        _callback = (_, kind, hwnd, objectId, childId, _, _) =>
        {
            if (hwnd != 0 && (kind == 3 || (objectId == 0 && childId == 0))) Changed?.Invoke();
        };
        _thread = new Thread(Observe) { IsBackground = true, Name = "Playhub QAM focus events" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }
    public QamWindowIdentity? CaptureGame(uint appId)
    {
        if (appId == 0 || !_games.TryGetQamSource(out nint hwnd, out uint gamePid)) return null;
        // A verified COM-activated UWP session has its own window lifecycle.
        // Keep the main-window handoff limited to ordinary tracked sessions.
        if (!ControlledQamFocusService.AllowsAutomaticGame(
            OverlaySteamArtworkResolver.IsTrackedAppProcess((int)gamePid, appId),
            UwpSteamSessionAssociation.Find(gamePid, appId) is not null)) return null;
        return Capture(hwnd, gamePid);
    }
    public QamWindowIdentity? CaptureSteam() => CaptureSteam(_configuredSteamPath);

    internal static QamWindowIdentity? CaptureSteam(string? configuredSteamPath)
    {
        if (SteamInstallationPath.FindRoot(configuredSteamPath) is not { } steamRoot) return null;
        // Verified Big Picture uses SDL_app (localized window title). CEF
        // popup/overlay browsers are not the main compositor, even if larger.
        nint[] main = OverlayWindowTools.FindSteamWindows().Where(hwnd =>
        {
            StringBuilder name = new(128);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "SDL_app" || GetWindow(hwnd, 4) != 0
                || (GetWindowLongPtr(hwnd, -20).ToInt64() & (0x80 | 0x08000000)) != 0) return false;
            return BelongsToSteam(hwnd, steamRoot);
        }).ToArray();
        // Two plausible main windows are not a license to choose one by area.
        return main.Length == 1 ? Capture(main[0], 0) : null;
    }

    internal static bool BelongsToSteam(nint window, string steamRoot)
    {
        GetWindowThreadProcessId(window, out uint pid);
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            string path = process.MainModule?.FileName ?? "";
            string executable = Path.GetFileName(path);
            return Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(steamRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) && (executable.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)
                || executable.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
    internal static QamWindowIdentity? Capture(nint hwnd, uint gamePid)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        long birth = Birth(pid), gameBirth = gamePid == 0 ? 0 : Birth(gamePid);
        return birth == 0 || (gamePid != 0 && gameBirth == 0) ? null : new(hwnd, pid, birth, gamePid, gameBirth);
    }
    public bool IsValid(QamWindowIdentity window)
    {
        if (!IsWindowOwnerCurrent(window)) return false;
        if (window.GamePid == 0) return true;
        return Birth(window.GamePid) == window.GameBirth
            && _games.TryGetQamGameProcess(window.Handle, out uint gamePid) && gamePid == window.GamePid;
    }

    internal static bool IsWindowOwnerCurrent(QamWindowIdentity window) =>
        IsWindow(window.Handle) && IsWindowVisible(window.Handle)
        && GetWindowThreadProcessId(window.Handle, out uint pid) != 0 && pid == window.OwnerPid && Birth(pid) == window.OwnerBirth;
    public bool IsSteamForeground(QamWindowIdentity steam)
    {
        if (!IsValid(steam)) return false;
        nint foreground = GetForegroundWindow();
        // QAM browser views can be owned popup windows. Require the same exact
        // owner chain; an unrelated Steam window is an application selection.
        for (int depth = 0; foreground != 0 && depth < 8; depth++, foreground = GetWindow(foreground, 4))
            if (foreground == steam.Handle) return true;
        return false;
    }
    public bool IsSourceForeground(QamWindowIdentity source) => IsValid(source) && GetForegroundWindow() == source.Handle;
    public bool IsTopmost(QamWindowIdentity window) => (GetWindowLongPtr(window.Handle, -20).ToInt64() & 8) != 0;
    public bool SetTopmost(QamWindowIdentity window, bool topmost) => IsValid(window)
        && SetWindowPos(window.Handle, new nint(topmost ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // no move/size/activation
    public bool Activate(QamWindowIdentity window)
    {
        if (!IsValid(window)) return false;
        return ActivateWindow(window.Handle, () => IsValid(window));
    }

    internal static bool ActivateWindow(nint window, Func<bool> stillAllowed)
    {
        PeekMessage(out _, 0, 0, 0, 0);
        uint current = GetCurrentThreadId();
        uint target = GetWindowThreadProcessId(window, out _);
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool targetAttached = target != current && target != 0 && AttachThreadInput(current, target, true);
        bool foregroundAttached = foreground != current && foreground != target && foreground != 0 && AttachThreadInput(current, foreground, true);
        try
        {
            if (!stillAllowed()) return false;
            if (IsIconic(window)) ShowWindow(window, 9);
            BringWindowToTop(window);
            SetForegroundWindow(window);
            if (GetForegroundWindow() != window && stillAllowed()) SwitchToThisWindow(window, true);
            // No synthetic keyboard input and no success based on an activation request.
            return GetForegroundWindow() == window;
        }
        finally
        {
            if (foregroundAttached) AttachThreadInput(current, foreground, false);
            if (targetAttached) AttachThreadInput(current, target, false);
        }
    }
    private static long Birth(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid);
        if (process == 0) return 0;
        try { return GetExitCodeProcess(process, out uint exit) && exit == 259 && GetProcessTimes(process, out long birth, out _, out _, out _) ? birth : 0; }
        finally { CloseHandle(process); }
    }
    private void Observe()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        if (Volatile.Read(ref _disposed) != 0) return;
        nint foreground = SetWinEventHook(3, 3, 0, _callback, 0, 0, 2);
        nint destroyed = SetWinEventHook(0x8001, 0x8001, 0, _callback, 0, 0, 2);
        try { Dispatcher.Run(); }
        finally { if (foreground != 0) UnhookWinEvent(foreground); if (destroyed != 0) UnhookWinEvent(destroyed); }
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if (Thread.CurrentThread != _thread) _thread.Join(500);
    }
    private delegate void WinEventCallback(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint relation);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(nint hwnd, bool altTab);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
