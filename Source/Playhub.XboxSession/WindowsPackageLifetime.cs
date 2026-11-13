// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

internal sealed class WindowsPackageLifetime : IPackageDebugReference
{
    public int Enable(string package) => Use(settings => settings.EnableDebugging(package, null, 0));
    public int Disable(string package) => Use(settings => settings.DisableDebugging(package));

    private static int Use(Func<IPackageDebugSettings, int> operation)
    {
        var settings = (IPackageDebugSettings)(object)new PackageDebugSettings();
        try { return operation(settings); }
        finally { Marshal.FinalReleaseComObject(settings); }
    }

    internal static LeaseProcessState Observe(LeaseProcess process)
    {
        using var handle = OpenProcess(0x1000 | 0x100000, false, process.Pid);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error() == 87 ? LeaseProcessState.Gone : LeaseProcessState.Unknown;
        return WaitForSingleObject(handle, 0) switch
        {
            0 => LeaseProcessState.Gone,
            258 when GetProcessTimes(handle, out long birth, out _, out _, out _) =>
                birth == process.Creation ? LeaseProcessState.Running : LeaseProcessState.Gone,
            _ => LeaseProcessState.Unknown
        };
    }

    // Use the OS's exact boot event as a recovery boundary. Refuse to assume that a debugger
    // reference still belongs to this trial after Windows has restarted or its log was cleared.
    internal static string BootMarker()
    {
        const string xpath = "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=12]]";
        using var query = EvtQuery(0, "System", xpath, 1 | 0x200);
        if (query.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!EvtNext(query, 1, out nint first, 250, 0, out uint returned) || returned != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows boot identity is unavailable.");
        using var boot = new EventHandle(first);
        _ = EvtRender(0, boot, 1, 0, 0, out uint size, out _);
        if (size is < 2 or > 65536) throw new InvalidOperationException("Windows boot event is invalid.");
        nint buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (!EvtRender(0, boot, 1, size, buffer, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var system = XDocument.Parse(Marshal.PtrToStringUni(buffer)!).Root?.Element(ns + "System");
            var time = (string?)system?.Element(ns + "TimeCreated")?.Attribute("SystemTime");
            var record = (string?)system?.Element(ns + "EventRecordID");
            return !string.IsNullOrWhiteSpace(time) && !string.IsNullOrWhiteSpace(record)
                ? record + "/" + time : throw new InvalidOperationException("Windows boot event has no stable identity.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [ComImport, Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D")]
    private sealed class PackageDebugSettings { }

    [ComImport, Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPackageDebugSettings
    {
        [PreserveSig] int EnableDebugging([MarshalAs(UnmanagedType.LPWStr)] string package,
            [MarshalAs(UnmanagedType.LPWStr)] string? debugger, nint environment);
        [PreserveSig] int DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] string package);
    }

    private sealed class EventHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public EventHandle() : base(true) { }
        internal EventHandle(nint existing) : this() => SetHandle(existing);
        protected override bool ReleaseHandle() => EvtClose(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint timeout);
    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern EventHandle EvtQuery(nint session, string path, string query, uint flags);
    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern bool EvtNext(EventHandle query, uint count, out nint events, uint timeout, uint flags, out uint returned);
    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern bool EvtRender(nint context, EventHandle fragment, uint flags, uint size, nint buffer, out uint used, out uint count);
    [DllImport("wevtapi.dll")] private static extern bool EvtClose(nint handle);
}
