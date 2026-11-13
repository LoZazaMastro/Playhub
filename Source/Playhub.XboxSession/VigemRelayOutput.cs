// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class VigemRelayOutput : IGuardedXboxRelayOutput
{
    internal const ushort Vendor = 0x045E;
    internal const ushort Product = 0x7EB0;
    // Native x64 resource from Nefarius.ViGEm.Client 1.21.256 (MIT).
    private const string LibrarySha256 = "00303F99E1968D523FBD08A7C691D60D145B7335059EC593947841D124C10875";
    private const uint Success = 0x20000000;
    private const long PdoRecheckMs = 250;

    private readonly string _nativePath;
    private readonly WindowsXboxPdo _pnp;
    private FileStream? _pin;
    private NativeClient? _api;
    private nint _client, _target;
    private bool _connected, _attachAttempted, _closed;
    private uint _serial, _userIndex;
    private string? _device;
    private long _pdoVerifiedAt;

    // Construction does not load code or inspect devices.
    internal VigemRelayOutput(string nativePath, string approvedBus)
    {
        _nativePath = nativePath;
        _pnp = new WindowsXboxPdo(approvedBus);
    }

    public bool TryConnectNeutral(Func<bool> canAttach)
    {
        if (_closed || _api is not null || !_pnp.SyntheticAbsent()) return false;
        if (!Path.IsPathFullyQualified(_nativePath) || !canAttach()) return false;
        _pin = new FileStream(_nativePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(SHA256.HashData(_pin)).Equals(LibrarySha256, StringComparison.Ordinal)) return false;
        _api = new NativeClient(_nativePath);
        _client = _api.AllocateClient();
        if (_client == 0 || _api.Connect(_client) != Success) return false;
        _connected = true;
        _target = _api.AllocateTarget();
        if (_target == 0) return false;

        _api.SetVendor(_target, Vendor);
        _api.SetProduct(_target, Product);
        if (!canAttach() || !_pnp.SyntheticAbsent()) return false;
        _attachAttempted = true;
        uint result = _api.Add(_client, _target);
        _serial = _api.Index(_target);
        if (result != Success || _serial == 0) return false;

        // ViGEm initializes neutral; establish our own neutral report before
        // doing any further work or forwarding a physical input.
        if (_api.Update(_client, _target, default) != Success) return false;
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            if (!canAttach()) return false;
            _device = _pnp.Resolve(_serial);
            if (_device is not null && _api.UserIndex(_client, _target, out _userIndex) == Success && _userIndex <= 3)
            {
                _pdoVerifiedAt = Environment.TickCount64;
                return IsOwned;
            }
            Thread.Sleep(20);
        } while (deadline.ElapsedMilliseconds < 1000);
        return false;
    }

    public bool IsOwned => !_closed && _device is not null && _api is { } api
        && api.Attached(_target) != 0 && api.Index(_target) == _serial
        && api.GetVendor(_target) == Vendor && api.GetProduct(_target) == Product
        && api.UserIndex(_client, _target, out uint currentIndex) == Success && currentIndex == _userIndex
        && IsSamePdo();

    public bool TrySubmitCurrent(XboxReport report, Func<bool> canForward)
    {
        // Native ownership/PDO getters precede the adjacent source/input guard.
        if (!IsOwned || !canForward()) return false;
        if (_api!.Update(_client, _target, XusbReport.From(report.GameControlsOnly())) != Success)
            throw new NativeRelaySubmitFailed();
        return true;
    }

    private bool IsSamePdo()
    {
        long now = Environment.TickCount64;
        if (now - _pdoVerifiedAt < PdoRecheckMs) return true;
        if (!string.Equals(_device, _pnp.Resolve(_serial), StringComparison.OrdinalIgnoreCase)) return false;
        _pdoVerifiedAt = now;
        return true;
    }

    public void Submit(XboxReport report)
    {
        if (!IsOwned || _api!.Update(_client, _target, XusbReport.From(report.GameControlsOnly())) != Success)
            throw new InvalidOperationException("Xbox relay output ownership or submission was lost.");
    }

    public bool Close()
    {
        if (_closed) return false;
        _closed = true;
        bool reportedNeutral = true;
        try
        {
            if (_api is { } api)
            {
                if (_attachAttempted && _target != 0 && api.Attached(_target) != 0)
                {
                    try { reportedNeutral = api.Update(_client, _target, default) == Success; }
                    finally { api.Remove(_client, _target); }
                }
            }
        }
        finally { ReleaseNative(); }

        if (!_attachAttempted) return true;
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            if (_pnp.SyntheticAbsent()) return reportedNeutral;
            Thread.Sleep(20);
        } while (deadline.ElapsedMilliseconds < 750);
        return false;
    }

    private void ReleaseNative()
    {
        if (_api is not { } api)
        {
            _pin?.Dispose();
            return;
        }
        try
        {
            if (_connected) api.Disconnect(_client);
        }
        finally
        {
            try { if (_target != 0) api.FreeTarget(_target); }
            finally
            {
                try { if (_client != 0) api.FreeClient(_client); }
                finally
                {
                    try { api.Dispose(); }
                    finally { _pin?.Dispose(); }
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct XusbReport(ushort Buttons, byte Lt, byte Rt, short Lx, short Ly, short Rx, short Ry)
    {
        internal static XusbReport From(XboxReport input) => new(input.Buttons, input.LeftTrigger,
            input.RightTrigger, input.LeftX, input.LeftY, input.RightX, input.RightY);
    }

    private sealed class NativeClient : IDisposable
    {
        private readonly nint _library;
        internal readonly Allocate AllocateClient, AllocateTarget;
        internal readonly Release FreeClient, FreeTarget, Disconnect;
        internal readonly ConnectClient Connect;
        internal readonly TargetOperation Add, Remove;
        internal readonly SetId SetVendor, SetProduct;
        internal readonly GetId GetVendor, GetProduct;
        internal readonly ReadIndex Index;
        internal readonly ReadAttached Attached;
        internal readonly ReadUserIndex UserIndex;
        internal readonly SendReport Update;

        internal NativeClient(string path)
        {
            _library = NativeLibrary.Load(path, typeof(VigemRelayOutput).Assembly, DllImportSearchPath.System32);
            try
            {
                AllocateClient = Get<Allocate>("vigem_alloc");
                AllocateTarget = Get<Allocate>("vigem_target_x360_alloc");
                FreeClient = Get<Release>("vigem_free");
                FreeTarget = Get<Release>("vigem_target_free");
                Disconnect = Get<Release>("vigem_disconnect");
                Connect = Get<ConnectClient>("vigem_connect");
                Add = Get<TargetOperation>("vigem_target_add");
                Remove = Get<TargetOperation>("vigem_target_remove");
                SetVendor = Get<SetId>("vigem_target_set_vid");
                SetProduct = Get<SetId>("vigem_target_set_pid");
                GetVendor = Get<GetId>("vigem_target_get_vid");
                GetProduct = Get<GetId>("vigem_target_get_pid");
                Index = Get<ReadIndex>("vigem_target_get_index");
                Attached = Get<ReadAttached>("vigem_target_is_attached");
                UserIndex = Get<ReadUserIndex>("vigem_target_x360_get_user_index");
                Update = Get<SendReport>("vigem_target_x360_update");
            }
            catch { NativeLibrary.Free(_library); throw; }
        }

        private T Get<T>(string symbol) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, symbol));

        public void Dispose() => NativeLibrary.Free(_library);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Allocate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConnectClient(nint client);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint TargetOperation(nint client, nint target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetId(nint target, ushort id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ushort GetId(nint target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadIndex(nint target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadAttached(nint target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadUserIndex(nint client, nint target, out uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SendReport(nint client, nint target, XusbReport report);
}
