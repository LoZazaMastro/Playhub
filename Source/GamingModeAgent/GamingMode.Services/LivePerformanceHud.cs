using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

// A composed Windows HUD. It does not inject into a game and cannot promise
// visibility over exclusive-fullscreen swapchains or protected video surfaces.
internal sealed class LivePerformanceHud : IDisposable
{
    private sealed record Pending(LiveFrameTelemetry Sample, LivePerformanceHudUsage? Usage, LivePerformanceHudBounds Bounds,
        LivePerformanceHudForegroundProof? Foreground);
    private readonly object _gate = new();
    private LivePerformanceHudSettings _settings = new(false);
    private Pending? _pending, _last;
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private HudWindow? _window;
    private bool _scheduled, _requestedVisible, _disposed;
    private long _published, _coalesced;
    private int _visible;
    private SafeProcessHandle? _target;
    private int _targetPid;
    private long _targetBirth;
    private SafeProcessHandle? _foregroundOwner;
    private LivePerformanceHudForegroundProof? _foregroundProof;
    private string? _lastFailure;
    private readonly LivePerformanceHudSnapshotStore _diagnosticCopies = new();
    private static long s_instanceCounter;
    private Func<LivePerformanceHudSubscriptionProof>? _displayProof;
    private LivePerformanceHudNativeDisplay? _nativeDisplay;
    internal void BindDisplayProof(Func<LivePerformanceHudSubscriptionProof> proof) { lock (_gate) _displayProof = proof; }
    internal LivePerformanceHudDisplaySnapshot CopyDiagnostics() => _diagnosticCopies.Read();
    internal string? LastFailure => Volatile.Read(ref _lastFailure);

    internal void Configure(LivePerformanceHudSettings settings)
    {
        lock (_gate) {
            if (_disposed) return;
            _settings = settings.Normalized();
            if (!_settings.Enabled) { _pending = null; _last = null; _requestedVisible = false; }
            Queue();
        }
    }

    internal void Publish(LiveFrameTelemetry sample, LivePerformanceHudUsage? usage, LivePerformanceHudBounds bounds,
        LivePerformanceHudForegroundProof? foreground = null)
    {
        lock (_gate) {
            if (_disposed || !_settings.Enabled) return;
            Interlocked.Increment(ref _published);
            if (_pending is not null) Interlocked.Increment(ref _coalesced);
            _pending = new(sample, usage, bounds, foreground); _requestedVisible = true;
            if (_thread is null) {
                _thread = new Thread(Run) { IsBackground = true, Name = "Playhub performance HUD" };
                _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
            }
            Queue();
        }
    }

    internal void Hide()
    {
        lock (_gate) {
            if (_disposed) return;
            _requestedVisible = false; _pending = null; _last = null; Queue();
        }
    }

    internal LivePerformanceHudDiagnostics Diagnostics
    {
        get {
            var copy = CopyDiagnostics();
            return new(copy.AttemptedDraws, copy.MaximumDrawMicroseconds, copy.AverageDrawMicroseconds,
                copy.Published, copy.Coalesced, copy.BufferedPoints, copy.Visible);
        }
    }

