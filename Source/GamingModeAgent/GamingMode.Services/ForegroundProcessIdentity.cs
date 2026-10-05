using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

internal interface IForegroundProcessIdentity : IDisposable
{
    string Name { get; }
    bool IsAlive { get; }
}

// Cache only one foreground PID, for short bursts of navigation feedback.
// Keep its process handle so PID reuse cannot inherit the previous identity.
internal sealed class ForegroundProcessIdentityCache
{
    private readonly Func<int, IForegroundProcessIdentity?> _open;
    private readonly Func<long> _clock;
    private readonly object _sync = new();
    private IForegroundProcessIdentity? _cached;
    private int _pid;
    private long _checkedAt;
    internal const long LifetimeMs = 250;

    public ForegroundProcessIdentityCache(Func<int, IForegroundProcessIdentity?> open, Func<long> clock)
    {
        _open = open;
        _clock = clock;
    }

    public string? Read(int pid)
    {
        lock (_sync)
        {
            long now = _clock();
            if (pid > 0 && _pid == pid && _cached is not null
                && now - _checkedAt >= 0 && now - _checkedAt < LifetimeMs && _cached.IsAlive)
                return _cached.Name;
            _cached?.Dispose();
            _cached = null;
            _pid = 0;
            if (pid <= 0) return null;
            _cached = _open(pid);
            if (_cached is null) return null;
            if (!_cached.IsAlive)
            {
                _cached.Dispose();
                _cached = null;
                return null;
            }
            _pid = pid;
            _checkedAt = now;
            return _cached.Name;
        }
    }
}

internal sealed class NativeForegroundProcessIdentity : IForegroundProcessIdentity
{
    private readonly SafeProcessHandle _handle;
    public string Name { get; }
    private NativeForegroundProcessIdentity(SafeProcessHandle handle, string name)
    {
        _handle = handle;
        Name = name;
    }

    public bool IsAlive => GetProcessTimes(_handle, out _, out FileTime exited, out _, out _)
        && exited.Low == 0 && exited.High == 0;
    public void Dispose() => _handle.Dispose();

    public static IForegroundProcessIdentity? Open(int pid)
    {
        SafeProcessHandle handle = OpenProcess(0x1000, false, pid); // query limited information only
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        var image = new StringBuilder(32768);
        int length = image.Capacity;
        if (!QueryFullProcessImageName(handle, 0, image, ref length))
        {
            handle.Dispose();
            return null;
        }
        return new NativeForegroundProcessIdentity(handle, Path.GetFileNameWithoutExtension(image.ToString()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }
    [DllImport("kernel32.dll")]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref int length);
    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out FileTime created, out FileTime exited, out FileTime kernel, out FileTime user);
}
