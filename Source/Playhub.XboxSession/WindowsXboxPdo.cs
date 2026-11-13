// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Runtime.InteropServices;
using System.Text;

internal sealed class WindowsXboxPdo
{
    private const uint PresentEnumerator = 0x101;
    private const uint BufferSmall = 0x1A;
    private const string DeviceId = @"USB\VID_045E&PID_7EB0";
    private readonly string _approvedBus;
    private readonly byte[] _propertyBytes = new byte[8192];

    internal WindowsXboxPdo(string approvedBus) => _approvedBus = approvedBus;

    internal static string? SinglePresentBus() =>
        ListPresent("ViGEmBus", 0x10000142) is [var bus] ? bus : null;

    internal bool SyntheticAbsent() => SyntheticDevices() is { Length: 0 };

    internal string? Resolve(uint serial)
    {
        if (SyntheticDevices() is not [var id] || !StillOwns(id, serial)) return null;
        return id;
    }

    internal bool StillOwns(string instance, uint serial)
    {
        if (serial == 0 || string.IsNullOrWhiteSpace(_approvedBus) || _approvedBus.Length > 4096
            || !instance.StartsWith(DeviceId + "\\", StringComparison.OrdinalIgnoreCase)
            || CM_Locate_DevNodeW(out uint node, instance, 0) != 0
            || CM_Locate_DevNodeW(out uint bus, _approvedBus, 0) != 0
            || CM_Get_Parent(out uint parent, node, 0) != 0 || parent != bus)
            return false;

        return StringProperty(bus, 5, 1) is ["VIGEMBUS"]
            && StringProperty(node, 5, 1) is ["XUSB22"]
            && StringProperty(node, 2, 7)?.Any(h => h == DeviceId || h.StartsWith(DeviceId + "&", StringComparison.Ordinal)) == true
            && UIntProperty(node, 0x1D) == serial && UIntProperty(node, 0x11) == serial;
    }

    private static string[]? SyntheticDevices()
    {
        var ids = ListPresent(DeviceId, PresentEnumerator);
        return ids?.All(id => id.StartsWith(DeviceId + "\\", StringComparison.OrdinalIgnoreCase)) == true ? ids : null;
    }

    private static string[]? ListPresent(string filter, uint flags)
    {
        // No service-generated devnodes or phantom matches. Enumerate only
        // present instances of the exact custom Xbox USB device ID.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out uint length, filter, flags) != 0
                || length == 0 || length > 8192) return null;
            var result = new char[length];
            uint status = CM_Get_Device_ID_ListW(filter, result, length, flags);
            if (status == BufferSmall) continue;
            if (status != 0 || result[^1] != '\0') return null;
            return new string(result).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return null;
    }

    private uint? UIntProperty(uint node, uint key)
    {
        uint length = sizeof(uint);
        return CM_Get_DevNode_Registry_PropertyW(node, key, out uint kind, _propertyBytes, ref length, 0) == 0
            && kind == 4 && length == sizeof(uint) ? BitConverter.ToUInt32(_propertyBytes) : null;
    }

    private string[]? StringProperty(uint node, uint key, uint expectedType)
    {
        uint length = (uint)_propertyBytes.Length;
        if (CM_Get_DevNode_Registry_PropertyW(node, key, out uint kind, _propertyBytes, ref length, 0) != 0
            || kind != expectedType || length < 2 || length > _propertyBytes.Length || (length & 1) != 0) return null;
        string value = Encoding.Unicode.GetString(_propertyBytes, 0, (int)length).ToUpperInvariant();
        if (!value.EndsWith(expectedType == 7 ? "\0\0" : "\0", StringComparison.Ordinal)) return null;
        return value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] list, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint key,
        out uint propertyType, [Out] byte[] value, ref uint length, uint flags);
}
