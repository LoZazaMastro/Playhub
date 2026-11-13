using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PlayhubSetup;

internal enum BundledProcessSafety { Unknown, Clear, Active }
internal sealed record BundledProcessFact(bool Known, bool Running, uint Pid = 0, long Birth = 0,
    uint Session = 0, string UserSid = "", string Image = "");

// Read-only veto. Unknown is never interpreted as no game. No stop/kill,
// debugger, window, focus, input, environment or loader operation occurs here.
internal static class BundledPluginProcessGuard
{
    private static readonly string[] Names = ["Playhub.GameSession", "Playhub.XboxSession"];
    internal static BundledProcessSafety Classify(bool enumerationKnown, uint session, string user,
        IReadOnlyCollection<string> expected, IEnumerable<BundledProcessFact> processes)
    {
        if (!enumerationKnown || string.IsNullOrWhiteSpace(user) || expected.Count != Names.Length
            || expected.Any(p => !Path.IsPathFullyQualified(p))) return BundledProcessSafety.Unknown;
        bool unknown = false;
        foreach (var fact in processes)
        {
            if (!fact.Known) { unknown = true; continue; }
            if (!fact.Running) continue;
            if (fact.Pid == 0 || fact.Birth <= 0 || string.IsNullOrWhiteSpace(fact.UserSid)
                || !Path.IsPathFullyQualified(fact.Image)) { unknown = true; continue; }
            if (fact.Session == session && fact.UserSid == user
                && expected.Contains(fact.Image, StringComparer.OrdinalIgnoreCase)) return BundledProcessSafety.Active;
        }
        return unknown ? BundledProcessSafety.Unknown : BundledProcessSafety.Clear;
    }

    internal static BundledProcessSafety Read(string installation)
    {
        var found = new List<Process>();
        try
        {
            if (!Path.IsPathFullyQualified(installation)) return BundledProcessSafety.Unknown;
            string root = Path.GetFullPath(installation);
            // A lexical path mismatch is not proof of another installation when
            // an ancestor is a junction. Defer such installations conservatively.
            for (string? directory = root; directory is not null; directory = Path.GetDirectoryName(directory))
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return BundledProcessSafety.Unknown;
            string[] expected = Names.Select(n => Path.Combine(root, n + ".exe")).ToArray();
            if (expected.Any(p => !File.Exists(p) || (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
                return BundledProcessSafety.Unknown;
            using var self = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            string? user = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(user) || !ProcessIdToSessionId((uint)self.Id, out uint session)) return BundledProcessSafety.Unknown;
            foreach (string name in Names) found.AddRange(Process.GetProcessesByName(name));
            var facts = found.Select(ReadProcess).ToArray();
            return Classify(true, session, user, expected, facts);
        }
        catch { return BundledProcessSafety.Unknown; }
        finally { foreach (var process in found) process.Dispose(); }
    }

    private static BundledProcessFact ReadProcess(Process process)
    {
        try
        {
            uint pid = checked((uint)process.Id);
            using var handle = OpenProcess(0x00101000, false, pid); // SYNCHRONIZE + limited query.
            if (handle.IsInvalid) return new(false, false);
            uint first = WaitForSingleObject(handle, 0);
            if (first == 0) return new(true, false);
            if (first != 258 || GetProcessId(handle) != pid
                || !GetProcessTimes(handle, out long birth, out _, out _, out _)
                || !ProcessIdToSessionId(pid, out uint session)) return new(false, false);
            var text = new StringBuilder(32768); uint size = (uint)text.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, text, ref size) || !Path.IsPathFullyQualified(text.ToString())) return new(false, false);
            string image = Path.GetFullPath(text.ToString());
            if (!OpenProcessToken(handle, 8, out var token)) return new(false, false);
            using (token)
            {
                GetTokenInformation(token, 1, IntPtr.Zero, 0, out uint bytes); // TokenUser, bounded sizing.
                if (Marshal.GetLastWin32Error() != 122 || bytes < IntPtr.Size || bytes > 65536) return new(false, false);
                IntPtr buffer = Marshal.AllocHGlobal(checked((int)bytes));
                try
                {
                    if (!GetTokenInformation(token, 1, buffer, bytes, out _)) return new(false, false);
                    string sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                    if (WaitForSingleObject(handle, 0) != 258 || GetProcessId(handle) != pid
                        || !GetProcessTimes(handle, out long after, out _, out _, out _) || after != birth
                        || !ProcessIdToSessionId(pid, out uint again) || again != session) return new(false, false);
                    return new(true, true, pid, birth, session, sid, image);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        catch { return new(false, false); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder image, ref uint length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, IntPtr value, uint bytes, out uint needed);
}
