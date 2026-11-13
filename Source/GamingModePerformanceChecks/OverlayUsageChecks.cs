using System.Runtime.InteropServices;
using GamingMode.Services;

internal static class OverlayUsageChecks
{
    // PDH_FMT_COUNTERVALUE_ITEM_W is a name pointer followed by a status DWORD,
    // four bytes of union alignment, and the formatted double on Windows x64.
    private const int ValueSize = 16;
    private static readonly int ItemSize = IntPtr.Size + ValueSize;

    internal static void ValidValues()
    {
        using var array = new CounterArray((0, 2.5), (0xc0000bc6, 999), (0, 7.5), (1, 50));
        Equal(10d, array.Sum(out bool available));
        Equal(true, available);
    }

    internal static void Availability()
    {
        using var invalid = new CounterArray((0xc0000bc6, 999), (1, 4));
        Equal(0d, invalid.Sum(out bool unavailable));
        Equal(false, unavailable);
        using var zero = new CounterArray((0, 0));
        Equal(0d, zero.Sum(out bool available));
        Equal(true, available);
        Equal(0d, OverlaySystemUsage.SumFormattedArray(0, 0, 0, out available));
        Equal(false, available);
    }

    internal static void InvalidBuffers()
    {
        using var array = new CounterArray((0, 1), (0, 2));
        foreach ((nint pointer, uint size, uint count) in new (nint, uint, uint)[]
        {
            (0, array.Size, 2),
            (array.Pointer, array.Size - 1, 2),
            (array.Pointer, 0, 1),
            (array.Pointer, array.Size, uint.MaxValue),
            (array.Pointer, uint.MaxValue, 1),
            (array.Pointer, array.Size, 0)
        })
        {
            Equal(0d, OverlaySystemUsage.SumFormattedArray(pointer, size, count, out bool available));
            Equal(false, available);
        }
    }

    internal static void FloatingPointValues()
    {
        using var ordered = new CounterArray((0, 1e16), (0, -1e16), (0, 1));
        Equal(1d, ordered.Sum(out _));
        using var missing = new CounterArray((0, double.NaN), (0, 1));
        Equal(true, double.IsNaN(missing.Sum(out bool available)));
        Equal(true, available);
    }

    internal static void BoundedAllocations()
    {
        using var array = new CounterArray(Enumerable.Repeat((0u, 0.125d), 1024).ToArray());
        for (int i = 0; i < 100; i++) array.Sum(out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double total = 0;
        for (int i = 0; i < 1000; i++) total += array.Sum(out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(128000d, total);
        if (allocated > 4096) throw new Exception($"Allocated {allocated} bytes to sum a native array");
        Console.WriteLine($"METRIC PDH 1000 totals of 1024 GPU engines allocated {allocated} bytes");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}; got {actual}");
    }

    private sealed class CounterArray : IDisposable
    {
        internal nint Pointer { get; }
        internal uint Size { get; }
        private readonly uint _count;

        internal CounterArray(params (uint Status, double Value)[] values)
        {
            _count = (uint)values.Length;
            Size = checked((uint)(values.Length * ItemSize));
            Pointer = Marshal.AllocHGlobal((int)Size);
            for (int i = 0; i < values.Length; i++)
            {
                nint entry = Pointer + i * ItemSize;
                // An intentionally unreadable name pointer proves the sum does
                // not depend on names (the named disk path is separate).
                Marshal.WriteIntPtr(entry, (nint)1);
                Marshal.WriteInt32(entry, IntPtr.Size, unchecked((int)values[i].Status));
                Marshal.WriteInt32(entry, IntPtr.Size + 4, 0);
                Marshal.WriteInt64(entry, IntPtr.Size + 8, BitConverter.DoubleToInt64Bits(values[i].Value));
            }
        }

        internal double Sum(out bool available) =>
            OverlaySystemUsage.SumFormattedArray(Pointer, Size, _count, out available);

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
