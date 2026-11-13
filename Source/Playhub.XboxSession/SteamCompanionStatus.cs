// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed class SteamCompanionStatus : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly Task _commands;
    private long _openRequested;

    internal SteamCompanionStatus(uint wrapperPid, long wrapperBirth, ulong gameId, uint gamePid, long gameBirth,
        Func<(int Scope, bool OverlayOpen, long KeyboardAt)> read)
    {
        string name = FormattableString.Invariant($"Playhub.XboxCompanion.{wrapperPid}.{wrapperBirth}");
        _worker = Serve(name, gameId, gamePid, gameBirth, read);
        _commands = ServeCommands(name + ".Open", read);
    }

    internal int TakeOpenRequest()
    {
        long requested = Interlocked.Exchange(ref _openRequested, 0);
        return requested != 0 && Environment.TickCount64 - requested <= 400 ? 1 : 0;
    }

    private async Task Serve(string name, ulong gameId, uint gamePid, long gameBirth,
        Func<(int Scope, bool OverlayOpen, long KeyboardAt)> read)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var state = read();
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
                    { GameId = gameId, GamePid = gamePid, GameBirth = gameBirth, state.Scope, state.OverlayOpen, state.KeyboardAt });
                try { await pipe.WriteAsync(payload, _stop.Token).ConfigureAwait(false); }
                catch (IOException) when (!_stop.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WSGM.PackagedLaunch.PackagedLaunchLog.Info("Steam companion status unavailable.");
        }
    }

    private async Task ServeCommands(string name, Func<(int Scope, bool OverlayOpen, long KeyboardAt)> read)
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            string agentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GamingMode", "GamingMode.exe");
            byte[] command = new byte[1];
            while (!_stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid)
                    || !IsAgent(pid, self.SessionId, agentPath)) continue;
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                bounded.CancelAfter(150);
                try
                {
                    if (await pipe.ReadAsync(command, bounded.Token).ConfigureAwait(false) == 1 && command[0] == 1)
                    {
                        var state = read();
                        if (state.Scope == 1 && (!state.OverlayOpen || SteamMirrorInputPresentationBridge.Available))
                            Interlocked.CompareExchange(ref _openRequested, Environment.TickCount64, 0);
                    }
                }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WSGM.PackagedLaunch.PackagedLaunchLog.Info("Steam companion Guide control unavailable.");
        }
    }

    private static bool IsAgent(uint pid, int session, string path)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return !process.HasExited && process.SessionId == session
                && string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OverflowException) { return false; }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint pid);

    public void Dispose()
    {
        _stop.Cancel();
        Task.WhenAll(_worker, _commands).GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