    // Called with _gate held; only one dispatcher operation can wait in line.
    private void Queue()
    {
        if (_dispatcher is null || _scheduled || _dispatcher.HasShutdownStarted) return;
        _scheduled = true;
        try { _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(Drain)); }
        catch (InvalidOperationException) { _scheduled = false; }
    }

    private void Run()
    {
        Native.SetThreadDpiAwarenessContext(new nint(-4));
        try {
            try { _nativeDisplay = LivePerformanceHudNativeDisplay.FromAssemblyPins(); } catch (Exception error) { Volatile.Write(ref _lastFailure, error.GetType().Name); }
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, error) => {
                Volatile.Write(ref _lastFailure, error.Exception.GetType().Name);
                error.Handled = true; HideWindow();
            };
            _window = new HudWindow(OnForegroundChanged, Interlocked.Increment(ref s_instanceCounter));
            _window.Surface.DiagnosticsChanged = () => PublishDiagnostics("OnRender");
            PublishDiagnostics("Created");
            lock (_gate) {
                _dispatcher = dispatcher;
                if (_disposed) dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                else Queue();
            }
            Dispatcher.Run();
        } catch (Exception error) {
            Volatile.Write(ref _lastFailure, error.GetType().Name);
            PublishDiagnostics("RunFailed");
        } finally {
            try { _nativeDisplay?.Dispose(); _window?.Close(); } catch (InvalidOperationException) { }
            _target?.Dispose(); _target = null; _foregroundOwner?.Dispose(); _foregroundOwner = null;
            Volatile.Write(ref _visible, 0);
            PublishDiagnostics("Stopped");
        }
    }

    private void Drain()
    {
        Pending? data;
        LivePerformanceHudSettings settings;
        bool visible;
        lock (_gate) {
            _scheduled = false;
            settings = _settings;
            data = _pending ?? _last;
            _pending = null;
            visible = !_disposed && settings.Enabled && _requestedVisible;
            _last = visible ? data : null;
        }
        if (_foregroundProof != data?.Foreground) { _foregroundOwner?.Dispose(); _foregroundOwner = null; }
        _foregroundProof = data?.Foreground;
        if (!visible || data is null || !data.Bounds.Valid
            || data.Sample.Status is not (FrameMeasurementStatus.Ready or FrameMeasurementStatus.Busy or FrameMeasurementStatus.NoData)
            || !CheckForeground(data.Sample.ProcessId, data.Sample.ProcessBirth)) {
            HideWindow(); return;
        }
        bool shown = _window!.Present(settings, data.Sample, data.Usage, data.Bounds,
            () => CheckForeground(data.Sample.ProcessId, data.Sample.ProcessBirth));
        if (shown && _displayProof is { } proof && _window is { } surface)
            _nativeDisplay?.Publish(proof(), data.Sample, surface.WindowHandle, surface.InstanceId, surface.WindowClass, surface.WindowThreadId, data.Foreground);
        else _nativeDisplay?.Close();
        Volatile.Write(ref _visible, shown ? 1 : 0);
        PublishDiagnostics(shown ? "Presented" : "Concealed");
    }

    private bool CheckForeground(int pid, long birth)
    {
        if (pid <= 0 || birth <= 0) return false;
        nint foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint observed);
        if (_target is null || _target.IsInvalid || _targetPid != pid || _targetBirth != birth) {
            _target?.Dispose();
            _target = Native.OpenProcess(0x1000, false, pid);
            _targetPid = pid; _targetBirth = birth;
            if (_target.IsInvalid || !Native.GetProcessTimes(_target, out long creation, out _, out _, out _) || creation != birth) {
                _target.Dispose(); _target = null; return false;
            }
        }
        if (!Native.GetExitCodeProcess(_target, out uint exitCode) || exitCode != 259) return false;
        if (observed == pid) return true;
        var proof = _foregroundProof;
        if (proof is null || foreground == 0 || foreground != proof.WindowHandle || observed != proof.ProcessId
            || proof.ProcessId <= 0 || proof.ProcessBirth <= 0) return false;
        if (_foregroundOwner is null) {
            _foregroundOwner = Native.OpenProcess(0x1000, false, proof.ProcessId);
            if (_foregroundOwner.IsInvalid || !Native.GetProcessTimes(_foregroundOwner, out long creation, out _, out _, out _)
                || creation != proof.ProcessBirth) {
                _foregroundOwner.Dispose(); _foregroundOwner = null; return false;
            }
        }
        return Native.GetExitCodeProcess(_foregroundOwner, out uint ownerCode) && ownerCode == 259;
    }

    private void OnForegroundChanged()
    {
        // The WinEvent callback runs on this dispatcher's thread. No timer or
        // process enumeration is needed when the HUD is hidden or switched off.
        if (Volatile.Read(ref _visible) != 0 && !CheckForeground(_targetPid, _targetBirth)) HideWindow();
    }

    private void HideWindow()
    {
        _nativeDisplay?.Close();
        _window?.Conceal(); _window?.Surface.Clear();
        _target?.Dispose(); _target = null; _targetPid = 0; _targetBirth = 0;
        _foregroundOwner?.Dispose(); _foregroundOwner = null; _foregroundProof = null;
        Volatile.Write(ref _visible, 0);
        PublishDiagnostics("Concealed");
    }

    // UI-thread publication. GET reads only the immutable copy above.
    private void PublishDiagnostics(string stage)
    {
        try {
            var window = _window; var surface = window?.Surface;
            var sample = _last?.Sample;
            _diagnosticCopies.Publish(new(Environment.TickCount64, stage, window?.InstanceId ?? 0,
                window?.WindowHandle.ToString("X") ?? "0", window?.WindowClass, window?.WindowThreadId ?? 0,
                window?.ExtendedStyle.ToString("X") , window?.Placement, window?.IsVisible ?? false, window?.Opacity ?? 0,
                sample?.ProcessId ?? 0, (sample?.ProcessBirth ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                sample?.Sequence ?? 0, surface?.Draws ?? 0, surface?.CompletedDraws ?? 0, surface?.FailedDraws ?? 0,
                Interlocked.Read(ref _published), Interlocked.Read(ref _coalesced), surface?.BufferedPoints ?? 0,
                surface?.MaximumDrawMicroseconds ?? 0, surface?.AverageDrawMicroseconds ?? 0,
                surface?.LastDrawFailure ?? LastFailure, _nativeDisplay?.CopyDiagnostics()));
        } catch (Exception) { /* Diagnostics never replace drawing/cleanup failures. */ }
    }

    public void Dispose()
    {
        lock (_gate) {
            if (_disposed) return;
            _disposed = true; _pending = _last = null; _requestedVisible = false;
            if (_dispatcher is null && _thread is null) {
                _target?.Dispose(); _target = null; _foregroundOwner?.Dispose(); _foregroundOwner = null;
            }
            if (_dispatcher is { HasShutdownStarted: false }) {
                try { _dispatcher.BeginInvoke(new Action(() => {
                    HideWindow(); _window?.Close(); _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                })); } catch (InvalidOperationException) { }
            }
        }
    }

    private sealed class HudWindow : Window
    {
        internal readonly LivePerformanceHudSurface Surface = new();
        private readonly Action _foregroundChanged;
        private readonly Native.WinEventCallback _eventCallback;
        private readonly DispatcherTimer _expiry;
        private readonly LivePerformanceHudStacking _stacking = new(new NativeStacking());
        private nint _eventHook, _hwnd;
        private LivePerformanceHudPlacement? _placement;
        private bool _drawing;
        internal long InstanceId { get; }
        internal nint WindowHandle => _hwnd;
        internal string? WindowClass { get; private set; }
        internal uint WindowThreadId { get; private set; }
        internal long ExtendedStyle { get; private set; }
        internal LivePerformanceHudPlacement? Placement => _placement;
        internal HudWindow(Action foregroundChanged, long instanceId)
        {
            InstanceId = instanceId;
            _foregroundChanged = foregroundChanged;
            _eventCallback = (_, _, _, _, _, _, _) => _foregroundChanged();
            ShowActivated = false; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent; Focusable = false; IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = Top = -32000;
            Width = Height = 1; Opacity = 0; Content = Surface;
            _expiry = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
            _expiry.Tick += (_, _) => Surface.Tick(Environment.TickCount64);
            SourceInitialized += (_, _) => {
                _hwnd = new WindowInteropHelper(this).Handle;
                long style = Native.GetWindowLongPtr(_hwnd, -20).ToInt64();
                Native.SetWindowLongPtr(_hwnd, -20, new nint(style | Native.ExTransparent | Native.ExNoActivate | Native.ExToolWindow));
                long required = Native.ExTransparent | Native.ExNoActivate | Native.ExToolWindow;
                ExtendedStyle = Native.GetWindowLongPtr(_hwnd, -20).ToInt64();
                if ((ExtendedStyle & required) != required) throw new Win32Exception("HUD input transparency unavailable");
                var name = new System.Text.StringBuilder(256);
                if (Native.GetClassName(_hwnd, name, name.Capacity) > 0) WindowClass = name.ToString();
                WindowThreadId = Native.GetWindowThreadProcessId(_hwnd, out _);
                HwndSource.FromHwnd(_hwnd)?.AddHook(WindowMessage);
            };
            Closed += (_, _) => { StopDrawing(); Unhook(); };
        }

        internal bool Present(LivePerformanceHudSettings settings, LiveFrameTelemetry sample,
            LivePerformanceHudUsage? usage, LivePerformanceHudBounds bounds, Func<bool> foreground)
        {
            long now = Environment.TickCount64;
            Surface.SetData(settings, sample, usage, now);
            var rectangle = new Native.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Left + bounds.Width, Bottom = bounds.Top + bounds.Height };
            nint monitor = Native.MonitorFromRect(ref rectangle, 2);
            double dpi = Native.GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 ? x : 96;
            var placement = LivePerformanceHudGeometry.Place(bounds, settings, dpi);
            // Geometry updates preserve stacking. A separate bounded repair
            // below handles games that later promote themselves above us.
            // Never reassert HWND_TOPMOST on each frame: an injected Steam
            // overlay can retain the game's foreground PID while it opens.
            bool showing = !IsVisible;
            if (Width != Surface.Width) Width = Surface.Width;
            if (Height != Surface.Height) Height = Surface.Height;
            if (showing) { Opacity = 0; Show(); }
            if (showing || _placement != placement) {
                uint flags = 0x10 | 0x200; // SWP_NOACTIVATE | SWP_NOOWNERZORDER
                if (!showing) flags |= 0x4; // SWP_NOZORDER for geometry-only changes
                if (!Native.SetWindowPos(_hwnd, new nint(-1), placement.Left, placement.Top, placement.Width, placement.Height, flags))
                    throw new Win32Exception("HUD placement unavailable");
                _placement = placement;
            }
            if (_eventHook == 0) _eventHook = Native.SetWinEventHook(3, 3, 0, _eventCallback, 0, 0, 0);
            if (_eventHook == 0) throw new Win32Exception("HUD foreground notifications unavailable");
            if (!foreground()) { Conceal(); return false; }
            _stacking.TryRepair(_hwnd, sample.Sequence,
                sample.Status == FrameMeasurementStatus.Ready && sample.UpdatedAtTick <= now
                    && now - sample.UpdatedAtTick < PresentMonLiveMetrics.StaleAfterMs,
                now, foreground);
            if (!foreground()) { Conceal(); return false; }
            Opacity = 1;
            if (!_expiry.IsEnabled) _expiry.Start();
            if (settings.Detail >= 2 && !_drawing) { CompositionTarget.Rendering += Render; _drawing = true; }
            else if (settings.Detail < 2 && _drawing) { CompositionTarget.Rendering -= Render; _drawing = false; }
            return true;
        }

        internal void Conceal() { StopDrawing(); Unhook(); if (IsVisible) Hide(); _placement = null; _stacking.Reset(); }
        private void Render(object? sender, EventArgs args) => Surface.Tick(Environment.TickCount64);
        private void StopDrawing() { _expiry.Stop(); if (_drawing) CompositionTarget.Rendering -= Render; _drawing = false; }
        private void Unhook() { if (_eventHook != 0) Native.UnhookWinEvent(_eventHook); _eventHook = 0; }
        private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x84) { handled = true; return new nint(-1); } // HTTRANSPARENT
            if (message == 0x21) { handled = true; return new nint(3); } // MA_NOACTIVATE
            return 0;
        }

        private sealed class NativeStacking : ILivePerformanceHudStacking
        {
            public nint Foreground() => Native.GetForegroundWindow();
            public nint Previous(nint window) => Native.GetWindow(window, 3); // GW_HWNDPREV
            public bool Visible(nint window) => Native.IsWindowVisible(window);
            public bool PlaceAfter(nint hud, nint anchor, uint flags) => Native.SetWindowPos(hud, anchor, 0, 0, 0, 0, flags);
        }
    }

    internal static class Native
    {
        internal const long ExTransparent = 0x20, ExNoActivate = 0x08000000, ExToolWindow = 0x80;
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        internal delegate void WinEventCallback(nint hook, uint type, nint hwnd, int objectId, int childId, uint thread, uint tick);
        [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int capacity);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint code);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern nint MonitorFromRect(ref Rect rect, uint flags);
        [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);
    }
}
