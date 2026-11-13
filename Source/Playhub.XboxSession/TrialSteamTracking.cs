// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

internal static class TrialSteamTracking
{
    internal static Process OpenParent(Process current)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var row = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        uint parentId = 0;
        for (bool available = Process32First(snapshot, ref row); available; available = Process32Next(snapshot, ref row))
        {
            if (row.ProcessId != current.Id) continue;
            parentId = row.ParentId;
            break;
        }
        if (parentId == 0) throw new InvalidOperationException("The Steam wrapper parent is unavailable.");
        var parent = Process.GetProcessById(checked((int)parentId));
        try
        {
            string expected = Path.Combine(AppContext.BaseDirectory, "Playhub.GameSession.exe");
            if (parent.SessionId != current.SessionId || parent.StartTime > current.StartTime || parent.HasExited
                || !string.Equals(parent.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The AppContainer sidecar requires its exact installed GameSession parent.");
            if (!OpenProcessToken(parent.Handle, 0x0008, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var own = WindowsIdentity.GetCurrent())
            {
                if (identity.User is null || own.User is null || !identity.User.Equals(own.User))
                    throw new InvalidOperationException("The GameSession parent belongs to another Windows user.");
            }
            return parent;
        }
        catch { parent.Dispose(); throw; }
    }

    internal static bool IsTracked(string log, Process parent, ulong gameId)
    {
        using var input = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        input.Position = Math.Max(0, input.Length - 256 * 1024);
        using var rows = new StreamReader(input);
        string? path = parent.MainModule?.FileName;
        if (path is null) return false;
        var start = parent.StartTime;
        var now = DateTime.Now;
        while (rows.ReadLine() is { } line)
            if (MatchesLog(line, parent.Id, path, gameId, start, now)) return !parent.HasExited;
        return false;
    }

    internal static bool MatchesLog(string row, int pid, string path, ulong gameId, DateTime started, DateTime now)
    {
        if (row.Length <= 22 || row[0] != '[' || row[20] != ']' || row[21] != ' '
            || !DateTime.TryParseExact(row.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var time)
            || time < started.AddSeconds(-2) || time > now.AddSeconds(2)) return false;
        string prefix = FormattableString.Invariant($"AppID {gameId} adding PID {pid} as a tracked process ");
        var message = row.AsSpan(22);
        if (!message.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var command = message[prefix.Length..].TrimStart('"');
        return command.StartsWith(path, StringComparison.OrdinalIgnoreCase)
            && (command.Length == path.Length || command[path.Length] is '"' or ' ');
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal nuint Heap;
        internal uint Module, Threads, ParentId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Exe;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW")] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW")] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint rights, out SafeAccessTokenHandle token);
}
