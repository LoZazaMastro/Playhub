using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Playhub.DeckyHost;

namespace GamingMode.Services;

internal static partial class NativeSteamOverlayHost
{
    internal sealed record Identity(uint Pid, long Birth);
    internal static Identity? Find(string root, uint companionPid, long companionBirth, ulong gameId, int session)
    {
        Identity? match = null;
        if (!ValveImageTrust.Verify(Path.Combine(root, "steam.exe"))
            || !ValveImageTrust.Verify(Path.Combine(root, "gameoverlayui64.exe"))) return null;
        foreach (var process in Process.GetProcessesByName("gameoverlayui64"))
        {
            using (process)
            {
                try
                {
                    var host = DeckyProcessTree.Read(process.Id);
                    var steam = host is null ? null : DeckyProcessTree.Read(host.ParentPid);
                    var companion = DeckyProcessTree.Read(checked((int)companionPid));
                    if (host is null || steam is null || companion is null || host.Session != session
                        || steam.Session != session || companion.Session != session || host.Sid != companion.Sid || steam.Sid != host.Sid
                        || companion.Birth != companionBirth || host.Birth < companionBirth || steam.Birth > host.Birth
                        || !host.Path.Equals(Path.Combine(root, "gameoverlayui64.exe"), StringComparison.OrdinalIgnoreCase)
                        || !steam.Path.Equals(Path.Combine(root, "steam.exe"), StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = OpenProcess(0x1000, false, process.Id);
                    if (handle.IsInvalid || ReadCommandLine(handle) is not { } command
                        || !Matches(Arguments(command), host.Path, companionPid, (uint)steam.Pid, gameId)
                        || process.HasExited || process.StartTime.ToFileTimeUtc() != host.Birth) continue;
                    if (match is not null) return null;
                    match = new((uint)process.Id, host.Birth);
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException
                    or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or OverflowException) { }
            }
        }
        return match;
    }

    private static string? ReadCommandLine(SafeProcessHandle process)
    {
        int size = 4096;
        for (int attempt = 0; attempt < 3 && size <= 131072; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryInformationProcess(process, 60, buffer, size, out int needed);
                if (status == 0)
                {
                    int length = (ushort)Marshal.ReadInt16(buffer);
                    nint text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    long offset = text.ToInt64() - buffer.ToInt64();
                    return length > 0 && length % 2 == 0 && offset >= 0 && offset <= size - length
                        ? Marshal.PtrToStringUni(text, length / 2) : null;
                }
                if (needed <= size || needed > 131072) return null;
                size = needed;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    private static string[] Arguments(string command)
    {
        nint argv = CommandLineToArgvW(command, out int count);
        if (argv == 0) return [];
        try
        {
            return count is > 0 and <= 256
                ? Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "").ToArray()
                : [];
        }
        finally { LocalFree(argv); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint rights, bool inherit, int pid);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle process, int infoClass, nint data, int size, out int needed);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint data);
}
