namespace GamingMode.Services;

public sealed record SteamModeCommand(long Revision, string Token, string Kind);
internal sealed record SteamModeCommandWait(SteamModeCommand? Command, Task<bool> Completed);

// A delivered desktop exit remains outstanding until Steam's frontend confirms
// native mode 7. Reading a command is deliberately not an acknowledgement.
internal sealed class SteamModeCommandState
{
    private readonly object _sync = new();
    private readonly Func<long> _clock;
    private long _revision = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private SteamModeCommand? _pending;
    private TaskCompletionSource<bool>? _completion;
    private bool _deliveredExit;
    private long _exitDeadline;
    internal SteamModeCommandState(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    internal SteamModeCommandWait RequestDesktop()
    {
        lock (_sync)
        {
            _completion?.TrySetResult(false);
            _pending = Next("exit");
            _exitDeadline = _clock() + 4000;
            _completion = Completion();
            return new(_pending, _completion.Task);
        }
    }

    internal SteamModeCommand? Read()
    {
        lock (_sync)
        {
            if (_pending?.Kind == "exit" && !_deliveredExit && _clock() > _exitDeadline)
            {
                _pending = null;
                _completion?.TrySetResult(false);
                _completion = null;
            }
            if (_pending?.Kind == "exit") _deliveredExit = true;
            return _pending;
        }
    }

    internal SteamModeCommandWait BeginOpen()
    {
        lock (_sync)
        {
            _completion?.TrySetResult(false);
            _pending = null;
            _completion = null;
            if (!_deliveredExit) return new(null, Task.FromResult(true));
            _pending = Next("barrier");
            _completion = Completion();
            return new(_pending, _completion.Task);
        }
    }

    internal bool Acknowledge(long revision, string token, bool completed)
    {
        lock (_sync)
        {
            if (_pending is null || _pending.Revision != revision
                || !string.Equals(_pending.Token, token, StringComparison.Ordinal)) return false;
            // A short native readback can miss an exit which is still being
            // applied. Keep this exact command/waiter alive for the next poll;
            // a negative observation is not a terminal transition failure.
            if (!completed) { _deliveredExit = true; return true; }
            _deliveredExit = false;
            _pending = null;
            _completion?.TrySetResult(completed);
            _completion = null;
            return true;
        }
    }

    private SteamModeCommand Next(string kind) => new(
        _revision = Math.Max(_revision + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        Guid.NewGuid().ToString("N"), kind);
    private static TaskCompletionSource<bool> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
