namespace Playhub.StandaloneHost.Migration;

public sealed record MigrationSource(string Name, string RelativeDirectory);
public sealed record MigrationFile(string RelativePath, long Bytes, string Sha256);
public sealed record MigrationPlan(string Id, string CandidateInstanceId,
    IReadOnlyList<MigrationSource> Sources, IReadOnlyList<MigrationFile> Files);
public sealed record SnapshotReceipt(string RelativeDirectory, string ManifestSha256, int FileCount);
public sealed record CandidateReadiness(string InstanceId, bool FrontendReady, bool BackendReady,
    bool OwnsFrontend, bool WriterActive);
public sealed record PreviousOwnerStopped(bool FrontendUnmounted, bool BackendStopped, bool WritersDrained);
public sealed record CandidateStopped(bool FrontendUnmounted, bool BackendStopped, bool WritersDrained);
public sealed record PreviousOwnerRestored(bool FrontendReady, bool BackendReady, bool WriterActive);
public sealed record HandoverContext(string TransactionId, string PlanId, string CandidateInstanceId);
public sealed record HandoverResult(string Status, SnapshotReceipt? Snapshot, string? JournalDirectory,
    bool PreviousOwnerRetirementAttempted, bool StandaloneActivated, string? Failure);

/// <summary>
/// A real implementation must attest runtime state, not files or a listening port.
/// Operations are idempotent and fenced by TransactionId. Cancellation stops any
/// delayed operation; StopCandidate revokes this transaction before confirming.
/// No production adapter is supplied by this fixture-only foundation.
/// </summary>
public interface IHostHandover
{
    Task<CandidateReadiness> ProbeCandidateAsync(HandoverContext context, CancellationToken cancellationToken);
    Task<PreviousOwnerStopped> RetirePreviousOwnerAsync(HandoverContext context, CancellationToken cancellationToken);
    Task ActivateCandidateAsync(HandoverContext context, CancellationToken cancellationToken);
    Task<CandidateStopped> StopCandidateAsync(HandoverContext context, CancellationToken cancellationToken);
    Task<PreviousOwnerRestored> RestorePreviousOwnerAsync(HandoverContext context, CancellationToken cancellationToken);
}
