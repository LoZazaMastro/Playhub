using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

internal sealed class PresentMonWindowsCapture : IPresentMonPlatform
{
    internal const string SessionPrefix = "PlayhubFrames-";
    internal const int CaptureSeconds = 4;
    internal const string ExecutableSha256 = "93ff5a3a7adfe6bd904e6d0ead7080d2f4ca5a2e579011c915bd7a804df666f6";
    private const long ExecutableBytes = 988672;

    public IDisposable? PinExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return null;
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (IOException) { return null; }
        try
        {
            if (stream.Length == ExecutableBytes &&
                Convert.ToHexString(SHA256.HashData(stream)).Equals(ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return stream;
        }
        catch (IOException) { }
        stream.Dispose();
        return null;
    }

    public IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status)
    {
        status = FrameMeasurementStatus.TargetUnavailable;
        if (target.ProcessId <= 0 || target.CreationFileTimeUtc <= 0) return null;
        var process = OpenProcess(0x00101000, false, target.ProcessId); // SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid)
        {
            if (Marshal.GetLastWin32Error() == 5) status = FrameMeasurementStatus.PermissionDenied;
            process.Dispose();
            return null;
        }
        if (!GetProcessTimes(process, out long birth, out long exited, out _, out _) ||
            birth != target.CreationFileTimeUtc || exited != 0)
        {
            process.Dispose();
            return null;
        }
        try { return new TargetLease(process); }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public IPresentMonCapture Start(string path, int processId, string session)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string[] arguments = ["--process_id", processId.ToString(CultureInfo.InvariantCulture), "--session_name", session,
            "--timed", CaptureSeconds.ToString(CultureInfo.InvariantCulture), "--terminate_after_timed",
            "--output_stdout", "--no_console_stats", "--v1_metrics", "--no_track_input"];
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new CaptureProcess(Process.Start(start) ?? throw new InvalidOperationException("PresentMon did not start."));
    }

    public TraceStopResult Stop(string session)
    {
        if (!session.StartsWith(SessionPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(session.AsSpan(SessionPrefix.Length), "N", out _)) return TraceStopResult.Failed;
        // ControlTrace writes the logger name and optional log file name into this buffer.
        const int maxNameBytes = 1024 * sizeof(char);
        int headerBytes = Marshal.SizeOf<EventTraceProperties>();
        var buffer = new byte[headerBytes + maxNameBytes * 2];
        var properties = new EventTraceProperties
        {
            Wnode = new WnodeHeader { BufferSize = (uint)buffer.Length },
            LoggerNameOffset = (uint)headerBytes,
            LogFileNameOffset = (uint)(headerBytes + maxNameBytes)
        };
        MemoryMarshal.Write(buffer.AsSpan(), in properties);
        return ControlTraceW(0, session, buffer, 1) switch
        {
            0 => TraceStopResult.Stopped,
            4201 => TraceStopResult.NotFound, // ERROR_WMI_INSTANCE_NOT_FOUND
            _ => TraceStopResult.Failed
        };
    }

    private sealed class TargetLease : IPresentMonTargetLease
    {
        private readonly SafeWaitHandle _process;
        private readonly ProcessWaitHandle _exitEvent;
        private readonly RegisteredWaitHandle _registration;
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TargetLease(SafeWaitHandle process)
        {
            _process = process;
            _exitEvent = new ProcessWaitHandle(process);
            _registration = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => _exited.TrySetResult(),
                null, Timeout.InfiniteTimeSpan, executeOnlyOnce: true);
        }

        public bool HasExited => WaitForSingleObject(_process, 0) != 258; // WAIT_TIMEOUT means still running
        public Task Exited => _exited.Task;

        public void Dispose()
        {
            _registration.Unregister(null);
            _exitEvent.Dispose();
        }
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        internal ProcessWaitHandle(SafeWaitHandle process) => SafeWaitHandle = process;
    }

    private sealed class CaptureProcess : IPresentMonCapture
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _pipeLifetime = new();

        internal CaptureProcess(Process process)
        {
            _process = process;
            Completion = ReadCompletionAsync(process, _pipeLifetime.Token);
        }

        public bool HasExited => _process.HasExited;
        public Task<PresentMonCaptureOutput> Completion { get; }
        public void Kill() => _process.Kill(entireProcessTree: false);

        public void Dispose()
        {
            _pipeLifetime.Cancel();
            _pipeLifetime.Dispose();
            _process.Dispose();
        }

        private static async Task<PresentMonCaptureOutput> ReadCompletionAsync(Process process, CancellationToken token)
        {
            try
            {
                var stdout = DrainAsync(process.StandardOutput, 2 * 1024 * 1024, token);
                var stderr = DrainAsync(process.StandardError, 16 * 1024, token);
                await Task.WhenAll(process.WaitForExitAsync(token), stdout, stderr).ConfigureAwait(false);
                return new(process.ExitCode, stdout.Result.Text, stderr.Result.Text, stdout.Result.Truncated || stderr.Result.Truncated);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
            {
                return new(-1, string.Empty, string.Empty, false);
            }
        }
    }

    private static async Task<(string Text, bool Truncated)> DrainAsync(TextReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        bool truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            int keep = Math.Min(count, limit - output.Length);
            output.Append(buffer, 0, keep);
            if (keep != count) truncated = true;
        }
        return (output.ToString(), truncated);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WnodeHeader
    {
        public uint BufferSize, ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public nint LoggerThreadId;
        public uint LogFileNameOffset, LoggerNameOffset;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeWaitHandle process, out long birth, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeWaitHandle process, uint milliseconds);

    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ControlTraceW(ulong traceId, string name, [In, Out] byte[] properties, uint operation);
}
