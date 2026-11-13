using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GamingMode.Models;

namespace GamingMode.Services;

// Owns only a startup curtain. It never activates, restores or minimizes Steam.
internal sealed class SteamThemeStartupCover : IDisposable
{
    private sealed record Identity(nint Window, int Pid, long Birth);
    private readonly SplashScreenService _splash;
    private readonly FileLogger _logger;
    private readonly string? _steamRoot;
    private readonly Identity? _origin;
    private readonly Identity? _desktopShell;
    private readonly SteamThemeForegroundTransition _foregroundTransition;
    private readonly nint _originHandle;
    private readonly object _sync = new();
    private readonly SteamThemeStartupPolicy _policy;
    private readonly CancellationTokenSource _cancel = new();
    private Identity? _client, _main;
    private bool _contextChanged, _foregroundRejectionLogged;
    private int _foregroundLeft;
    private int _disposed;
    private readonly NativeRect _display;
    private readonly nint _monitor;
    private readonly Dictionary<int, (long Birth, string Path)> _trusted = new();
    private readonly int _sessionId;
    internal bool CanProceed => _splash.Running && IsAllowedForeground();
    internal bool MayLaunch => Volatile.Read(ref _foregroundLeft) == 0 && IsAllowedForeground();
    internal long DeadlineTick => _policy.DeadlineTick;

