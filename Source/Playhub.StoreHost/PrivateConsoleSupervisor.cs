// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Playhub.StoreLibrary;

// Runs only in our disposable --supervise process, never inside WinUI/Agent.
// CTRL_C_EVENT is broadcast ONLY to its verified private console and owned job.
internal static class PrivateConsoleSupervisor
{
    internal sealed record Outcome(int ExitCode, bool StopRequested, bool Drained, bool Forced);
    private delegate bool Handler(uint type);
    private static readonly Handler CtrlHandler = _ => true;
    internal static async Task<Outcome> RunAsync(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, string workingDirectory, Func<string, Task> stdout, Func<string, Task> stderr, Task checkpointRequest, CancellationToken cancellationToken, Func<Task>? rootExited = null)
    {
        var saved = new[] { GetStdHandle(-10), GetStdHandle(-11), GetStdHandle(-12) };
        // CREATE_NO_WINDOW already supplies a private console without a window.
        // Reallocating that console can unexpectedly launch Windows Terminal.
        EnsurePrivateHiddenConsole();
        for (var i = 0; i < saved.Length; i++) SetStdHandle(-10 - i, saved[i]);
        if (!SetConsoleCtrlHandler(CtrlHandler, true)) throw new Win32Exception(Marshal.GetLastWin32Error(), "register-private-handler");
        using var job = new SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "create-owned-job");
        var limits = new ExtendedLimit { Basic = new BasicLimit { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE: only our suspended child tree.
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimit>())) throw new Win32Exception(Marshal.GetLastWin32Error(), "set-owned-job");
        using var output = Pipe(); using var errors = Pipe();
        var nulSecurity = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        using var nul = CreateFile("NUL", 0x80000000, 3, ref nulSecurity, 3, 0, IntPtr.Zero);
        if (nul.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "open-null-input");
        var start = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x101, Show = 0, Input = nul.DangerousGetHandle(), Output = output.Write.DangerousGetHandle(), Error = errors.Write.DangerousGetHandle() };
        var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
        var env = string.Join('\0', environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Key + "=" + e.Value)) + "\0\0";
        if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x404, env, workingDirectory, ref start, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "create-suspended-tool");
        using var process = new SafeFileHandle(info.Process, true); using var thread = new SafeFileHandle(info.Thread, true);
        var assigned = AssignProcessToJobObject(job, process);
        if (!assigned) { var error = Marshal.GetLastWin32Error(); TerminateProcess(process, 201); throw new Win32Exception(error, "assign-owned-job"); }
        output.Write.Dispose(); errors.Write.Dispose();
        if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error(), "resume-owned-tool");
        var drainOut = Drain(output.Read, stdout); var drainError = Drain(errors.Read, stderr);
        var stopped = false; var forced = false; var stopAt = Stopwatch.GetTimestamp(); var rootReported = false;
        try
        {
            while (true)
            {
                var accounting = new Accounting();
                if (!QueryInformationJobObject(job, 1, ref accounting, Marshal.SizeOf<Accounting>(), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "read-owned-job");
                if (!rootReported && GetExitCodeProcess(process, out var rootCode) && rootCode != 259)
                { rootReported = true; if (rootExited != null) await rootExited(); }
                if (accounting.Active == 0) break;
                if (!stopped && (checkpointRequest.IsCompleted || cancellationToken.IsCancellationRequested))
                {
                    if (!ConsoleContainsOnlyOwned(job)) throw new InvalidOperationException("supervisor_console_ownership_lost");
                    stopped = true; stopAt = Stopwatch.GetTimestamp();
                    if (!GenerateConsoleCtrlEvent(0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "signal-private-console");
                }
                // A maintenance/pause timeout is not permission to force-stop a
                // downloader. The host reports blocked quiescence; this retained
                // supervisor continues observing its exact job until real exit.
                await Task.Delay(20, CancellationToken.None);
            }
            await Task.WhenAll(drainOut, drainError).WaitAsync(TimeSpan.FromSeconds(10));
            if (!GetExitCodeProcess(process, out var code) || code == 259) return new(203, stopped, false, forced);
            var final = new Accounting();
            var drained = QueryInformationJobObject(job, 1, ref final, Marshal.SizeOf<Accounting>(), IntPtr.Zero) && final.Active == 0;
            return new(unchecked((int)code), stopped, drained, forced);
        }
        finally { SetConsoleCtrlHandler(CtrlHandler, false); FreeConsole(); }
    }
    private static void EnsurePrivateHiddenConsole()
    {
        if (IsSingleOwnHiddenConsole()) return;
        // Detach THIS disposable process only. An inherited/shared/visible
        // console is never hidden, attached to, signaled or closed by us.
        FreeConsole();
        try
        {
            // Windows 11 24H2+: allocate the session without creating any window,
            // including when Windows Terminal is the default console host.
            var options = new ConsoleOptions { Mode = 2, UseShowWindow = true, ShowWindow = 0 };
            var error = AllocConsoleWithOptions(ref options, out var result);
            if (error < 0) throw new Win32Exception(error, "allocate-private-hidden-console");
            if (result != 1) throw new InvalidOperationException("private_console_not_created");
        }
        catch (EntryPointNotFoundException)
        {
            // Legacy Windows has no no-window allocation API. AllocConsole's
            // successful result belongs only to us; never hide an inherited HWND.
            if (!AllocConsole()) throw new Win32Exception(Marshal.GetLastWin32Error(), "allocate-private-console");
            if (!IsSingleOwnConsole()) throw new InvalidOperationException("private_console_ownership_lost");
            var window = GetConsoleWindow();
            if (window != IntPtr.Zero) ShowWindow(window, 0);
        }
        if (!IsSingleOwnHiddenConsole()) throw new InvalidOperationException("private_console_not_hidden");
    }
    private static bool IsSingleOwnConsole()
    {
        var ids = new uint[2];
        return GetConsoleProcessList(ids, (uint)ids.Length) == 1 && ids[0] == (uint)Environment.ProcessId;
    }
    private static bool IsSingleOwnHiddenConsole()
    {
        if (!IsSingleOwnConsole()) return false;
        var window = GetConsoleWindow();
        return window == IntPtr.Zero || !IsWindowVisible(window);
    }
    private static bool ConsoleContainsOnlyOwned(SafeFileHandle job)
    {
        var ids = new uint[128]; var count = GetConsoleProcessList(ids, (uint)ids.Length);
        if (count == 0 || count > ids.Length) return false;
        for (var i = 0; i < count; i++)
        {
            if (ids[i] == Environment.ProcessId) continue;
            using var process = new SafeFileHandle(OpenProcess(0x1000, false, ids[i]), true);
            if (process.IsInvalid || !IsProcessInJob(process, job, out var owned) || !owned) return false;
        }
        return true;
    }
    private static async Task Drain(SafeFileHandle handle, Func<string, Task> callback)
    {
        using var file = new FileStream(handle, FileAccess.Read, 4096, false);
        using var reader = new StreamReader(file, Encoding.UTF8, false, 4096);
        while (await reader.ReadLineAsync() is { } line) { if (line.Length <= 65536) await callback(line); }
    }
    private sealed class Pipes(SafeFileHandle read, SafeFileHandle write) : IDisposable { public SafeFileHandle Read = read; public SafeFileHandle Write = write; public void Dispose() { Read.Dispose(); Write.Dispose(); } }
    private static Pipes Pipe()
    {
        var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        if (!CreatePipe(out var r, out var w, ref security, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetHandleInformation(r, 1, 0)) { r.Dispose(); w.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        return new(r, w);
    }
    internal static string Quote(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("invalid_argument");
        var b = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value) { if (c == '\\') { slashes++; continue; } if (c == '"') b.Append('\\', slashes * 2 + 1); else b.Append('\\', slashes); b.Append(c); slashes = 0; }
        return b.Append('\\', slashes * 2).Append('"').ToString();
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int Size; public IntPtr Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags; public short Show, ReservedSize; public IntPtr ReservedData, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit { public long ProcessTime, JobTime; public uint Flags; public UIntPtr Minimum, Maximum; public uint ActiveLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong R, W, O, RB, WB, OB; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting { public long User, Kernel, PeriodUser, PeriodKernel; public uint Faults, Total, Active, Terminated; }
    [StructLayout(LayoutKind.Sequential)] private struct ConsoleOptions { public int Mode; [MarshalAs(UnmanagedType.Bool)] public bool UseShowWindow; public ushort ShowWindow; }
    [DllImport("kernel32", ExactSpelling = true)] private static extern int AllocConsoleWithOptions(ref ConsoleOptions options, out int result);
    [DllImport("kernel32", SetLastError = true)] private static extern bool AllocConsole();
    [DllImport("kernel32")] private static extern bool FreeConsole();
    [DllImport("kernel32")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32")] private static extern IntPtr GetStdHandle(int index);
    [DllImport("kernel32")] private static extern bool SetStdHandle(int index, IntPtr value);
    [DllImport("user32")] private static extern bool ShowWindow(IntPtr hwnd, int show);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("kernel32", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint type, uint group);
    [DllImport("kernel32", SetLastError = true)] private static extern uint GetConsoleProcessList([Out] uint[] list, uint count);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref ExtendedLimit limits, int bytes);
    [DllImport("kernel32", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int type, ref Accounting data, int bytes, IntPtr returned);
    [DllImport("kernel32", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32", SetLastError = true)] private static extern bool IsProcessInJob(SafeFileHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32", SetLastError = true)] private static extern bool TerminateProcess(SafeFileHandle process, uint code);
    [DllImport("kernel32", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
    [DllImport("kernel32", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint bytes);
    [DllImport("kernel32", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string file, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string file, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, string environment, string directory, ref StartupInfo startup, out ProcessInfo info);
}
