using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

internal sealed class PresentMonLiveWindowsCapture : IPresentMonLivePlatform
{
    private readonly PresentMonWindowsCapture _shared = new();
    public IDisposable? PinExecutable(string path) => _shared.PinExecutable(path);
    public IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status) => _shared.OpenTarget(target, out status);
    public TraceStopResult Stop(string session) => _shared.Stop(session);

    internal static string[] Arguments(int pid, string session) =>
        ["--process_id", pid.ToString(CultureInfo.InvariantCulture), "--session_name", session,
            "--terminate_on_proc_exit", "--output_stdout", "--no_console_stats", "--v1_metrics", "--no_track_input", "--no_track_gpu",
            "--live_output_ms", LivePerformanceCadence.Milliseconds.ToString(CultureInfo.InvariantCulture)];

    public IPresentMonCapture Start(string path, int pid, string session, Action<string> line)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)!,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in Arguments(pid, session)) start.ArgumentList.Add(argument);
        return new LiveProcess(Process.Start(start) ?? throw new InvalidOperationException("The live frame collector did not start."), line);
    }

    private sealed class LiveProcess : IPresentMonCapture, IPresentMonLiveHealth, IPresentMonLiveResources
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _stop = new();
        private readonly long _birth;
        private int _failure = -1;
        internal LiveProcess(Process process, Action<string> line)
        {
            _process = process;
            _birth = process.StartTime.ToFileTimeUtc();
            Completion = ObserveAsync(line, _stop.Token);
        }
        public bool HasExited => _process.HasExited;
        public Task<PresentMonCaptureOutput> Completion { get; }
        public FrameMeasurementStatus? Failure => Volatile.Read(ref _failure) is int value && value >= 0 ? (FrameMeasurementStatus)value : null;
        public void Kill() => _process.Kill(entireProcessTree: false);
        public void Dispose() { _stop.Cancel(); _stop.Dispose(); _process.Dispose(); }
        public LiveCollectorResources? ReadResources()
        {
            try
            {
                var memory = new ProcessMemory { Size = (uint)Marshal.SizeOf<ProcessMemory>() };
                if (!GetProcessTimes(_process.Handle, out long birth, out long exited, out long kernel, out long user)
                    || birth != _birth || exited != 0 || !K32GetProcessMemoryInfo(_process.Handle, ref memory, memory.Size)) return null;
                return new(_process.Id, birth, (kernel + user) / 10000.0, checked((long)memory.WorkingSet));
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct ProcessMemory
        {
            internal uint Size, PageFaultCount;
            internal nuint PeakWorkingSet, WorkingSet, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long birth, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool K32GetProcessMemoryInfo(nint process, ref ProcessMemory memory, uint size);

        private async Task<PresentMonCaptureOutput> ObserveAsync(Action<string> line, CancellationToken cancellation)
        {
            try
            {
                var stdout = PumpLines(_process.StandardOutput, line, cancellation,
                    () => Interlocked.CompareExchange(ref _failure, (int)FrameMeasurementStatus.Failed, -1));
                var stderr = ReadError(_process.StandardError, cancellation, (text, truncated) =>
                {
                    if (truncated) Interlocked.CompareExchange(ref _failure, (int)FrameMeasurementStatus.Failed, -1);
                    if (new PresentMonCaptureOutput(0, "", text, false).LostEvents)
                        Interlocked.CompareExchange(ref _failure, (int)FrameMeasurementStatus.NoData, -1);
                });
                await Task.WhenAll(_process.WaitForExitAsync(cancellation), stdout, stderr).ConfigureAwait(false);
                return new(_process.ExitCode, "", stderr.Result.Text, stdout.Result || stderr.Result.Truncated);
            }
            catch (OperationCanceledException) { return new(-1, "", "", false); }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            { return new(-1, "", error.Message, true); }
        }
    }

    internal static async Task<bool> PumpLines(TextReader reader, Action<string> line, CancellationToken cancellation, Action? invalid = null)
    {
        char[] buffer = new char[4096];
        var pending = new StringBuilder(512);
        bool oversized = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
        {
            for (int index = 0; index < count; index++)
            {
                char value = buffer[index];
                if (value == '\n')
                {
                    if (!oversized) line(pending.ToString().TrimEnd('\r'));
                    pending.Clear();
                }
                else if (!oversized)
                {
                    if (pending.Length == 16384) { oversized = true; pending.Clear(); invalid?.Invoke(); }
                    else pending.Append(value);
                }
            }
        }
        return oversized || pending.Length != 0;
    }

    private static async Task<(string Text, bool Truncated)> ReadError(TextReader reader, CancellationToken cancellation, Action<string, bool> updated)
    {
        var result = new StringBuilder();
        char[] buffer = new char[1024];
        bool truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
        {
            int accepted = Math.Min(count, 16384 - result.Length);
            result.Append(buffer, 0, accepted);
            truncated |= accepted != count;
            updated(result.ToString(), truncated);
        }
        return (result.ToString(), truncated);
    }
}
