using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

// Only the cleanup boundary uses this snapshot. No polling cache or service is
// introduced; unreadable identities are always preserved.
internal static class DeckyOrphanCleanup
{
    internal sealed record Identity(int Pid, int ParentPid, long Birth, string Path, string Sid, uint Session, string CommandLine);
    private sealed record Entry(int Pid, int ParentPid, string Name);
    internal static bool IsLoaderName(string name) => name.Equals("PluginLoader.exe", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PluginLoader_noconsole.exe", StringComparison.OrdinalIgnoreCase);
    private static bool IsLoaderParentName(string name) => System.IO.Path.GetFileNameWithoutExtension(name).StartsWith("PluginLoader", StringComparison.OrdinalIgnoreCase);

    internal static int Cleanup(IReadOnlyCollection<string> allowedPaths)
    {
        int count = 0;
        foreach (Identity candidate in Inspect(allowedPaths))
            if (TryTerminate(candidate, allowedPaths)) count++;
        return count;
    }

    internal static IReadOnlyList<Identity> Inspect(IReadOnlyCollection<string> allowedPaths)
    {
        Dictionary<int, Entry> snapshot = Snapshot();
        List<Identity> candidates = [];
        Dictionary<int, long?> births = [];
        using WindowsIdentity user = WindowsIdentity.GetCurrent();
        string? sid = user.User?.Value;
        if (sid is null || !ProcessIdToSessionId((uint)Environment.ProcessId, out uint session)) return candidates;
        foreach (Entry entry in snapshot.Values)
        {
            if (!IsLoaderName(entry.Name)) continue;
            // Most calls see only healthy loader trees. Read birth from held
            // query handles, once per PID, without command lines or token reads.
            if (snapshot.TryGetValue(entry.ParentPid, out Entry? parent) && IsLoaderParentName(parent.Name))
            {
                long? childBirth = Birth(entry.Pid, births), parentBirth = Birth(parent.Pid, births);
                if (childBirth is null || parentBirth is null || parentBirth <= childBirth) continue;
            }
            using SafeProcessHandle child = OpenProcess(0x1000, false, entry.Pid);
            Identity? identity = ReadIdentity(child, entry.Pid);
            if (identity is null || identity.ParentPid != entry.ParentPid || !IsOwnedFork(identity, allowedPaths, sid, session)) continue;
            if (ParentIsGoneOrUnrelated(identity)) candidates.Add(identity);
        }
        return candidates;
    }

    internal static bool IsOwnedFork(Identity identity, IReadOnlyCollection<string> paths, string sid, uint session)
    {
        if (identity.Birth <= 0 || identity.Sid != sid || identity.Session != session
            || !IsLoaderName(System.IO.Path.GetFileName(identity.Path))
            || !paths.Any(path => path.Equals(identity.Path, StringComparison.OrdinalIgnoreCase))) return false;
        string[] args = Arguments(identity.CommandLine);
        if (args.Length != 4 || !args[0].Equals(identity.Path, StringComparison.OrdinalIgnoreCase)
            || args[1] != "--multiprocessing-fork") return false;
        int? parent = null; long? pipe = null;
        foreach (string argument in args.Skip(2))
        {
            if (argument.StartsWith("parent_pid=", StringComparison.Ordinal) && parent is null
                && int.TryParse(argument[11..], out int value) && value > 0) parent = value;
            else if (argument.StartsWith("pipe_handle=", StringComparison.Ordinal) && pipe is null
                && long.TryParse(argument[12..], out long handle) && handle > 0) pipe = handle;
            else return false;
        }
        return parent == identity.ParentPid && pipe is not null;
    }

    // A parent PID may have been recycled. Birth is part of the relationship,
    // and a parent that exists but cannot be inspected is never assumed gone.
    private static bool ParentIsGoneOrUnrelated(Identity child)
    {
        using SafeProcessHandle parent = OpenProcess(0x1000, false, child.ParentPid);
        if (parent.IsInvalid) return Marshal.GetLastWin32Error() == 87; // nonexistent PID only
        if (!GetProcessTimes(parent, out long birth, out long exit, out _, out _)) return false;
        string? path = ImagePath(parent);
        return ParentEvidenceAllowsCleanup(child.Birth, birth, exit, path);
    }

    internal static bool ParentEvidenceAllowsCleanup(long childBirth, long? parentBirth, long parentExit, string? parentPath)
        => parentBirth is not null && (parentExit != 0 || parentBirth > childBirth
            || (parentPath is not null && !IsLoaderParentName(parentPath)));

    internal static bool TryTerminate(Identity candidate, IReadOnlyCollection<string> allowedPaths)
    {
        // Termination applies to this handle, never a second PID-based Kill.
        using SafeProcessHandle process = OpenProcess(0x1001, false, candidate.Pid);
        Identity? current = ReadIdentity(process, candidate.Pid);
        if (current is null || current != candidate || !ParentIsGoneOrUnrelated(current)) return false;
        using WindowsIdentity user = WindowsIdentity.GetCurrent();
        if (user.User?.Value is not string sid || !ProcessIdToSessionId((uint)Environment.ProcessId, out uint session)
            || !IsOwnedFork(current, allowedPaths, sid, session)) return false;
        return TerminateProcess(process, 0);
    }

    internal static Identity? ReadIdentity(SafeProcessHandle process, int pid)
    {
        if (process.IsInvalid || !GetProcessTimes(process, out long birth, out long exited, out _, out _) || exited != 0) return null;
        string? path = ImagePath(process), command = CommandLine(process), sid = OwnerSid(process);
        BasicInfo info = new();
        if (path is null || command is null || sid is null || !ProcessIdToSessionId((uint)pid, out uint session)
            || NtQueryBasic(process, 0, ref info, Marshal.SizeOf<BasicInfo>(), out _) != 0
            || info.Pid.ToInt64() != pid || info.ParentPid.ToInt64() > int.MaxValue) return null;
        return new(pid, info.ParentPid.ToInt32(), birth, path, sid, session, command);
    }

    private static long? Birth(int pid, Dictionary<int, long?> cache)
    {
        if (cache.TryGetValue(pid, out long? birth)) return birth;
        using SafeProcessHandle process = OpenProcess(0x1000, false, pid);
        birth = !process.IsInvalid && GetProcessTimes(process, out long created, out long exit, out _, out _) && exit == 0 ? created : null;
        cache[pid] = birth;
        return birth;
    }

    private static Dictionary<int, Entry> Snapshot()
    {
        Dictionary<int, Entry> result = [];
        using SafeFileHandle snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return result;
        ProcessEntry entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) return result;
        do
        {
            result[(int)entry.Pid] = new((int)entry.Pid, (int)entry.ParentPid, entry.Name);
            if (result.Count >= 16384) return []; // never use a truncated parent map
        }
        while (Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) return []; // unexpected enumeration failure
        return result;
    }

