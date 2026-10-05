namespace Playhub.StandaloneHost.Migration;

/// <summary>Fixture-ready handover state machine. Dry run is the default. This
/// class has no Steam, Decky, process, registry, autostart or network operations.</summary>
public sealed class MigrationCoordinator
{
    private readonly MigrationStorage storage;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> completedPlans = new(StringComparer.Ordinal);
    private readonly TimeSpan timeout;
    public MigrationCoordinator(MigrationStorage storage, TimeSpan? operationTimeout = null)
    {
        this.storage = storage;
        timeout = operationTimeout ?? TimeSpan.FromSeconds(15);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
    }

    public async Task<HandoverResult> ExecuteAsync(MigrationPlan plan, IHostHandover? runtime = null,
        bool apply = false, CancellationToken cancellationToken = default)
    {
        storage.VerifyCurrent(plan);
        if (!apply) return new("dry-run", null, null, false, false, null);
        ArgumentNullException.ThrowIfNull(runtime);
        if (!await gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("A migration is already running in this coordinator.");
        SnapshotReceipt? snapshot = null;
        string? journal = null;
        int step = 0;
        bool retiring = false;
        var context = new HandoverContext(Guid.NewGuid().ToString("N"), plan.Id, plan.CandidateInstanceId);
        void Record(string state) => storage.AppendJournal(journal!, ++step, state, context, snapshot);
        void TryRecord(string state) { try { Record(state); } catch { /* Journal faults cannot skip compensation. */ } }
        try
        {
            if (completedPlans.Contains(plan.Id)) throw new InvalidOperationException("This coordinator already completed the plan.");
            snapshot = storage.CreateSnapshot(plan);
            journal = storage.CreateJournal(context.TransactionId);
            Record("snapshot-verified");
            var ready = await Call(ct => runtime.ProbeCandidateAsync(context, ct), cancellationToken);
            Validate(ready, plan, active: false);
            storage.VerifyCurrent(plan);
            Record("candidate-ready-no-writer");
            Record("previous-owner-retirement-requested");
            retiring = true; // Even a lost reply makes the retirement state uncertain.
            var stopped = await Call(ct => runtime.RetirePreviousOwnerAsync(context, ct), cancellationToken);
            if (!stopped.FrontendUnmounted || !stopped.BackendStopped || !stopped.WritersDrained)
                throw new InvalidOperationException("Previous owner did not confirm full retirement.");
            Record("previous-owner-retired");
            await Call(async ct => { await runtime.ActivateCandidateAsync(context, ct); return true; }, cancellationToken);
            Validate(await Call(ct => runtime.ProbeCandidateAsync(context, ct), cancellationToken), plan, active: true);
            Record("standalone-active");
            completedPlans.Add(plan.Id);
            return new("active", snapshot, journal, true, true, null);
        }
        catch (Exception failure)
        {
            // Never overwrite source preferences on rollback: writes made since the
            // snapshot belong to the user and must survive a transport failure.
            if (journal is null) throw;
            TryRecord("rollback-requested");
            bool stoppedConfirmed = false;
            try
            {
                var stopped = await Call(ct => runtime.StopCandidateAsync(context, ct), CancellationToken.None);
                stoppedConfirmed = stopped.FrontendUnmounted && stopped.BackendStopped && stopped.WritersDrained;
            }
            catch { /* Uncertain candidate state must never start a second writer. */ }
            if (!stoppedConfirmed)
            {
                TryRecord("blocked-candidate-stop-unconfirmed");
                return new("blocked", snapshot, journal, retiring, false, failure.GetType().Name);
            }
            if (retiring)
            {
                try
                {
                    var restored = await Call(ct => runtime.RestorePreviousOwnerAsync(context, ct), CancellationToken.None);
                    if (!restored.FrontendReady || !restored.BackendReady || !restored.WriterActive)
                        throw new InvalidOperationException("Previous owner restoration was not confirmed.");
                }
                catch
                {
                    TryRecord("blocked-previous-owner-restore-unconfirmed");
                    return new("blocked", snapshot, journal, true, false, failure.GetType().Name);
                }
            }
            TryRecord("rolled-back");
            return new("rolled-back", snapshot, journal, retiring, false, failure.GetType().Name);
        }
        finally { gate.Release(); }
    }

    private async Task<T> Call<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return await action(deadline.Token).WaitAsync(timeout, cancellationToken);
    }
    private static void Validate(CandidateReadiness ready, MigrationPlan plan, bool active)
    {
        if (ready.InstanceId != plan.CandidateInstanceId || !ready.FrontendReady || !ready.BackendReady
            || ready.WriterActive != active || ready.OwnsFrontend != active)
            throw new InvalidOperationException("Candidate runtime readiness or ownership was not attested.");
    }
}
