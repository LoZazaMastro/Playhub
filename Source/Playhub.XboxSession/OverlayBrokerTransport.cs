// Playhub Xbox Session. Adapted from WSGM OverlayObjectBroker at 5268460, GPL-3.0-only.
using System.ComponentModel;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WSGM.PackagedLaunch;

internal sealed class OverlayBrokerTransport : IDisposable
{
    // Request layout in Native/WsgmBridge/Bridge.cpp. All handles are unnamed.
    internal const int Size = 552;
    internal const int Version = 1;
    internal const int NameOffset = 40;
    internal const int ResultOffset = 24;
    internal const int ErrorOffset = 32;
    internal const int Denied = 5;
    private const int InvalidHandle = 6;
    private const uint ReadWriteMapping = 0x06;
    private const uint Sync = 0x100000;
    private const uint EventAccess = Sync | 0x02;
    private const uint MutexAccess = Sync | 0x01;
    private const uint MaxMappingSize = 64 * 1024 * 1024;

    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _request;
    private readonly EventWaitHandle _response;
    private readonly EventWaitHandle _stop;
    private readonly Thread _worker;
    private readonly OverlayObjectAllowList _allowed;
    private readonly Func<nint, uint, nint> _duplicate;
    private int _disposed;

    // The owner supplies a duplicator tied to its already verified, retained process handle.
    // This component does not choose or open a game process and has no activation entry point.
    internal OverlayBrokerTransport(int pid, ulong gameId, Func<nint, uint, nint> duplicate)
    {
        if (pid <= 0 || (uint)gameId != 0x02000000 || (gameId >> 32) < 0x80000000)
            throw new ArgumentException("An exact Steam shortcut game ID is required.");
        _allowed = new OverlayObjectAllowList(pid, gameId.ToString(CultureInfo.InvariantCulture));
        _duplicate = duplicate ?? throw new ArgumentNullException(nameof(duplicate));
        var allocated = new Stack<IDisposable>();
        T Own<T>(T resource) where T : IDisposable { allocated.Push(resource); return resource; }
        try
        {
            _mapping = Own(MemoryMappedFile.CreateNew(null, Size));
            _view = Own(_mapping.CreateViewAccessor(0, Size));
            _request = Own(new EventWaitHandle(false, EventResetMode.AutoReset));
            _response = Own(new EventWaitHandle(false, EventResetMode.AutoReset));
            _stop = Own(new EventWaitHandle(false, EventResetMode.ManualReset));
            _view.Write(0, Version);
            _worker = new Thread(Run) { IsBackground = true, Name = "Playhub Xbox overlay broker" };
            _worker.Start();
        }
        catch
        {
            while (allocated.TryPop(out var resource)) resource.Dispose();
            throw;
        }
    }

    internal (nint Mapping, nint Request, nint Response) Handles => (_mapping.SafeMemoryMappedFileHandle.DangerousGetHandle(),
        _request.SafeWaitHandle.DangerousGetHandle(), _response.SafeWaitHandle.DangerousGetHandle());

    private void Run()
    {
        try
        {
            WaitHandle[] handles = [_stop, _request];
            while (WaitHandle.WaitAny(handles) == 1)
            {
                try { Respond(); }
                catch (Win32Exception error) { Complete(0, error.NativeErrorCode); }
                catch (Exception) { Complete(0, InvalidHandle); }
                _response.Set();
            }
        }
        finally
        {
            _stop.Dispose();
            _response.Dispose();
            _request.Dispose();
            _view.Dispose();
            _mapping.Dispose();
        }
    }

    private void Respond()
    {
        Complete(0, Denied);
        if (_view.ReadInt32(0) != Version || _view.ReadInt32(36) != 0) return;
        uint operation = _view.ReadUInt32(4), access = _view.ReadUInt32(8), flags = _view.ReadUInt32(12);
        uint high = _view.ReadUInt32(16), low = _view.ReadUInt32(20);
        var encodedName = new byte[512];
        _view.ReadArray(NameOffset, encodedName, 0, encodedName.Length);
        string characters = Encoding.Unicode.GetString(encodedName);
        int terminator = characters.IndexOf('\0');
        if (terminator < 1) return;
        string name = characters[..terminator];
        if (!_allowed.Admits(name)) return;
        bool mapping = name.EndsWith("_mem-IPCWrapper", StringComparison.Ordinal);
        bool mutex = name.EndsWith("_mutex-IPCWrapper", StringComparison.Ordinal);

        // Accept observed all-access masks only after reducing them to the bridge's required rights.
        uint requested = operation switch
        {
            2 when access == 0x000F001F => ReadWriteMapping,
            4 when access == 0x001F0001 => MutexAccess,
            6 when access == 0x001F0003 => EventAccess,
            _ => access
        };
        Marshal.SetLastPInvokeError(0);
        (SafeWaitHandle? Object, uint Rights) named = operation switch
        {
            1 when mapping && access == 4 && flags == 0 && high == 0 && low is > 0 and <= MaxMappingSize
                => (CreateFileMapping(new nint(-1), 0, 4, 0, low, name), ReadWriteMapping),
            2 when mapping && Fits(requested, ReadWriteMapping) && flags == 0 && high == 0 && low == 0
                => (OpenFileMapping(requested, false, name), requested),
            3 when mutex && access == 0 && flags == 0 && high == 0 && low == 0
                => (CreateMutex(0, false, name), MutexAccess),
            4 when mutex && Fits(requested, MutexAccess) && flags == 0 && high == 0 && low == 0
                => (OpenMutex(requested, false, name), requested),
            5 when !mapping && !mutex && access == 0 && flags <= 3 && high == 0 && low == 0
                => (CreateEvent(0, (flags & 1) != 0, (flags & 2) != 0, name), EventAccess),
            6 when !mapping && !mutex && Fits(requested, EventAccess) && flags == 0 && high == 0 && low == 0
                => (OpenEvent(requested, false, name), requested),
            _ => (null, 0)
        };
        int error = Marshal.GetLastPInvokeError();
        if (named.Object is null) return;
        using var local = named.Object;
        if (local.IsInvalid) { Complete(0, error == 0 ? InvalidHandle : error); return; }
        if (Volatile.Read(ref _disposed) != 0) return;
        nint duplicate = _duplicate(local.DangerousGetHandle(), named.Rights);
        Complete(duplicate, duplicate == 0 ? InvalidHandle : error);
    }

    private static bool Fits(uint requested, uint allowed) => requested != 0 && (requested & ~allowed) == 0;

    private void Complete(nint result, int error)
    {
        _view.Write(ResultOffset, result.ToInt64());
        _view.Write(ErrorOffset, error);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set();
        // The worker owns the handles even if an in-flight duplicator misses this budget.
        if (Thread.CurrentThread != _worker) _worker.Join(TimeSpan.FromSeconds(2));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileMappingW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateFileMapping(nint file, nint attributes, uint protect, uint high, uint low, string name);
    [DllImport("kernel32.dll", EntryPoint = "OpenFileMappingW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle OpenFileMapping(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", EntryPoint = "CreateMutexW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateMutex(nint attributes, bool owner, string name);
    [DllImport("kernel32.dll", EntryPoint = "OpenMutexW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle OpenMutex(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateEvent(nint attributes, bool manual, bool initial, string name);
    [DllImport("kernel32.dll", EntryPoint = "OpenEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle OpenEvent(uint access, bool inherit, string name);
}