    private static string? ImagePath(SafeProcessHandle process)
    {
        StringBuilder text = new(32768); int length = text.Capacity;
        return QueryFullProcessImageName(process, 0, text, ref length) ? text.ToString() : null;
    }

    private static string? OwnerSid(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, 8, out SafeAccessTokenHandle token)) return null;
        using (token)
        {
            using WindowsIdentity identity = new(token.DangerousGetHandle());
            return identity.User?.Value;
        }
    }

    // Same NT command-line query already used by display recovery. Cap all
    // allocation/retries, and reject unexpected pointers or unavailable data.
    private static string? CommandLine(SafeProcessHandle process)
    {
        int size = 4096;
        for (int attempt = 0; attempt < 3 && size <= 131072; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryBuffer(process, 60, buffer, size, out int needed);
                if (status == 0)
                {
                    int length = (ushort)Marshal.ReadInt16(buffer);
                    nint text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    long offset = text.ToInt64() - buffer.ToInt64();
                    if (length == 0) return "";
                    return length % 2 == 0 && offset >= 0 && offset <= size - length ? Marshal.PtrToStringUni(text, length / 2) : null;
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
        if (string.IsNullOrWhiteSpace(command)) return [];
        nint argv = CommandLineToArgvW(command, out int count);
        if (argv == 0) return [];
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "").ToArray(); }
        finally { LocalFree(argv); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry { public uint Size, Usage, Pid; public nuint Heap; public uint Module, Threads, ParentPid; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public nint Exit, Peb, Affinity, Priority, Pid, ParentPid; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref int length);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")] private static extern int NtQueryBasic(SafeProcessHandle process, int kind, ref BasicInfo info, int size, out int needed);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")] private static extern int NtQueryBuffer(SafeProcessHandle process, int kind, nint buffer, int size, out int needed);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
