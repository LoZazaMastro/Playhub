using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

internal sealed record SteamDownloadPowerResult(bool Ok, string? Lease = null, string? Reason = null);

internal sealed class SteamDownloadPowerLease : IDisposable
{
    private const int LeaseMilliseconds = 8_000;
    private readonly object _sync = new();
    private readonly Func<bool> _allowed;
    private readonly Func<bool> _ac;
    private readonly Func<Action, IDisposable?> _acquire;
    private readonly Func<long> _clock;
    private IDisposable? _request;
    private Timer? _watchdog;
    private string? _lease;
    private uint _appId;
    private long _expires;
    private bool _disposed;

    internal SteamDownloadPowerLease(Func<bool> allowed)
        : this(allowed, NativeSteamDownloadPowerRequest.OnAc, NativeSteamDownloadPowerRequest.TryAcquire, () => Environment.TickCount64) { }

    internal SteamDownloadPowerLease(Func<bool> allowed, Func<bool> ac, Func<Action, IDisposable?> acquire, Func<long> clock)
    {
        _allowed = allowed;
        _ac = ac;
        _acquire = acquire;
        _clock = clock;
    }

    internal SteamDownloadPowerResult Start(uint appId)
    {
        lock (_sync)
        {
            ExpireIfNeeded();
            if (appId == 0 || !CanHold()) return new(false, Reason: "unavailable");
            if (_request is not null) return new(false, Reason: "busy");
            string lease = Guid.NewGuid().ToString("N");
            _request = _acquire(() => Stop(lease));
            if (_request is null) return new(false, Reason: "power_request_denied");
            _lease = lease;
            _appId = appId;
            _expires = _clock() + LeaseMilliseconds;
            _watchdog = new Timer(_ => Check(lease), null, 1_000, 1_000);
            return new(true, _lease);
        }
    }

    internal SteamDownloadPowerResult Renew(string lease, uint appId)
    {
        lock (_sync)
        {
            ExpireIfNeeded();
            if (_request is null || _lease != lease || _appId != appId) return new(false, Reason: "ended");
            _expires = _clock() + LeaseMilliseconds;
            return new(true, _lease);
        }
    }

    internal bool Stop(string lease)
    {
        lock (_sync)
        {
            if (_request is null || _lease != lease) return false;
            Release();
            return true;
        }
    }

    internal void Revoke() { lock (_sync) Release(); }

    private void Check(string lease)
    {
        lock (_sync)
            if (_lease == lease) ExpireIfNeeded();
    }

    private bool CanHold()
    {
        try { return !_disposed && _ac() && _allowed(); }
        catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
        { return false; }
    }

    private void ExpireIfNeeded()
    {
        if (_request is not null && (_clock() >= _expires || !CanHold())) Release();
    }

    private void Release()
    {
        _watchdog?.Dispose();
        _watchdog = null;
        _request?.Dispose();
        _request = null;
        _lease = null;
        _appId = 0;
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; Release(); }
    }
}

internal sealed class NativeSteamDownloadPowerRequest : IDisposable
{
    private const int SystemRequired = 1;
    private readonly SafeFileHandle _handle;
    private readonly PowerModeChangedEventHandler _powerChanged;

    private NativeSteamDownloadPowerRequest(SafeFileHandle handle, PowerModeChangedEventHandler powerChanged)
    {
        _handle = handle;
        _powerChanged = powerChanged;
    }

    internal static bool OnAc() => OperatingSystem.IsWindows() && GetSystemPowerStatus(out var status) && status.AcLineStatus == 1;

    internal static IDisposable? TryAcquire(Action stop)
    {
        if (!OnAc()) return null;
        PowerModeChangedEventHandler change = (_, args) =>
        {
            if (args.Mode is PowerModes.Suspend or PowerModes.Resume || !OnAc()) stop();
        };
        try { SystemEvents.PowerModeChanged += change; }
        catch (InvalidOperationException) { return null; }

        nint reason = 0;
        SafeFileHandle? handle = null;
        bool acquired = false;
        try
        {
            reason = Marshal.StringToHGlobalUni("Playhub: finishing a Steam download");
            var context = new ReasonContext { Version = 0, Flags = 1, Reason = new ReasonUnion { Simple = reason } };
            handle = PowerCreateRequest(in context);
            if (!handle.IsInvalid && OnAc() && PowerSetRequest(handle, SystemRequired))
            {
                var request = new NativeSteamDownloadPowerRequest(handle, change);
                acquired = true;
                return request;
            }
            return null;
        }
        finally
        {
            if (reason != 0) Marshal.FreeHGlobal(reason);
            if (!acquired)
            {
                SystemEvents.PowerModeChanged -= change;
                handle?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= _powerChanged;
        try { PowerClearRequest(_handle, SystemRequired); }
        finally { _handle.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        internal byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        internal uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        internal uint Version, Flags;
        internal ReasonUnion Reason;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ReasonUnion
    {
        [FieldOffset(0)] internal nint Simple;
        [FieldOffset(0)] internal DetailedReason Detailed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetailedReason
    {
        internal nint ResourceFile;
        internal uint ReasonId, StringCount;
        internal nint Strings;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll")] private static extern SafeFileHandle PowerCreateRequest(in ReasonContext context);
    [DllImport("kernel32.dll")] private static extern bool PowerSetRequest(SafeFileHandle request, int requestType);
    [DllImport("kernel32.dll")] private static extern bool PowerClearRequest(SafeFileHandle request, int requestType);
}
