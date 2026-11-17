// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    private sealed record LegacyInstallReceipt(StoreGameKey Key, long AccountRevision, string AccountIdentity,
        string InstallPath, Guid OwnerJob, string ManifestHash);
    private readonly Dictionary<StoreGameKey, LegacyInstallReceipt> legacyInstallReceipts = [];

    private LegacyInstallReceipt? LegacyInstallFor(StoreLibraryGame game) =>
        legacyInstallReceipts.TryGetValue(game.Key, out var receipt) && receipt.Key.Store is StoreKind.Epic or StoreKind.Gog or StoreKind.Amazon &&
        receipt.AccountRevision == game.AccountRevision &&
        StringComparer.OrdinalIgnoreCase.Equals(receipt.InstallPath, game.InstallPath) && receipt.OwnerJob != Guid.Empty ? receipt : null;

    private Guid InstallOwner(StoreLibraryGame game, StoreDownloadJob? job) => job?.JobId ?? LegacyInstallFor(game)?.OwnerJob ?? Guid.Empty;

    private async Task<string?> EnsureLegacyInstallReceiptAsync(StoreGameKey key, CancellationToken token)
    {
        StoreLibraryGame game; string identity; long accountRevision; IStoreLegacyInstalledGameProvider provider;
        await gate.WaitAsync(token);
        try
        {
            if (disposed || quiescing || key.Store is not(StoreKind.Epic or StoreKind.Gog or StoreKind.Amazon) || !games.TryGetValue(key, out var held) ||
                !held.IsInstalled || held.InstallPath is null || accounts[key.Store].State==StoreConnectionState.Connecting ||
                OwnedCompletedInstall(held) is not null ||
                Provider(key.Store) is not IStoreLegacyInstalledGameProvider legacy ||
                InstalledOperationBusy(key.Store) || RemovalTransferBusy(key.Store) ||
                jobs.Any(j => j.Key == key && !Terminal(j.State))) return null;
            game = held; identity = accountIdentities.GetValueOrDefault(key.Store)??"local-installation"; accountRevision=accounts[key.Store].Revision; provider = legacy;
            installedOperationStores.Add(key.Store); providerOperations++;
        }
        finally { gate.Release(); }
        StoreOwnedUninstallPlan? plan = null; string? failure = null;
        try { plan = await provider.VerifyLegacyInstallationAsync(key.Id, game.InstallPath!, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { failure = "uninstall_verification_timeout"; }
        catch (Exception error) when (error is not OutOfMemoryException and not OperationCanceledException)
        { failure = error is StoreOperationException operation ? operation.Code : "uninstall_verification_failed"; }
        finally
        {
            await gate.WaitAsync();
            try
            {
                installedOperationStores.Remove(key.Store); providerOperations--;
                if (plan is not null && !disposed && !quiescing && accounts[key.Store].State!=StoreConnectionState.Connecting && accounts[key.Store].Revision==accountRevision &&
                    ReferenceEquals(games.GetValueOrDefault(key), game) && (accountIdentities.GetValueOrDefault(key.Store)??"local-installation") == identity &&
                    plan.Key == key && plan.OwnerJob != Guid.Empty && StringComparer.OrdinalIgnoreCase.Equals(plan.InstallPath, game.InstallPath) &&
                    !jobs.Any(job => job.JobId == plan.OwnerJob && (job.Key != key || !StringComparer.OrdinalIgnoreCase.Equals(job.InstallPath, plan.InstallPath) ||
                        jobAccountIdentities.GetValueOrDefault(job.JobId) != identity)))
                {
                    var previous = legacyInstallReceipts.GetValueOrDefault(key);
                    legacyInstallReceipts[key] = new(key, game.AccountRevision, identity, plan.InstallPath, plan.OwnerJob, plan.ManifestHash);
                    try { Save(); }
                    catch { if (previous is null) legacyInstallReceipts.Remove(key); else legacyInstallReceipts[key] = previous; throw; }
                }
            }
            finally { gate.Release(); }
        }
        return failure;
    }

    private void RestoreLegacyInstalls(IEnumerable<LegacyInstallReceipt> receipts)
    {
        foreach (var receipt in receipts.Take(10001))
        {
            if (receipt is null || receipt.Key is null || string.IsNullOrEmpty(receipt.Key.Id) || receipt.Key.Id.Length > 250 ||
                receipt.Key.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')) || receipt.Key.Id is "." or ".." ||
                legacyInstallReceipts.Count >= 10000 || receipt.Key.Store is not(StoreKind.Epic or StoreKind.Gog or StoreKind.Amazon) || receipt.OwnerJob == Guid.Empty ||
                string.IsNullOrEmpty(receipt.ManifestHash) || receipt.ManifestHash.Length != 64 || !receipt.ManifestHash.All(Uri.IsHexDigit) ||
                receipt.AccountRevision < 0 || string.IsNullOrEmpty(receipt.AccountIdentity) || receipt.AccountIdentity.Length > 4096 ||
                string.IsNullOrEmpty(receipt.InstallPath) || !Path.IsPathFullyQualified(receipt.InstallPath) ||
                !legacyInstallReceipts.TryAdd(receipt.Key, receipt)) throw new StoreOperationException("queue_invalid");
        }
    }
}
