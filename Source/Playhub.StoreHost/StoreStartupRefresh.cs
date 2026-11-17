// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

// A confirmed Steam start schedules account refreshes behind the cached snapshot.
// The queue owns account revisions and transfer state; this observer owns neither.
public sealed class StoreStartupRefresh : IAsyncDisposable
{
    private readonly IStoreLibraryService library;
    private readonly CancellationTokenSource lifetime;
    private readonly TimeSpan budget;
    private readonly Func<CancellationToken,Task>? publicCatalogRefresh;
    private readonly object sync = new();
    private readonly HashSet<SteamProcessIdentity> seen = [];
    private Task pending = Task.CompletedTask;
    private bool disposed;

    public StoreStartupRefresh(IStoreLibraryService library, CancellationToken cancellationToken = default,
        TimeSpan? refreshBudget = null, Func<CancellationToken,Task>? publicCatalogRefresh = null)
    {
        this.library = library;
        this.publicCatalogRefresh = publicCatalogRefresh;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget = refreshBudget ?? TimeSpan.FromSeconds(45);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(refreshBudget));
    }

    public void ObserveSteamStart(SteamProcessIdentity steam)
    {
        if (steam.Pid == 0 || steam.Birth <= 0) return;
        lock (sync)
        {
            if (disposed || lifetime.IsCancellationRequested || !seen.Add(steam)) return;
            // Serial account queries avoid competing with another startup refresh.
            // Register itself returns immediately; no browser or process is started.
            Task preceding = pending;
            pending = Task.Run(async () =>
            {
                try { await preceding; } catch (Exception e) when (e is not OutOfMemoryException) { }
                if (!lifetime.IsCancellationRequested) await RefreshAsync(lifetime.Token);
            });
        }
    }

    public Task WhenIdleAsync()
    {
        lock (sync) return pending;
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            // Independent public catalogue refresh never masquerades as an account.
            // Cached reads remain immediate while this owned startup task runs.
            if (publicCatalogRefresh != null)
            {
                try { await publicCatalogRefresh(token).WaitAsync(budget, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
            var snapshot = await library.GetSnapshotAsync(token).WaitAsync(budget, token);
            foreach (var account in snapshot.Accounts.Where(a => a.State == StoreConnectionState.Connected && a.Capabilities.CanRefresh))
            {
                token.ThrowIfCancellationRequested();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(budget);
                try
                {
                    // A sign-out/replacement between snapshot and query is refused
                    // by the real service's expected-account revision check.
                    await library.RefreshAsync(account.Store, account.Revision, attempt.Token).WaitAsync(budget, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is not OutOfMemoryException) { /* Keep the last confirmed catalog; the next account can still refresh. */ }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { /* Cached libraries stay available when a store is offline. */ }
    }

    public async ValueTask DisposeAsync()
    {
        Task work;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            work = pending;
        }
        try { await work; }
        finally { lifetime.Dispose(); }
    }
}
