// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

/// <summary>Observes the durable migration. Only the native Steam owner can report success.</summary>
internal sealed class StoreMigrationController(
    Func<CancellationToken, Task<StoreSteamCollectionMigration>> request,
    Func<Guid, CancellationToken, Task<StoreSteamCollectionMigration>> status) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? flight;
    private bool disposed;
    public StoreSteamCollectionMigration? Snapshot { get; private set; }
    public bool Busy { get; private set; }
    public bool ReadFailed { get; private set; }
    public bool Started { get; private set; }
    public bool WasPending { get; private set; }
    public bool InitialCompleted { get; private set; }
    public event EventHandler? Changed;
    public bool NeedsPolling => !ReadFailed && Snapshot?.State is "pending" or "waitingSteam";

    public Task StartAsync() => Started ? flight ?? Task.CompletedTask : ReadAsync(newRequest: true);
    public Task RefreshAsync() => ReadAsync(newRequest: Snapshot is null || Snapshot.State == "failed");

    private Task ReadAsync(bool newRequest)
    {
        if (disposed) return Task.CompletedTask;
        if (flight is not null) return flight;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        flight = completion.Task;
        Busy = true;
        Changed?.Invoke(this, EventArgs.Empty);
        _ = ReadCoreAsync(newRequest, completion);
        return completion.Task;
    }

    private async Task ReadCoreAsync(bool newRequest, TaskCompletionSource completion)
    {
        bool initial = !Started;
        Started = true;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var previous = Snapshot;
            var value = await (newRequest || previous is null ? request(deadline.Token)
                : status(previous.RequestId, deadline.Token)).WaitAsync(deadline.Token);
            if (disposed) return;
            if (!Valid(value) || !newRequest && previous is not null &&
                (value.RequestId != previous.RequestId || value.Version != previous.Version ||
                 previous.SteamAccountId is not null && value.SteamAccountId != previous.SteamAccountId))
                throw new InvalidOperationException("Migration readback does not match its request.");
            Snapshot = value;
            ReadFailed = false;
            if (initial && value.State == "completed") InitialCompleted = true;
            WasPending |= value.State != "completed";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { if (!disposed) ReadFailed = true; }
        finally
        {
            if (ReferenceEquals(flight, completion.Task)) flight = null;
            if (!disposed) { Busy = false; Changed?.Invoke(this, EventArgs.Empty); }
            completion.TrySetResult();
        }
    }

    internal static bool Valid(StoreSteamCollectionMigration? value) => value is not null
        && value.RequestId != Guid.Empty && !string.IsNullOrWhiteSpace(value.Version) && value.Version.Length <= 160
        && value.State is "pending" or "waitingSteam" or "completed" or "failed"
        && value.Confirmed >= 0 && value.Total >= value.Confirmed && value.Total <= 50000
        && (value.State != "completed" || value.Confirmed == value.Total)
        && (value.State == "waitingSteam" || value.SteamAccountId is { Length: 17 } account && account.All(char.IsAsciiDigit));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel(); lifetime.Dispose(); Changed = null;
    }
}