    internal SteamThemeStartupCover(SplashScreenService splash, FileLogger logger, string? steamPath,
        int minimumMs, int timeoutMs, bool settlingDeliveredExit = false)
    {
        _splash = splash; _logger = logger; _steamRoot = SteamInstallationPath.FindRoot(steamPath);
        using (var current = Process.GetCurrentProcess()) _sessionId = current.SessionId;
        _originHandle = GetForegroundWindow(); _origin = Capture(_originHandle);
        _foregroundTransition = new(settlingDeliveredExit, LastInputTick());
        _desktopShell = settlingDeliveredExit ? CaptureDesktopShell() : null;
        _policy = new(Environment.TickCount64, minimumMs, timeoutMs);
        BindClient();
        var existing = FindMain();
        _monitor = MonitorFromWindow(existing?.Window ?? _originHandle, 2);
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(_monitor, ref monitor))
        {
            _display = monitor.Monitor;
            _splash.SetTargetPhysicalBounds(_display.Left, _display.Top, _display.Right - _display.Left, _display.Bottom - _display.Top);
        }
        _splash.SetForegroundGuard(() =>
        {
            bool allowed = IsAllowedForeground();
            if (!allowed) Interlocked.Exchange(ref _foregroundLeft, 1);
            return allowed;
        });
    }

    internal async Task<SteamThemeStartupResult> WaitAsync()
    {
        var probe = new SteamThemeReadinessProbe(VerifiedSteamProcess);
        try
        {
            while (!_cancel.IsCancellationRequested && _splash.Running)
            {
                bool clientCurrent = BindClient();
                var main = FindMain(requireFullscreen: true);
                bool allowed = IsAllowedForeground();
                bool exact = main is not null && Capture(GetForegroundWindow()) == main;
                bool fullscreen = main is not null && CoversDisplay(main.Window);
                var receipt = exact && fullscreen ? await probe.ReadAsync(_cancel.Token).ConfigureAwait(false) : null;
                if (receipt is not null && (Math.Abs(receipt.PhysicalWidth - (_display.Right - _display.Left)) > 16
                    || Math.Abs(receipt.PhysicalHeight - (_display.Bottom - _display.Top)) > 16)) receipt = null;
                lock (_sync)
                {
                    if (_main is not null && main != _main) _contextChanged = true;
                    if (receipt is not null && main is not null) _main ??= main;
                }
                // Recheck native ownership after the asynchronous CDP response.
                allowed &= IsAllowedForeground();
                exact = main is not null && Capture(GetForegroundWindow()) == main && FindMain(requireFullscreen: true) == main;
                SteamThemeStartupResult result;
                lock (_sync)
                    result = _policy.Observe(Environment.TickCount64, IsOriginForeground(), allowed, exact && _main == main && _main is not null,
                        clientCurrent, !_contextChanged, fullscreen, receipt);
                if (result != SteamThemeStartupResult.Waiting)
                {
                    _logger.Info($"Steam themed startup curtain completed: {result}; CSS proof: {receipt?.Proof ?? "none"}.");
                    if (result == SteamThemeStartupResult.Ready) await _splash.HideAsync(fade: true, fadeMs: 180).ConfigureAwait(false);
                    else _splash.CancelImmediately();
                    return result;
                }
                await Task.Delay(80, _cancel.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _logger.Error("Steam theme readiness could not be confirmed; closing only the startup curtain.", error); }
        _splash.CancelImmediately();
        return SteamThemeStartupResult.Cancelled;
    }

    private bool IsOriginForeground() => _originHandle == 0 ? GetForegroundWindow() == 0
        : _origin is not null && GetForegroundWindow() == _originHandle && Capture(_originHandle) == _origin;
    private bool IsAllowedForeground()
    {
        lock (_sync)
        {
            if (_cancel.IsCancellationRequested || _contextChanged || Volatile.Read(ref _foregroundLeft) != 0 || Volatile.Read(ref _disposed) != 0) return false;
            if (_display.Right > _display.Left)
            {
                var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(_monitor, ref monitor) || !monitor.Monitor.Equals(_display)) return false;
            }
            nint foreground = GetForegroundWindow();
            var identity = Capture(foreground);
            bool allowed = _foregroundTransition.Allows(IsOriginForeground(),
                identity is not null && VerifiedSteamProcess(identity.Pid) == identity.Birth,
                _main is not null && identity == _main, IsNeutralDesktopForeground(foreground, identity), LastInputTick());
            if (!allowed && !_foregroundRejectionLogged)
            {
                _foregroundRejectionLogged = true;
                _logger.Info($"Steam startup foreground scope cancelled: {_foregroundTransition.CancellationReason}; window={foreground}; pid={identity?.Pid ?? 0}.");
            }
            return allowed;
        }
    }

    private Identity? CaptureDesktopShell()
    {
        var identity = Capture(GetShellWindow());
        if (identity is null) return null;
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            return process.SessionId == _sessionId && !process.HasExited
                && process.StartTime.ToFileTimeUtc() == identity.Birth
                && string.Equals(process.MainModule?.FileName,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    StringComparison.OrdinalIgnoreCase) ? identity : null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }
    private bool IsNeutralDesktopForeground(nint foreground, Identity? identity)
    {
        if (foreground == 0) return true;
        if (_desktopShell is null || identity is null || identity.Pid != _desktopShell.Pid
            || identity.Birth != _desktopShell.Birth || Capture(GetShellWindow()) != _desktopShell) return false;
        var name = new StringBuilder(128);
        if (GetClassName(foreground, name, name.Capacity) == 0) return false;
        // File Explorer windows and arbitrary windows owned by Explorer are not the desktop.
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }
    private static uint? LastInputTick()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref input) ? input.Tick : null;
    }

    private bool BindClient()
    {
        lock (_sync)
        {
            if (_client is not null) return CaptureProcess(_client.Pid) == _client.Birth;
            if (_steamRoot is null) return true; // Missing installation times out without hiding another application.
            var matches = new List<Identity>();
            var processes = Process.GetProcessesByName("steam");
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.SessionId != _sessionId || process.HasExited
                            || !string.Equals(process.MainModule?.FileName, Path.Combine(_steamRoot, "steam.exe"), StringComparison.OrdinalIgnoreCase)
                            || !ValveImageTrust.Verify(process.MainModule!.FileName!)) continue;
                        matches.Add(new(0, process.Id, process.StartTime.ToFileTimeUtc()));
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            if (matches.Count > 1) { _contextChanged = true; return false; }
            if (matches.Count == 1) _client = matches[0];
            return true;
        }
    }

    private long? VerifiedSteamProcess(int pid)
    {
        lock (_sync)
        {
            if (_steamRoot is null || !BindClient() || _client is null) return null;
            try
            {
                using var process = Process.GetProcessById(pid);
                long birth = process.StartTime.ToFileTimeUtc();
                if (process.HasExited || process.SessionId != _sessionId) return null;
                if (_trusted.TryGetValue(pid, out var trusted) && trusted.Birth == birth) return birth;
                string path = process.MainModule?.FileName ?? "";
                string name = Path.GetFileName(path);
                if (!Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(_steamRoot) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) || (!name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase))
                    || !ValveImageTrust.Verify(path) || !HasClientLineage(pid, birth)) return null;
                _trusted[pid] = (birth, path);
                return birth;
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or IOException) { return null; }
        }
    }

    private Identity? FindMain(bool requireFullscreen = false)
    {
        List<Identity> matches = new();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindow(hwnd, 4) != 0) return true;
            if (requireFullscreen && !CoversDisplay(hwnd)) return true;
            StringBuilder className = new(128); GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() != "SDL_app") return true;
            var identity = Capture(hwnd);
            if (identity is not null && VerifiedSteamProcess(identity.Pid) == identity.Birth) matches.Add(identity);
            return true;
        }, 0);
        return matches.Count == 1 ? matches[0] : null;
    }
    private bool HasClientLineage(int pid, long birth)
    {
        if (_client is null) return false;
        if (pid == _client.Pid) return birth == _client.Birth;
        nint snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == 0 || snapshot == new nint(-1)) return false;
        try
        {
            Dictionary<uint,uint> parents = new();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) return false;
            do { parents[entry.Pid] = entry.ParentPid; if (parents.Count > 32768) return false; }
            while (Process32Next(snapshot, ref entry));
            uint current = (uint)pid; long childBirth = birth;
            for (int depth = 0; depth < 16; depth++)
            {
                if (!parents.TryGetValue(current, out uint parent) || parent == 0 || parent == current) return false;
                long? parentBirth = CaptureProcess((int)parent);
                // A reused parent PID cannot establish this child's ancestry.
                if (!parentBirth.HasValue || parentBirth > childBirth || parentBirth < _client.Birth) return false;
                if (parent == _client.Pid) return parentBirth == _client.Birth;
                current = parent; childBirth = parentBirth.Value;
            }
            return false;
        }
        finally { CloseHandle(snapshot); }
    }
    private bool CoversDisplay(nint hwnd)
    {
        if (MonitorFromWindow(hwnd, 2) != _monitor || DwmGetWindowAttribute(hwnd, 9, out var bounds, Marshal.SizeOf<NativeRect>()) != 0) return false;
        return Math.Abs(bounds.Left - _display.Left) <= 16 && Math.Abs(bounds.Top - _display.Top) <= 16
            && Math.Abs(bounds.Right - _display.Right) <= 16 && Math.Abs(bounds.Bottom - _display.Bottom) <= 16;
    }
    private static Identity? Capture(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd) || GetWindowThreadProcessId(hwnd, out uint pid) == 0) return null;
        long? birth = CaptureProcess((int)pid);
        return birth.HasValue ? new(hwnd, (int)pid, birth.Value) : null;
    }
    private static long? CaptureProcess(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited ? null : process.StartTime.ToFileTimeUtc(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Cancel(); _splash.CancelImmediately();
    }
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { internal uint Size, Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo value);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal NativeRect Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct ProcessEntry
    {
        internal uint Size, Usage, Pid; internal nuint Heap; internal uint Module, Threads, ParentPid;
        internal int Priority; internal uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] internal string? Name;
    }
    private delegate bool EnumWindowsProc(nint hwnd, nint value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint value);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder value, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out NativeRect bounds,int size);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="Process32FirstW")] private static extern bool Process32First(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="Process32NextW")] private static extern bool Process32Next(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
