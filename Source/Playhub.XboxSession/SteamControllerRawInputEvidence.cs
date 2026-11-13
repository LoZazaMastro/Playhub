// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal static class SteamControllerRawInputEvidence
{
    private const uint Hid = 2, DeviceName = 0x20000007, DeviceInfo = 0x2000000b, Error = uint.MaxValue;
    // Valve documents the synthetic path format in Steam Input Gamepad Emulation Best Practices.
    private static readonly Regex ValvePipe = new("^" + Regex.Escape(@"\\.\pipe\HID#VID_045E&PID_028E&IG_00#")
        + @"([0-9a-f]{1,4})&([0-9a-f]{1,4})&([0-9a-f]{1,16})#([0-3])#([0-9]{1,10})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool Matches(string path, SteamControllerSourceProbe.Slot slot, int processId)
    {
        if (processId <= 0 || slot.Handle == 0 || path.Length > 512) return false;
        var candidate = ValvePipe.Match(path);
        return candidate.Success
            && ushort.TryParse(candidate.Groups[1].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort vendor)
            && vendor == 0x28DE
            && ushort.TryParse(candidate.Groups[2].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort product)
            && product == slot.Product
            && ulong.TryParse(candidate.Groups[3].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong handle)
            && handle == slot.Handle
            && candidate.Groups[4].ValueSpan[0] - '0' == slot.Index
            && int.TryParse(candidate.Groups[5].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            && pid == processId;
    }

    internal static bool? HasExactDevice(SteamControllerSourceProbe.Slot slot, int processId)
    {
        var device = ExactDevice(slot, processId);
        return device is { } found ? found != 0 : null;
    }

    // Null is an incomplete/ambiguous observation, zero a completed absence.
    // A positive handle belongs to this process's exact Steam virtual source,
    // not the receiver's PnP presence or an independently emulated Xbox pad.
    internal static nint? ExactDevice(SteamControllerSourceProbe.Slot slot, int processId)
    {
        if (processId <= 0 || slot.Index is < 0 or > 3 || slot.Product is not (0x1302 or 0x1304) || slot.Handle == 0) return null;
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RawDevice>();
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count > 512) return null;
        if (count == 0) return 0;
        var devices = new RawDevice[count];
        uint read = GetRawInputDeviceList(devices, ref count, size);
        if (read == Error || read > devices.Length) return null;
        int matches = 0;
        nint result = 0;
        for (int i = 0; i < read; i++)
        {
            var device = devices[i];
            if (device.Type != Hid) continue;
            uint chars = 0;
            if (GetRawInputDeviceInfoW(device.Handle, DeviceName, null, ref chars) == Error) return null;
            if (chars is < 2 or > 512) continue;
            var path = new StringBuilder((int)chars);
            if (GetRawInputDeviceInfoW(device.Handle, DeviceName, path, ref chars) == Error) return null;
            if (!Matches(path.ToString(), slot, processId)) continue;
            if (++matches != 1) return null;
            var info = new RawDeviceInfo { Size = (uint)Marshal.SizeOf<RawDeviceInfo>() };
            uint bytes = info.Size;
            if (GetRawInputDeviceInfoW(device.Handle, DeviceInfo, ref info, ref bytes) == Error
                || !ValveDeviceInfo(info.Type, info.Vendor, info.Product) || device.Handle is 0 or -1) return null;
            result = device.Handle;
        }
        return result;
    }

    internal static bool ValveDeviceInfo(uint type, uint vendor, uint product) => type == Hid && vendor == 0x28DE && product == 0x11FF;
    internal static bool SameDevice(nint expected, nint? observed) => expected is not (0 or -1) && observed == expected;

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RawDeviceInfo
    {
        [FieldOffset(0)] internal uint Size;
        [FieldOffset(4)] internal uint Type;
        [FieldOffset(8)] internal uint Vendor;
        [FieldOffset(12)] internal uint Product;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawDevice
    {
        internal readonly nint Handle;
        internal readonly uint Type;
    }

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList([Out] RawDevice[]? devices, ref uint count, uint size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetRawInputDeviceInfoW(nint device, uint command, StringBuilder? data, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetRawInputDeviceInfoW(nint device, uint command, ref RawDeviceInfo data, ref uint size);
}
