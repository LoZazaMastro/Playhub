using System;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

// Coalesce a burst of OS events into one wake-up. No timer runs when idle;
// the bounded timeout is only a fallback for drivers that omit notifications.
internal sealed class BackgroundWorkSignal
{
	private readonly SemaphoreSlim _pending = new(0, 1);

	public void Notify()
	{
		try { _pending.Release(); }
		catch (SemaphoreFullException) { }
	}

	public Task<bool> WaitAsync(TimeSpan fallback, CancellationToken token)
		=> _pending.WaitAsync(fallback, token);
}
