using System.Threading;

namespace PlayhubSetup;

internal sealed class SetupSession
{
    private int _started;
    private int _result;
    private int _finished;

    public bool TryStart() => Interlocked.CompareExchange(ref _started, 1, 0) == 0;

    public void Complete(bool succeeded) => Volatile.Write(ref _result, succeeded ? 1 : -1);

    public bool TryFinish(out bool succeeded)
    {
        var result = Volatile.Read(ref _result);
        succeeded = result == 1;
        return result != 0 && Interlocked.CompareExchange(ref _finished, 1, 0) == 0;
    }
}
