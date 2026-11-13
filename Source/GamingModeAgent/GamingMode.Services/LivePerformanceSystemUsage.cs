using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// The HUD owns one small PDH query: no disk/network counters, process
// enumeration, WMI, external tools or hardware-driver polling on a HUD tick.
internal interface ILivePerformanceTargetUsageRuntime { LivePerformanceHudUsage? ReadUsage(int processId); }

internal sealed class LivePerformanceSystemUsage : IDisposable
{
    private readonly object _gate = new();
    private nint _query, _cpu, _gpu, _vram, _buffer;
    private uint _capacity;
    private bool _primed;
    internal LivePerformanceHudUsage Read(int processId = 0)
    {
        lock (_gate) {
            double? cpu = null, gpu = null; long? vram = null, totalVram = null;
            if (_query == 0 && Native.PdhOpenQuery(null, 0, out _query) == 0) {
                if (Native.PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Time", 0, out _cpu) != 0) _cpu = 0;
                if (Native.PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", 0, out _gpu) != 0) _gpu = 0;
                if (Native.PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out _vram) != 0) _vram = 0;
            }
            if (_query != 0 && Native.PdhCollectQueryData(_query) == 0) {
                if (_primed) {
                    if (_cpu != 0 && Native.PdhGetFormattedCounterValue(_cpu, 0x200, out _, out var value) == 0
                        && value.Status is 0 or 1 && double.IsFinite(value.Value)) cpu = Math.Clamp(value.Value, 0, 100);
                    gpu = ReadGpu(processId, out var adapter);
                    if (adapter is not null) { vram = ReadDedicated(adapter); totalVram = DedicatedCapacity(adapter); }
                }
                _primed = true;
            } else ResetCore();
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            bool hasMemory = Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical <= long.MaxValue
                && memory.AvailablePhysical <= memory.TotalPhysical;
            return new(cpu, gpu, hasMemory ? (long)(memory.TotalPhysical - memory.AvailablePhysical) : null,
                hasMemory ? (long)memory.TotalPhysical : null, vram, totalVram);
        }
    }

    private double? ReadGpu(int processId, out string? adapter)
    {
        adapter = null;
        if (_gpu == 0) return null;
        uint size = _capacity, count = 0;
        uint result = Native.PdhGetFormattedCounterArray(_gpu, 0x200 | 0x8000, ref size, ref count, _buffer);
        if (result == 0x800007D2 && size is > 0 and <= 4 * 1024 * 1024) { // PDH_MORE_DATA
            _buffer = Marshal.ReAllocHGlobal(_buffer, new nint(size)); _capacity = size;
            result = Native.PdhGetFormattedCounterArray(_gpu, 0x200 | 0x8000, ref size, ref count, _buffer);
        }
        if (result != 0 || size > _capacity) return null;
        adapter = SelectAdapter(ReadNamedValues(_buffer, size, count, false), processId);
        return SumGpu(_buffer, size, count);
    }

    internal static double? SumGpu(nint buffer, uint size, uint count)
    {
        int stride = IntPtr.Size + 16;
        if (buffer == 0 || size > int.MaxValue || count == 0 || count > size / (uint)stride) return null;
        double total = 0;
        bool valid = false;
        for (int index = 0; index < count; index++) {
            nint value = buffer + index * stride + IntPtr.Size;
            int status = Marshal.ReadInt32(value);
            if (status is not (0 or 1)) continue;
            double number = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value, 8));
            if (!double.IsFinite(number) || number < 0) continue;
            total += number; valid = true;
        }
        return valid ? Math.Clamp(total, 0, 100) : null;
    }

    // Adapter-wide dedicated allocation, not current-process usage and not shared system RAM.
    private long? ReadDedicated(string adapter)
    {
        if (_vram == 0) return null;
        uint size = _capacity, count = 0;
        uint result = Native.PdhGetFormattedCounterArray(_vram, 0x400, ref size, ref count, _buffer);
        if (result == 0x800007D2 && size is > 0 and <= 4 * 1024 * 1024) {
            _buffer = Marshal.ReAllocHGlobal(_buffer, new nint(size)); _capacity = size;
            result = Native.PdhGetFormattedCounterArray(_vram, 0x400, ref size, ref count, _buffer);
        }
        if (result != 0 || size > _capacity) return null;
        return ReadDedicatedValue(_buffer, size, count, adapter);
    }
    internal static long? ReadDedicatedValue(nint buffer, uint size, uint count, string adapter)
    {
        int stride = IntPtr.Size + 16;
        if (buffer == 0 || size > int.MaxValue || count == 0 || count > size / (uint)stride) return null;
        long? found = null;
        for (int i = 0; i < count; i++) {
            nint item = buffer + i * stride, value = item + IntPtr.Size;
            if (Marshal.ReadInt32(value) is not (0 or 1)) continue;
            string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
            if (!string.Equals(name, adapter, StringComparison.OrdinalIgnoreCase)) continue;
            long bytes = Marshal.ReadInt64(value, 8);
            if (bytes < 0 || found.HasValue) return null;
            found = bytes;
        }
        return found;
    }
    internal readonly record struct NamedValue(string Name, double Value);
    internal static IEnumerable<NamedValue> ReadNamedValues(nint buffer, uint size, uint count, bool large)
    {
        int stride = IntPtr.Size + 16;
        if (buffer == 0 || size > int.MaxValue || count == 0 || count > size / (uint)stride) yield break;
        for (int i = 0; i < count; i++) {
            nint item = buffer + i * stride, value = item + IntPtr.Size;
            if (Marshal.ReadInt32(value) is not (0 or 1)) continue;
            string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
            double number = large ? Marshal.ReadInt64(value, 8) : BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value, 8));
            if (name is { Length: > 0 and <= 256 } && double.IsFinite(number) && number >= 0) yield return new(name, number);
        }
    }
    private static readonly Regex EngineIdentity = new(@"^pid_(\d+)_(luid_0x[0-9a-f]{8}_0x[0-9a-f]{8}_phys_\d+)_eng_\d+_engtype_3D$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static string? SelectAdapter(IEnumerable<NamedValue> values, int processId)
    {
        if (processId <= 0) return null;
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values) {
            var m = EngineIdentity.Match(item.Name);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int pid) || pid != processId || !double.IsFinite(item.Value) || item.Value < 0) continue;
            string key = m.Groups[2].Value;
            totals[key] = totals.GetValueOrDefault(key) + item.Value;
        }
        var ordered = totals.OrderByDescending(x => x.Value).ToArray();
        return ordered.Length == 1 || ordered.Length > 1 && ordered[0].Value > ordered[1].Value ? ordered[0].Key : null;
    }
    private string? _capacityAdapter;
    private long? _dedicatedCapacity;
    private long _capacityReadAt;
    private long? DedicatedCapacity(string adapter)
    {
        long now = Environment.TickCount64;
        if (adapter == _capacityAdapter && now - _capacityReadAt is >= 0 and < 5000) return _dedicatedCapacity;
        _capacityAdapter = adapter; _capacityReadAt = now; _dedicatedCapacity = null;
        // Linked physical nodes cannot be compared to an aggregate DXGI capacity.
        if (!adapter.EndsWith("_phys_0", StringComparison.OrdinalIgnoreCase)) return null;
        nint factory = 0;
        try {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref iid, out factory) < 0 || factory == 0) return null;
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint i = 0; i < 32; i++) {
                if (enumerate(factory, i, out nint native) < 0 || native == 0) break;
                try {
                    if (Method<GetDesc>(native, 10)(native, out var desc) < 0 || (desc.Flags & 2) != 0) continue;
                    string key = $"luid_0x{unchecked((uint)desc.LuidHigh):x8}_0x{desc.LuidLow:x8}_phys_0";
                    if (string.Equals(key, adapter, StringComparison.OrdinalIgnoreCase) && desc.DedicatedVideoMemory > 0 && (ulong)desc.DedicatedVideoMemory <= long.MaxValue) {
                        _dedicatedCapacity = (long)desc.DedicatedVideoMemory; break;
                    }
                } finally { Marshal.Release(native); }
            }
        } catch { _dedicatedCapacity = null; }
        finally { if (factory != 0) Marshal.Release(factory); }
        return _dedicatedCapacity;
    }
    private static T Method<T>(nint instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc(nint self, out AdapterDesc desc);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDesc {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);

    internal void Reset() { lock (_gate) ResetCore(); }
    private void ResetCore()
    {
        if (_query != 0) Native.PdhCloseQuery(_query);
        if (_buffer != 0) Marshal.FreeHGlobal(_buffer);
        _query = _cpu = _gpu = _vram = _buffer = 0; _capacity = 0; _primed = false;
    }
    public void Dispose() => Reset();

    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status, Padding; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    private static class Native
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhOpenQuery(string? source, nint user, out nint query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhAddEnglishCounter(nint query, string path, nint user, out nint counter);
        [DllImport("pdh.dll")] internal static extern uint PdhCollectQueryData(nint query);
        [DllImport("pdh.dll")] internal static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, ref uint count, nint items);
        [DllImport("pdh.dll")] internal static extern uint PdhCloseQuery(nint query);
        [DllImport("kernel32.dll")] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    }
}
