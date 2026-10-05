using System.Diagnostics;

namespace Playhub.Services;

internal static class SingleInstanceRedirect
{
    internal static async Task<bool> WaitAsync(Task redirect, Func<bool> ownerExited, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        _ = redirect.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        while (!redirect.IsCompleted)
        {
            if (ownerExited()) return false;
            if (elapsed.Elapsed >= timeout) throw new TimeoutException("The existing Playhub instance did not acknowledge activation.");
            await Task.WhenAny(redirect, Task.Delay(100));
        }
        if (ownerExited()) return false;
        await redirect;
        return true;
    }
}
