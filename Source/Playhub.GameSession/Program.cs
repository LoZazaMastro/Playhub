using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Playhub.GameSession;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 3 && args[0] == "--gdk-game-proof")
            {
                var plan = GdkLaunchProof.Resolve(args[1], args[2], new RegisteredPackageCatalog(physicalPaths: true));
                var rawArguments = args.Length > 3 ? args[3] : "";
                if (args.Length > 4) rawArguments += " " + string.Join(" ", args.Skip(4).Select(GameLifetime.Quote));
                GdkLaunchProof.RejectRunningGame(plan);
                UwpSessionLog.Write($"gdk-game-proof validated package={plan.PackageFullName} helper={plan.Helper} game={plan.Game}");
                return GameLifetime.Run(plan.Game, Array.Empty<string>(), plan.Directory, rawArguments);
            }
            if (args.Length >= 3 && args[0] == "--gdk-proof")
            {
                var plan = GdkLaunchProof.Resolve(args[1], args[2], new RegisteredPackageCatalog());
                var rawArguments = args.Length > 3 ? args[3] : "";
                if (args.Length > 4) rawArguments += " " + string.Join(" ", args.Skip(4).Select(GameLifetime.Quote));
                using var running = new WindowsUwpRuntime().FindGame(args[1].Split('!')[0], Path.GetFileName(plan.Game));
                if (running is not null) throw new InvalidOperationException("This Xbox game is already running; another launch was not started.");
                UwpSessionLog.Write($"gdk-proof validated package={plan.PackageFullName} helper={plan.Helper} game={plan.Game}");
                return GameLifetime.Run(plan.Helper, Array.Empty<string>(), plan.Directory, rawArguments);
            }
            if (args.Length >= 3 && args[0] == "--uwp")
            {
                var arguments = args.Length > 3 ? args[3] : "";
                if (args.Length > 4) arguments += " " + string.Join(" ", args.Skip(4).Select(GameLifetime.Quote));
                return UwpLifetime.Run(args[1], args[2], arguments, new WindowsUwpRuntime());
            }
            if (args.Length < 2 || args[0] != "--game")
                throw new ArgumentException("Expected --game followed by the game executable.");
            return GameLifetime.Run(Path.GetFullPath(args[1]), args.Skip(2));
        }
        catch (Exception ex)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "logs");
            try
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "game-session.log"), $"{DateTimeOffset.Now:O} {ex}\n");
            }
            catch { }
            MessageBox(IntPtr.Zero, ex.Message, "Playhub", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}

internal static class GameLifetime
{
    // Keep Steam's original child alive until the launched process tree exits.
    // Never attach unrelated processes or set KILL_ON_JOB_CLOSE: closing this
    // helper must not terminate the game or other applications.
    internal static int Run(string executable, IEnumerable<string> arguments, string? directory = null, string? rawArguments = null)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("Game executable not found.", executable);
        using var job = new Microsoft.Win32.SafeHandles.SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
        if (!string.IsNullOrEmpty(rawArguments)) command.Append(' ').Append(rawArguments);
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 4,
                IntPtr.Zero, directory ?? Environment.CurrentDirectory, ref startup, out var process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(process.Process, true);
        using var thread = new Microsoft.Win32.SafeHandles.SafeFileHandle(process.Thread, true);
        // Assignment before ResumeThread prevents losing a fast bootstrapper's child.
        var assigned = AssignProcessToJobObject(job, handle);
        if (rawArguments is not null) UwpSessionLog.Write($"gdk-proof bootstrap pid={process.ProcessId} assigned-job={assigned} package={PackageIdentity(handle)}");
        if (ResumeThread(thread) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!assigned)
        {
            // Some externally imposed jobs prohibit nesting. Still follow the
            // exact process handle; never guess by executable name or PID reuse.
            WaitForSingleObject(handle, uint.MaxValue);
        }
        else
        {
            while (true)
            {
                if (!QueryInformationJobObject(job, 1, out var accounting, Marshal.SizeOf<JobAccounting>(), IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (accounting.ActiveProcesses == 0) break;
                Thread.Sleep(250);
            }
        }
        return GetExitCodeProcess(handle, out var code) ? unchecked((int)code) : 1;
    }

    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static string PackageIdentity(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        uint length = 0;
        var error = GetPackageFullName(handle, ref length, null);
        if (error != 122 || length > 32768) return "query-error-" + error;
        var name = new StringBuilder((int)length);
        error = GetPackageFullName(handle, ref length, name);
        return error == 0 ? name.ToString() : "query-error-" + error;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobAccounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string app, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(Microsoft.Win32.SafeHandles.SafeFileHandle job, Microsoft.Win32.SafeHandles.SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(Microsoft.Win32.SafeHandles.SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(Microsoft.Win32.SafeHandles.SafeFileHandle job, int type,
        out JobAccounting information, int size, IntPtr returnedLength);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(Microsoft.Win32.SafeHandles.SafeFileHandle process, out uint code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFullName(Microsoft.Win32.SafeHandles.SafeFileHandle process, ref uint length, StringBuilder? name);
}
