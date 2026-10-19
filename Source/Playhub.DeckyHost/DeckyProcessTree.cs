using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Playhub.DeckyHost;

internal static class DeckyProcessTree
{
    internal sealed record Identity(int Pid, int ParentPid, long Birth, string Path, string Sid, uint Session);
    internal static bool IsLoaderPath(string path) => Path.GetFileName(path).Equals("PluginLoader.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path).Equals("PluginLoader_noconsole.exe", StringComparison.OrdinalIgnoreCase);

    internal static Identity? Read(int pid)
    {
        using var handle = OpenProcess(0x1000, false, pid);
        return Read(handle, pid);
    }

    internal static bool HasLiveLoader(string loader)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(loader)))
            using (process)
            {
                Identity? identity = Read(process.Id);
                // Unreadable same-name processes prevent deleting extraction.
                if (identity is null || identity.Path.Equals(loader, StringComparison.OrdinalIgnoreCase)) return true;
            }
        return false;
    }

    internal static Identity? FindRoot(int listenerPid, string loader)
    {
        Identity? root = Read(listenerPid);
        using var user = WindowsIdentity.GetCurrent();
        if (root is null || !root.Path.Equals(Path.GetFullPath(loader), StringComparison.OrdinalIgnoreCase)
            || root.Sid != user.User?.Value || !ProcessIdToSessionId((uint)Environment.ProcessId, out uint session)
            || root.Session != session) return null;
        var visited = new HashSet<int>();
        while (visited.Add(root.Pid))
        {
            Identity? parent = Read(root.ParentPid);
            if (parent is null || !SameOwner(parent, root) || parent.Birth > root.Birth) return root;
            root = parent;
        }
        return null;
    }

    internal static int StopInstallation(string servicesDirectory)
    {
        servicesDirectory = Path.GetFullPath(servicesDirectory);
        using var currentUser = WindowsIdentity.GetCurrent();
        if (currentUser.User?.Value is not string sid || !ProcessIdToSessionId((uint)Environment.ProcessId, out uint session)) return 0;
        var identities = Snapshot().Values.Where(identity => identity.Sid == sid && identity.Session == session
            && Path.GetDirectoryName(identity.Path)!.Equals(servicesDirectory, StringComparison.OrdinalIgnoreCase)).ToDictionary(identity => identity.Pid);
        int stopped = 0;
        foreach (var root in identities.Values.Where(identity => !identities.TryGetValue(identity.ParentPid, out var parent)
            || !SameOwner(identity, parent) || parent.Birth > identity.Birth))
        {
            using var handle = OpenProcess(0x101001, false, root.Pid);
            if (Read(handle, root.Pid) != root) continue;
            stopped += StopDescendants(root);
            // Give a onefile parent its own cleanup opportunity before bounded
            // fallback termination of this exact retained handle.
            if (WaitForSingleObject(handle, 1500) == 0) continue;
            if (Read(handle, root.Pid) == root && TerminateProcess(handle, 0)) stopped++;
            if (WaitForSingleObject(handle, 10000) != 0) throw new IOException("Decky is still closing. Try again after it has stopped.");
        }
        return stopped;
    }

    // Verify the whole lineage using birth times, image paths, owner SID and
    // session, then re-read the same held termination handle. External launchers
    // and upload programs cannot enter this set, even when Decky started them.
    internal static int Depth(Identity candidate, Identity root, IReadOnlyDictionary<int, Identity> tree)
    {
        if (candidate.Pid == root.Pid || candidate.Birth < root.Birth || !SameOwner(candidate, root)) return 0;
        int depth = 0;
        var visited = new HashSet<int>();
        Identity current = candidate;
        while (visited.Add(current.Pid) && ++depth < 1024)
        {
            if (current.ParentPid == root.Pid) return current.Birth >= root.Birth ? depth : 0;
            if (!tree.TryGetValue(current.ParentPid, out var parent) || !SameOwner(parent, root) || parent.Birth > current.Birth) return 0;
            current = parent;
        }
        return 0;
    }

    private static bool SameOwner(Identity a, Identity b) => a.Path.Equals(b.Path, StringComparison.OrdinalIgnoreCase)
        && a.Sid == b.Sid && a.Session == b.Session;

    internal static int StopDescendants(Identity root)
    {
        // Retain the root handle so PID reuse or a new install cannot be mistaken
        // for this onefile parent. Leave that parent alive to remove _MEI itself.
        using var parent = OpenProcess(0x1000, false, root.Pid);
        if (Read(parent, root.Pid) != root) return 0;
        var budget = Stopwatch.StartNew();
        var targets = new List<(SafeProcessHandle Handle, Identity Identity, int Depth, bool Suspended)>();
        try
        {
            // Freeze only verified owned loader descendants, shallowest first,
            // before collecting the final tree. This closes the main-process
            // fork race without suspending the onefile parent or external jobs.
            // Three bounded snapshots also collect children already being born.
            for (int pass = 0; pass < 3 && budget.ElapsedMilliseconds < 1200; pass++)
            {
                var tree = Snapshot(budget);
                var known = targets.Select(target => target.Identity.Pid).ToHashSet();
                var found = tree.Values.Select(identity => (Identity: identity, Depth: Depth(identity, root, tree)))
                    .Where(target => target.Depth > 0 && !known.Contains(target.Identity.Pid)).OrderBy(target => target.Depth).ToArray();
                if (found.Length == 0) break;
                foreach (var target in found)
                {
                    var handle = OpenProcess(0x101801, false, target.Identity.Pid); // terminate, query, synchronize, suspend/resume
                    if (Read(handle, target.Identity.Pid) != target.Identity) { handle.Dispose(); continue; }
                    bool suspended = NtSuspendProcess(handle) == 0;
                    targets.Add((handle, target.Identity, target.Depth, suspended));
                }
            }
            int stopped = 0;
            foreach (var target in targets.OrderByDescending(target => target.Depth))
                if (Read(target.Handle, target.Identity.Pid) == target.Identity && TerminateProcess(target.Handle, 0)) stopped++;
            foreach (var target in targets)
            {
                int remaining = 1500 - (int)budget.ElapsedMilliseconds;
                if (remaining <= 0) break;
                WaitForSingleObject(target.Handle, (uint)remaining);
            }
            return stopped;
        }
        finally
        {
            foreach (var target in targets)
            {
                // A failed termination must never strand a live process frozen.
                if (target.Suspended && Read(target.Handle, target.Identity.Pid) == target.Identity) NtResumeProcess(target.Handle);
                target.Handle.Dispose();
            }
        }
    }

    internal static Dictionary<int, Identity> Snapshot(Stopwatch? budget = null)
    {
        var result = new Dictionary<int, Identity>();
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return result;
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) return result;
        int count = 0;
        do
        {
            if (++count > 16384 || budget?.ElapsedMilliseconds >= 1200) return new();
            if (IsLoaderPath(entry.Name))
            {
                var identity = Read((int)entry.Pid);
                if (identity is not null && identity.ParentPid == entry.ParentPid) result[identity.Pid] = identity;
            }
        } while (Process32Next(snapshot, ref entry));
        return Marshal.GetLastWin32Error() == 18 ? result : new();
    }

    internal static Identity? Read(SafeProcessHandle handle, int pid)
    {
        if (handle.IsInvalid || !GetProcessTimes(handle, out long birth, out long exited, out _, out _) || exited != 0) return null;
        var image = new StringBuilder(32768); int length = image.Capacity;
        var info = new BasicInfo();
        if (!QueryFullProcessImageName(handle, 0, image, ref length) || !ProcessIdToSessionId((uint)pid, out uint session)
            || NtQueryBasic(handle, 0, ref info, Marshal.SizeOf<BasicInfo>(), out _) != 0
            || info.Pid.ToInt64() != pid || info.ParentPid.ToInt64() > int.MaxValue
            || !OpenProcessToken(handle, 8, out var token)) return null;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User?.Value is string sid ? new(pid, info.ParentPid.ToInt32(), birth, image.ToString(), sid, session) : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry { public uint Size, Usage, Pid; public nuint Heap; public uint Module, Threads, ParentPid; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public nint Exit, Peb, Affinity, Priority, Pid, ParentPid; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle handle, out long birth, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder image, ref int length);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")] private static extern int NtQueryBasic(SafeProcessHandle handle, int kind, ref BasicInfo info, int size, out int needed);
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(SafeProcessHandle handle);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(SafeProcessHandle handle);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(SafeProcessHandle handle, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(SafeProcessHandle handle, uint code);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
