using System.IO;
using GamingMode.Services;

internal static class SteamModeSettlementTests
{
    internal static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        long now = 0;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool stillPendingAfterThreeSeconds = false;
        var settled = await SteamModeSettlement.WaitAsync(pending.Task, () => true, 8000, () => now, ms => {
            now += ms;
            if (now == 3100) stillPendingAfterThreeSeconds = !pending.Task.IsCompleted;
            if (now == 4000) pending.SetResult(true);
            return Task.CompletedTask;
        });
        Check(settled == SteamModeSettlementResult.Complete && now == 4000 && stillPendingAfterThreeSeconds,
            "A real native settlement at four seconds completes the original request inside the same eight-second cover budget");
        Check(await SteamModeSettlement.WaitAsync(Task.FromResult(true), () => false, 8000, () => 100)
            == SteamModeSettlementResult.Cancelled, "Cancellation wins over an already available native acknowledgment");
        Check(await SteamModeSettlement.WaitAsync(Task.FromResult(false), () => true, 8000, () => 100)
            == SteamModeSettlementResult.Superseded, "A revoked command cannot dispatch the URI");
        Check(await SteamModeSettlement.WaitAsync(Task.FromException<bool>(new IOException("lost")), () => true, 8000, () => 100)
            == SteamModeSettlementResult.Superseded, "A failed command task does not fabricate native settlement");
        now = 0; int waits = 0;
        var never = new TaskCompletionSource<bool>();
        Check(await SteamModeSettlement.WaitAsync(never.Task, () => true, 8000, () => now,
            ms => { waits++; now += ms; return Task.CompletedTask; }) == SteamModeSettlementResult.Expired
            && now == 8000 && waits == 160, "The wait shares the cover's original deadline and adds no new timeout afterward");
        now = 0;
        Check(await SteamModeSettlement.WaitAsync(never.Task, () => now < 2000, 8000, () => now,
            ms => { now += ms; return Task.CompletedTask; }) == SteamModeSettlementResult.Cancelled && now == 2000,
            "Foreground departure cancels a pending queue before a later acknowledgment");
        now = 0;
        Check(await SteamModeSettlement.WaitAsync(Task.FromResult(true), () => { now = 9000; return true; }, 8000, () => now)
            == SteamModeSettlementResult.Expired, "A slow successful ownership getter cannot extend the transition budget");
        Check(await SteamModeSettlement.WaitAsync(Task.FromResult(true), () => true, 8000, () => 8000)
            == SteamModeSettlementResult.Expired, "An acknowledgment on or after expiry cannot launch late");
        Console.WriteLine($"PASS {checks} queued Steam settlement deadline/cancellation checks");
    }
}
