using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GamingMode.Services;

// Resolving is allowed only from Steam's current tracked app and an observed
// game surface. No HTTP client can supply a PID, image, HWND or executable.
internal sealed class LivePerformanceSourceResolver
{
    private readonly GamingWindowFocusService _focus;
    internal LivePerformanceSourceResolver(GamingWindowFocusService focus) => _focus = focus;

    internal async Task<ILivePerformanceSource?> ResolveAsync(uint appId, CancellationToken cancellation)
    {
        if (appId == 0 || SteamInstallationPath.FindRoot() is not { } root
            || SteamInstallationPath.ReadVerifiedClientBirth(root) is not { } steamBirth) return null;
        Process? steam = null, game = null, wrapper = null;
        Win32OwnedAssociationCatalog.Lease? owned = null;
        try {
            using var self = Process.GetCurrentProcess();
            var steamCandidates = Process.GetProcessesByName("steam");
            try {
                foreach (var process in steamCandidates) {
                    if (process.HasExited || process.SessionId != self.SessionId || process.StartTime != steamBirth
                        || !string.Equals(process.MainModule?.FileName, Path.Combine(root, "steam.exe"), StringComparison.OrdinalIgnoreCase)) continue;
                    if (steam is not null) return null;
                    steam = Process.GetProcessById(process.Id); _ = steam.Handle;
                }
            } finally { foreach (var process in steamCandidates) process.Dispose(); }
            if (steam is null || !ValveImageTrust.Verify(Path.Combine(root, "steam.exe"))) return null;

            nint window = 0;
            uint pid = 0;
            long birth = 0;
            string? aumid = null;
            Func<bool>? stillAssociated = null;
            if (appId >= 0x80000000) {
                var packaged = UwpSteamSessionAssociation.FindForApp(appId);
                if (packaged.Count > 1) return null;
                if (packaged is [var uwp]) {
                    pid = uwp.GamePid; birth = uwp.GameBirth; aumid = uwp.Aumid;
                    wrapper = Process.GetProcessById(checked((int)uwp.WrapperPid)); _ = wrapper.Handle;
                    if (wrapper.StartTime.ToFileTimeUtc() != uwp.WrapperBirth) return null;
                    stillAssociated = () => UwpSteamSessionAssociation.Find(pid, appId) == uwp;
                } else {
                    owned=await Win32SteamSessionAssociation.FindLeaseAsync(appId,cancellation).ConfigureAwait(false);
                    if(owned is not null) {
                        var win32=owned.Identity;
                        if(!owned.Current||(uint)(win32.GameId>>32)!=appId||(uint)win32.GameId!=0x02000000)return null;
                        pid=win32.GamePid;birth=win32.GameBirth;
                        wrapper=Process.GetProcessById(checked((int)win32.WrapperPid));_=wrapper.Handle;
                        if(wrapper.StartTime.ToFileTimeUtc()!=win32.WrapperBirth)return null;
                    }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (pid == 0 && (!_focus.TryGetQamSource(out window, out pid) || pid == 0)
                && !TryFindTrackedWindow(appId, self.SessionId, out window, out pid)) return null;
            game = Process.GetProcessById(checked((int)pid)); _ = game.Handle;
            long observedBirth = game.StartTime.ToFileTimeUtc();
            if (birth != 0 && birth != observedBirth) return null;
            if (game.HasExited || game.SessionId != self.SessionId || steam.HasExited
                || !FrameMeasurementTargetPolicy.CanMeasureWindowOwner(game.ProcessName)
                || (owned is not null ? !owned.Current : stillAssociated is null && !OverlaySteamArtworkResolver.IsTrackedAppProcess(game.Id, appId))) return null;
            var source = new Source(game, steam, wrapper, new(game.Id, observedBirth), appId, aumid,
                window, stillAssociated,owned);
            // A missing game window during loading can be resolved again on the
            // next lease renew; do not attach to a launch helper in its place.
            if (!source.RefreshWindow()) { source.Dispose(); game = steam = wrapper = null;owned=null; return null; }
            game = steam = wrapper = null;owned=null;
            return source;
        } catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException
            or System.ComponentModel.Win32Exception or UnauthorizedAccessException or OverflowException) { return null; }
        finally { owned?.Dispose();game?.Dispose(); steam?.Dispose(); wrapper?.Dispose(); }
    }

    private static bool TryFindTrackedWindow(uint appId, int session, out nint window, out uint pid)
    {
        window = 0; pid = 0;
        // Desktop Mode does not run GamingWindowFocusService's borderless scan.
        // Resolve the same signed Steam tracking independently; a library title
        // or a matching filename is never evidence of a running game.
        nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
        foreach (nint candidate in OverlayWindowTools.EnumerateTopLevelHandles().Take(128)) {
            if (!OverlayWindowTools.IsPresentGameSurface(candidate)) continue;
            int owner = OverlayWindowTools.WindowOwnerProcessId(candidate);
            try {
                using var process = Process.GetProcessById(owner);
                if (process.HasExited || process.SessionId != session
                    || !FrameMeasurementTargetPolicy.CanMeasureWindowOwner(process.ProcessName)
                    || !OverlaySteamArtworkResolver.IsTrackedAppProcess(owner, appId)) continue;
                if (pid != 0 && pid != owner) { window = 0; pid = 0; return false; }
                if (window == 0 || candidate == foreground || candidate == process.MainWindowHandle) window = candidate;
                pid = (uint)owner;
            } catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
        }
        return window != 0 && pid != 0;
    }

    private sealed class Source : ILivePerformanceSource,ILivePerformanceAssociationRefresh
    {
        private readonly Process _game, _steam;
        private readonly Process? _wrapper;
        private readonly uint _appId;
        private readonly string? _aumid;
        private readonly Func<bool>? _associated;
        private Win32OwnedAssociationCatalog.Lease? _owned;
        private readonly Win32SteamSessionAssociation.Identity? _ownedIdentity;
        private readonly object _gate = new();
        private nint _window;
        private int _windowPid;
        private long _windowBirth;
        private bool _disposed;
        public PresentMonTarget Target { get; }
        internal Source(Process game, Process steam, Process? wrapper, PresentMonTarget target, uint appId,
            string? aumid, nint window, Func<bool>? associated,Win32OwnedAssociationCatalog.Lease? owned) =>
            (_game, _steam, _wrapper, Target, _appId, _aumid, _window, _associated,_owned,_ownedIdentity) =
            (game, steam, wrapper, target, appId, aumid, window, associated,owned,owned?.Identity);

        public bool Current {
            get {
                lock (_gate) {
                    if (!Alive()) return false;
                    if (!AssociationCurrent()) return false;
                    RefreshWindow();
                    return (_ownedIdentity is null||AssociationCurrent())&&Alive();
                }
            }
        }
        private bool AssociationCurrent()=>_ownedIdentity is not null
            ?_owned is {} held&&held.Current&&held.Identity==_ownedIdentity&&held.Current
            :_associated is not null?_associated():OverlaySteamArtworkResolver.IsTrackedAppProcess(Target.ProcessId,_appId);
        public async Task<bool> RefreshAssociationAsync(CancellationToken cancellation)
        {
            lock(_gate){if(!Alive())return false;if(_ownedIdentity is null)return true;}
            var fresh=await Win32SteamSessionAssociation.FindLeaseAsync(_appId,cancellation).ConfigureAwait(false);
            try {
                lock(_gate) {
                    if(!Alive()||fresh is null||!fresh.Current||fresh.Identity!=_ownedIdentity||!fresh.Current||!Alive())return false;
                    var old=_owned;_owned=fresh;fresh=null;old?.Dispose();
                    return AssociationCurrent()&&Alive();
                }
            } finally {fresh?.Dispose();}
        }
        private bool Alive() => !_disposed && !_game.HasExited && !_steam.HasExited && (_wrapper is null || !_wrapper.HasExited);

        internal bool RefreshWindow()
        {
            lock (_gate) {
                if (!Alive()) return false;
                if (_window != 0 && AcceptWindow(_window)) return true;
                _window = 0;
                foreach (nint candidate in OverlayWindowTools.EnumerateTopLevelHandles())
                    if (AcceptWindow(candidate)) { _window = candidate; return true; }
                return false;
            }
        }
        private bool AcceptWindow(nint window)
        {
            if (!OverlayWindowTools.IsPresentGameSurface(window)) return false;
            int owner = OverlayWindowTools.WindowOwnerProcessId(window);
            if (owner != Target.ProcessId) {
                if (_aumid is null || !string.Equals(UwpFrameWindow.ReadAppId(window), _aumid, StringComparison.OrdinalIgnoreCase)) return false;
                var identity = UwpSteamSessionAssociation.Find((uint)Target.ProcessId, _appId);
                if (identity is null || !UwpSteamSessionAssociation.IsOnlyRunningInstance(identity)) return false;
            }
            using var process = Process.GetProcessById(owner);
            if (process.HasExited || process.SessionId != _game.SessionId) return false;
            _windowPid = owner; _windowBirth = process.StartTime.ToFileTimeUtc();
            return true;
        }

        public LivePerformanceHudForegroundProof? ForegroundProof {
            get { lock (_gate) return _window != 0 ? new(_window, _windowPid, _windowBirth) : null; }
        }
        public bool TryGetBounds(out LivePerformanceHudBounds bounds)
        {
            lock (_gate) {
                bounds = default;
                if (!Alive() || _ownedIdentity is not null&&!AssociationCurrent() || _window == 0 || Native.GetForegroundWindow() != _window
                    || OverlayWindowTools.WindowOwnerProcessId(_window) != _windowPid
                    || !OverlayWindowTools.IsPresentGameSurface(_window)) return false;
                nint dpi = Native.SetThreadDpiAwarenessContext(new(-4));
                try {
                    if (!Native.GetClientRect(_window, out var rect)) return false;
                    var origin = new Native.Point { X = rect.Left, Y = rect.Top };
                    if (!Native.ClientToScreen(_window, ref origin)) return false;
                    bounds = new(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    return bounds.Valid && (_ownedIdentity is null||AssociationCurrent()) && Alive() && Native.GetForegroundWindow() == _window;
                } finally { if (dpi != 0) Native.SetThreadDpiAwarenessContext(dpi); }
            }
        }
        public void Dispose()
        {
            lock (_gate) {
                if (_disposed) return;
                _disposed = true; _owned?.Dispose();_owned=null;_game.Dispose(); _steam.Dispose(); _wrapper?.Dispose();
            }
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window, ref Point point);
        [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    }
}

internal sealed class LivePerformanceRuntime : ILivePerformanceRuntime, ILivePerformanceHudDiagnosticsRuntime, ILivePerformanceHudDisplayRuntime, ILivePerformanceTargetUsageRuntime
{
    private readonly LivePerformanceSourceResolver _resolver;
    private readonly PresentMonLiveCollector _collector;
    private readonly LivePerformanceHud _hud = new();
    private readonly LivePerformanceSystemUsage _usage = new();
    internal LivePerformanceRuntime(GamingWindowFocusService focus, string provider) =>
        (_resolver, _collector) = (new(focus), new(provider));
    public Task<ILivePerformanceSource?> ResolveAsync(uint appId, CancellationToken cancellation) => _resolver.ResolveAsync(appId, cancellation);
    public Task<FrameMeasurementStatus> StreamAsync(PresentMonTarget target, int refreshMs,
        Action<LiveFrameTelemetry> publish, CancellationToken cancellation) => _collector.StreamAsync(target, refreshMs, publish, cancellation);
    public void Configure(LivePerformanceHudSettings settings) { _hud.Configure(settings); if (!settings.Enabled) _usage.Reset(); }
    public void Publish(LiveFrameTelemetry sample, LivePerformanceHudUsage? usage, LivePerformanceHudBounds bounds,
        LivePerformanceHudForegroundProof? foreground) => _hud.Publish(sample, usage, bounds, foreground);
    public LivePerformanceHudDisplaySnapshot CopyHudDiagnostics() => _hud.CopyDiagnostics();
    public void BindDisplayProof(Func<LivePerformanceHudSubscriptionProof> proof) => _hud.BindDisplayProof(proof);
    public void Hide() => _hud.Hide();
    public void Dispose() { _hud.Dispose(); _usage.Dispose(); }
    public LivePerformanceHudUsage? ReadUsage() => _usage.Read();
    public LivePerformanceHudUsage? ReadUsage(int processId) => _usage.Read(processId);
}
