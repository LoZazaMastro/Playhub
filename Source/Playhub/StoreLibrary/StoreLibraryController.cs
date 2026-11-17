// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

/// <summary>View lifetime and commands only. The service owns credentials and downloads.</summary>
internal sealed class StoreLibraryController(IStoreLibraryService service) : IDisposable
{
    internal StoreLibraryController CreateIndependent() => new(service);
    internal long SuccessfulReadGeneration { get; private set; }
    private readonly CancellationTokenSource lifetime = new();
    private long epoch;
    private bool disposed;
    private Task? readFlight;
    private StoreConnectSession? connection;
    private StoreInstallPreparation? preparation;
    public StoreLibrarySnapshot Snapshot { get; private set; } = new(-1, DateTimeOffset.MinValue, [], [], []);
    private string? operationErrorKey;
    private readonly Dictionary<StoreKind, string> libraryErrors = new();
    private readonly Dictionary<StoreKind, Task<bool>> libraryRefreshes = new();
    private (StoreLibrarySnapshot Snapshot, StoreKind Store, long AccountRevision)? deferredRefresh;
    public string? ErrorKey { get => operationErrorKey ?? libraryErrors.Values.FirstOrDefault(); private set => operationErrorKey = value; }
    public StoreKind? ErrorStore => operationErrorKey is null && libraryErrors.Count > 0 ? libraryErrors.Keys.First() : null;
    public string? LibraryError(StoreKind store) => libraryErrors.GetValueOrDefault(store);
    public bool IsRefreshing(StoreKind store) => libraryRefreshes.ContainsKey(store);
    public bool Busy { get; private set; }
    public event EventHandler? Changed;
    private void Notify()
    {
        if (!disposed && !Busy && deferredRefresh is { } pending)
        {
            deferredRefresh = null;
            if (pending.Snapshot.Revision >= Snapshot.Revision && Account(pending.Store) is { State: StoreConnectionState.Connected } account
                && account.Revision == pending.AccountRevision) Snapshot = pending.Snapshot;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private bool Current(long expected) => !disposed && expected == epoch;

    internal static bool Valid(StoreLibrarySnapshot? s)
    {
        if (s is null || s.Revision < 0 || s.Accounts is null || s.Games is null || s.Jobs is null
            || s.Accounts.Count > Enum.GetValues<StoreKind>().Length || s.Games.Count > 100000 || s.Jobs.Count > 10000) return false;
        static bool Key(StoreGameKey? k) => k is not null && Enum.IsDefined(k.Store)
            && !string.IsNullOrWhiteSpace(k.Id) && k.Id.Length <= 512 && k.Id.Trim() == k.Id && !k.Id.Any(char.IsControl);
        static bool Caps(StoreCapabilities? c) => c is not null && Enum.IsDefined(c.InstallKind);
        if (s.Accounts.Any(a => a is null || !Enum.IsDefined(a.Store) || !Enum.IsDefined(a.State) || a.Revision < 0 || !Caps(a.Capabilities))
            || s.Accounts.Select(a => a.Store).Distinct().Count() != s.Accounts.Count) return false;
        if (s.Games.Any(g => g is null || !Key(g.Key) || string.IsNullOrWhiteSpace(g.Title) || g.AccountRevision < 0
            || !Caps(g.Capabilities) || g.DownloadBytes < 0 || g.IsLaunchable && !g.IsInstalled)
            || s.Games.Select(g => g.Key).Distinct().Count() != s.Games.Count) return false;
        return !s.Jobs.Any(j => j is null || j.JobId == Guid.Empty || !Key(j.Key) || !Enum.IsDefined(j.State)
            || j.Revision < 0 || j.AccountRevision < 0 || j.DownloadedBytes < 0 || j.TotalBytes < 0
            || j.BytesPerSecond is double speed && (!double.IsFinite(speed) || speed < 0)
            || j.ProgressPercent is double percent && (!double.IsFinite(percent) || percent < 0 || percent > 100))
            && s.Jobs.Select(j => j.JobId).Distinct().Count() == s.Jobs.Count;
    }

    private bool Accept(StoreLibrarySnapshot snapshot, long expected)
    {
        if (!Current(expected) || !Valid(snapshot)) return false;
        if (snapshot.Revision >= Snapshot.Revision) { Snapshot = snapshot; ErrorKey = null; }
        return true; // A valid older read is superseded, not a read failure.
    }

    public Task ReadAsync()
    {
        if (disposed || Busy) return Task.CompletedTask;
        if (readFlight is { } current) return current;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        readFlight = completion.Task;
        _ = ReadCoreAsync(completion);
        return completion.Task;
    }
    private async Task ReadCoreAsync(TaskCompletionSource completion)
    {
        long expected = epoch;
        try
        {
            var next = await service.GetSnapshotAsync(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(15), lifetime.Token);
            if (Current(expected))
            {
                if (!Accept(next, expected)) ErrorKey = "StoreLibrary.ReadFailed";
                else if (next.Revision >= Snapshot.Revision) SuccessfulReadGeneration++;
            }
        }
        catch { if (Current(expected)) ErrorKey = "StoreLibrary.ReadFailed"; }
        finally { if (ReferenceEquals(readFlight, completion.Task)) readFlight = null; completion.TrySetResult(); if (Current(expected)) Notify(); }
    }

    private StoreAccount? Account(StoreKind store) => Snapshot.Accounts.FirstOrDefault(a => a.Store == store);
    public IEnumerable<StoreLibraryGame> VisibleGames => Snapshot.Games.Where(g => g.IsInstalled
        || Account(g.Key.Store) is { State: StoreConnectionState.Connected } a && a.Revision == g.AccountRevision);
    public bool CanInstall(StoreLibraryGame game) => !Busy && !game.IsInstalled
        && Snapshot.Games.FirstOrDefault(g => g.Key == game.Key && g.AccountRevision == game.AccountRevision) is { IsInstalled: false } current
        && current.Capabilities.InstallKind == StoreInstallKind.Direct
        && game.Capabilities.InstallKind == StoreInstallKind.Direct
        && Account(game.Key.Store) is { } a && a.Revision == game.AccountRevision
        && (game.Capabilities.InstallKind == StoreInstallKind.ExternalHandoff || a.State == StoreConnectionState.Connected)
        && !Snapshot.Jobs.Any(j => j.Key == game.Key && StoreLibraryPresentation.Active(j.State));
    public bool CanControl(StoreDownloadJob job, StoreJobAction action) => !Busy && Snapshot.Jobs.Contains(job)
        && (action switch { StoreJobAction.Pause => job.CanPause, StoreJobAction.Resume => job.CanResume, StoreJobAction.Cancel => job.CanCancel, StoreJobAction.DownloadNow => job.CanDownloadNow, _ => false });
    public bool CanClearCompleted => !disposed && !Busy && service is IStoreDownloadHistoryService && Snapshot.Jobs.Any(j => j.State == StoreJobState.Completed);
    public async Task<bool> ClearCompletedAsync()
    {
        if (!CanClearCompleted || service is not IStoreDownloadHistoryService history) return false;
        await ReadAsync();
        if (!CanClearCompleted) return false;
        var revision=Snapshot.Revision;
        var selections=Snapshot.Jobs.Where(j=>j.State is StoreJobState.Completed or StoreJobState.Canceled)
            .Select(j=>new StoreHistorySelection(j.JobId,j.Revision,j.Key,j.AccountRevision)).ToArray();
        return await Command(ct => history.ClearCompletedAsync(new(revision, Guid.NewGuid(),selections), ct));
    }

    private async Task<bool> Command(Func<CancellationToken, Task<StoreCommandResult>> operation)
    {
        if (disposed || Busy) return false;
        Busy = true; long expected = ++epoch; ErrorKey = null; Notify();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            var result = await operation(deadline.Token).WaitAsync(deadline.Token);
            if (!Current(expected)) return false;
            if (!Accept(result.Snapshot, expected) || !result.Accepted)
            { ErrorKey = "StoreLibrary.CommandFailed"; return false; }
            return true;
        }
        catch { if (Current(expected)) ErrorKey = "StoreLibrary.CommandFailed"; return false; }
        finally { if (Current(expected)) { Busy = false; Notify(); } }
    }

    public Task<bool> RefreshAsync(StoreKind store)
    {
        var a = Account(store);
        if (disposed || a is null || a.State != StoreConnectionState.Connected || !a.Capabilities.CanRefresh) return Task.FromResult(false);
        if (libraryRefreshes.TryGetValue(store, out var current)) return current;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        libraryRefreshes.Add(store, completion.Task);
        Notify();
        _ = RefreshCoreAsync(store, a.Revision, completion);
        return completion.Task;
    }
    private async Task RefreshCoreAsync(StoreKind store, long accountRevision, TaskCompletionSource<bool> completion)
    {
        bool refreshed = false;
        try
        {
            // Metadata refresh must not disable every game action or cancel an
            // unrelated store. Large owned libraries share the host's 2-minute budget.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var result = await service.RefreshAsync(store, accountRevision, deadline.Token).WaitAsync(deadline.Token);
            if (disposed || Account(store) is not { State: StoreConnectionState.Connected } account || account.Revision != accountRevision) return;
            refreshed = result.Accepted && Valid(result.Snapshot)
                && result.Snapshot.Accounts.Any(a => a.Store == store && a.State == StoreConnectionState.Connected && a.Revision == accountRevision);
            if (refreshed && result.Snapshot.Revision >= Snapshot.Revision)
            {
                if (!Busy) Snapshot = result.Snapshot;
                else if (deferredRefresh is null || result.Snapshot.Revision > deferredRefresh.Value.Snapshot.Revision)
                    deferredRefresh = (result.Snapshot, store, accountRevision);
            }
            if (refreshed) libraryErrors.Remove(store);
            else libraryErrors[store] = "StoreLibrary.RefreshFailed";
        }
        catch { if (!disposed && Account(store)?.Revision == accountRevision) libraryErrors[store] = "StoreLibrary.RefreshFailed"; }
        finally
        {
            libraryRefreshes.Remove(store);
            completion.TrySetResult(refreshed);
            if (!disposed) Notify();
        }
    }
    public async Task<bool> DisconnectAsync(StoreKind store)
    {
        var a = Account(store); connection = null;
        if (a is null) return false;
        bool disconnected = await Command(ct => service.DisconnectAsync(store, a.Revision, ct));
        if (disconnected && !disposed) { libraryErrors.Remove(store); Notify(); }
        return disconnected;
    }

    public async Task<StoreConnectSession?> BeginConnectAsync(StoreKind store)
    {
        if (disposed || Busy || Account(store)?.Capabilities.CanConnect != true) return null;
        Busy = true; long expected = ++epoch; connection = null; ErrorKey = null; Notify();
        try
        {
            var c = await service.BeginConnectAsync(store, lifetime.Token).WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (!Current(expected)) return null;
            var uri = c.AuthorizationUri;
            bool allowed = AuthorizationUriAllowed(store, uri);
            if (c.SessionId == Guid.Empty || c.Store != store || c.ExpiresAt <= DateTimeOffset.UtcNow || !allowed || !c.RequiresAuthorizationCode)
            { ErrorKey = "StoreLibrary.ConnectFailed"; return null; }
            connection = c; return c;
        }
        catch { if (Current(expected)) ErrorKey = "StoreLibrary.ConnectFailed"; return null; }
        finally { if (Current(expected)) { Busy = false; Notify(); } }
    }
    internal static bool AuthorizationUriAllowed(StoreKind store, Uri? uri)
    {
        if (uri is not { IsAbsoluteUri: true } || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (store == StoreKind.Amazon)
            return (uri.Host.Equals("amazon.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.amazon.com", StringComparison.OrdinalIgnoreCase))
                && uri.AbsolutePath == "/ap/signin";
        string domain = store == StoreKind.Epic ? "epicgames.com" : store == StoreKind.Gog ? "gog.com" : "";
        return domain.Length > 0 && (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
    public Task<bool> CompleteConnectAsync(StoreConnectSession session, string code)
    {
        if (!ReferenceEquals(session, connection) || session.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(code) || code.Length > 8192)
            return Task.FromResult(false);
        connection = null; // one attempt; secrets never retained in this controller
        return Command(ct => service.CompleteConnectAsync(session.SessionId, code, ct));
    }
    public async Task<bool> ConnectAndLoadLibraryAsync(StoreConnectSession session, string code)
    {
        if (!await CompleteConnectAsync(session, code)) return false;
        await RefreshAsync(session.Store);
        return !disposed && Account(session.Store)?.State == StoreConnectionState.Connected;
    }
    public async Task RefreshLibrariesAsync()
    {
        await ReadAsync();
        if (disposed || Busy || operationErrorKey is not null) return;
        await Task.WhenAll(Snapshot.Accounts.Where(a => a.State == StoreConnectionState.Connected && a.Capabilities.CanRefresh)
            .Select(a => a.Store).ToArray().Select(RefreshAsync));
    }
    public void CancelConnect() { connection = null; }
    public async Task<StoreInstallPreparation?> PrepareInstallAsync(StoreLibraryGame game, string? path = null)
    {
        if (!CanInstall(game)) return null;
        if (path is not null && !Path.IsPathFullyQualified(path)) { ErrorKey = "StoreLibrary.DestinationInvalid"; Notify(); return null; }
        Busy = true; long expected = ++epoch; preparation = null; ErrorKey = null; Notify();
        try
        {
            var p = await service.PrepareInstallAsync(game.Key, game.AccountRevision, path, lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (!Current(expected)) return null;
            if (p.ErrorCode is { } error)
            {
                ErrorKey = error switch
                {
                    "invalid_destination" => "StoreLibrary.DestinationInvalid",
                    "destination_not_empty_or_unwritable" => "StoreLibrary.DestinationNotEmpty",
                    "insufficient_space" => "StoreLibrary.InsufficientSpace",
                    _ => "StoreLibrary.PrepareFailed"
                };
                return null;
            }
            if (p.Key != game.Key || p.AccountRevision != game.AccountRevision || p.Revision < 0
                || p.InstallKind != game.Capabilities.InstallKind || p.ErrorCode is not null
                || p.Locations is null || p.Locations.Any(l => l.FreeBytes < 0) || p.RequiredBytes < 0
                || path is not null && (p.ConfirmedPath is null || !Path.GetFullPath(path).Equals(Path.GetFullPath(p.ConfirmedPath), StringComparison.OrdinalIgnoreCase)))
            { ErrorKey = "StoreLibrary.PrepareFailed"; return null; }
            if (path is not null && p.InstallKind == StoreInstallKind.Direct)
            {
                string root = Path.GetPathRoot(p.ConfirmedPath!) ?? "";
                var location = p.Locations.FirstOrDefault(l => l.RootPath.Equals(root, StringComparison.OrdinalIgnoreCase));
                if (location is null || !location.IsReady || p.RequiredBytes is long needed && location.FreeBytes < needed)
                { ErrorKey = "StoreLibrary.PrepareFailed"; return null; }
            }
            preparation = p; return p;
        }
        catch { if (Current(expected)) ErrorKey = "StoreLibrary.PrepareFailed"; return null; }
        finally { if (Current(expected)) { Busy = false; Notify(); } }
    }
    public Task<bool> InstallAsync(StoreLibraryGame game, StoreInstallPreparation reviewed, string? language)
    {
        if (!CanInstall(game) || !ReferenceEquals(reviewed, preparation) || reviewed.Key != game.Key
            || reviewed.AccountRevision != game.AccountRevision || reviewed.ExpiresAt <= DateTimeOffset.UtcNow
            || reviewed.InstallKind != game.Capabilities.InstallKind) return Task.FromResult(false);
        if (reviewed.InstallKind == StoreInstallKind.Direct && (reviewed.SelectionNonce is not Guid nonce || nonce == Guid.Empty
            || string.IsNullOrWhiteSpace(reviewed.ConfirmedPath) || !Path.IsPathFullyQualified(reviewed.ConfirmedPath))) return Task.FromResult(false);
        preparation = null; // each new install requires a new explicit reviewed selection
        var request = new StoreEnqueueRequest(game.Key, game.AccountRevision, reviewed.ConfirmedPath ?? "", Guid.NewGuid(),
            reviewed.SelectionNonce ?? Guid.Empty, reviewed.Revision, language);
        return Command(ct => service.EnqueueAsync(request, ct));
    }
    public Task<bool> ControlAsync(StoreDownloadJob job, StoreJobAction action)
    {
        if (action == StoreJobAction.Cancel) return Task.FromResult(false);
        return DispatchControlAsync(job, action);
    }
    public async Task<bool> CancelAsync(StoreDownloadJob job, Func<Task<bool>> confirm)
    {
        if (!CanControl(job, StoreJobAction.Cancel)) return false;
        long expected = epoch;
        if (!await confirm() || !Current(expected)) return false;
        if (readFlight is { } prior) await prior;
        if (!Current(expected)) return false;
        await ReadAsync();
        var latest = Snapshot.Jobs.FirstOrDefault(j => j.JobId == job.JobId && j.Key == job.Key && j.AccountRevision == job.AccountRevision);
        if (ErrorKey is not null || latest is null || !Current(expected)) return false;
        return await DispatchControlAsync(latest, StoreJobAction.Cancel);
    }
    private Task<bool> DispatchControlAsync(StoreDownloadJob job, StoreJobAction action)
    {
        if (!CanControl(job, action)) return Task.FromResult(false);
        var request = new StoreJobCommand(job.JobId, job.Revision, action, Guid.NewGuid(),job.Key,job.AccountRevision,job.InstallPath);
        return Command(ct => service.ControlAsync(request, ct));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; ++epoch; connection = null;
        lifetime.Cancel(); lifetime.Dispose(); Changed = null;
        // View disposal never sends Cancel for a service-owned download.
    }
}
