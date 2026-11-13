namespace GamingMode.Services;

internal enum SteamModeSettlementResult { Complete, Cancelled, Expired, Superseded }

internal static class SteamModeSettlement
{
    // Settlement and themed startup share ONE deadline. An incomplete native
    // observation must not end the user's request before that transition does.
    internal static async Task<SteamModeSettlementResult> WaitAsync(Task<bool> completion,
        Func<bool> canProceed, long deadlineTick, Func<long>? clock = null, Func<int, Task>? delay = null)
    {
        clock ??= () => Environment.TickCount64;
        delay ??= milliseconds => Task.Delay(milliseconds);
        while (clock() < deadlineTick)
        {
            if (!canProceed()) return SteamModeSettlementResult.Cancelled;
            if (clock() >= deadlineTick) break; // A slow ownership getter does not extend the budget.
            if (completion.IsCompleted)
            {
                try { return await completion.ConfigureAwait(false) ? SteamModeSettlementResult.Complete : SteamModeSettlementResult.Superseded; }
                catch { return SteamModeSettlementResult.Superseded; }
            }
            await Task.WhenAny(completion, delay((int)Math.Clamp(deadlineTick - clock(), 1, 50))).ConfigureAwait(false);
        }
        return SteamModeSettlementResult.Expired;
    }
}
