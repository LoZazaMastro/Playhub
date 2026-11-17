// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

internal enum StoreAccountIntent { SignIn, SignOut }
internal enum StoreAccountActionResult { Completed, AlreadyInState, Unavailable, Failed, Canceled }

/// <summary>Independent account command slots over the same authoritative service.</summary>
internal sealed class StoreAccountControllers : IDisposable
{
    private readonly Dictionary<StoreKind, StoreAccountController> stores = new();
    private bool disposed;
    internal event EventHandler? Changed;
    internal StoreAccountControllers(StoreLibraryController source)
    {
        foreach (StoreKind kind in Enum.GetValues<StoreKind>())
        {
            var account = new StoreAccountController(kind, source.CreateIndependent());
            account.Changed += ForwardChanged; stores.Add(kind, account);
        }
    }
    private void ForwardChanged(object? sender, EventArgs args) { if (!disposed) Changed?.Invoke(sender, args); }
    internal StoreAccountController ForStore(StoreKind kind) => stores.TryGetValue(kind, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(kind));
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var account in stores.Values) { account.Changed -= ForwardChanged; account.Dispose(); }
        Changed = null;
    }
}

internal sealed class StoreAccountController : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private StoreLibraryController controller;
    private Task<bool>? loadFlight;
    private Task<StoreAccountActionResult>? actionFlight;
    private StoreAccountIntent? activeIntent;
    private bool disposed;
    internal StoreLibraryController Controller { get { lock (gate) return controller; } }
    internal StoreKind Store { get; }
    internal bool Busy { get { lock (gate) return actionFlight is not null; } }
    internal bool Loading { get { lock (gate) return loadFlight is not null; } }
    internal StoreAccount? Account => Controller.Snapshot.Accounts.FirstOrDefault(account => account.Store == Store);
    internal event EventHandler? Changed;
    internal StoreAccountController(StoreKind store, StoreLibraryController controller)
    {
        Store = store; this.controller = controller; lifetimeToken = lifetime.Token;
        controller.Changed += ControllerChanged;
    }
    private void ControllerChanged(object? sender, EventArgs args) { if (ReferenceEquals(sender, Controller)) Notify(); }
    private void Notify() { lock (gate) { if (disposed) return; } Changed?.Invoke(this, EventArgs.Empty); }

    // Bootstrap failures retry only on this store's slot, independently.
    internal Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool>? completion = null; Task<bool> flight; StoreLibraryController current;
        lock (gate)
        {
            if (disposed || cancellationToken.IsCancellationRequested) return Task.FromResult(false);
            current = controller;
            if (loadFlight is { } existing) flight = existing;
            else { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); flight = loadFlight = completion.Task; }
        }
        if (completion is not null) { Notify(); _ = LoadAsync(current, completion); }
        return WaitForLoadAsync(flight, cancellationToken);
    }
    private static async Task<bool> WaitForLoadAsync(Task<bool> flight, CancellationToken cancellationToken)
    { try { return await flight.WaitAsync(cancellationToken); } catch (OperationCanceledException) { return false; } }
    private async Task LoadAsync(StoreLibraryController current, TaskCompletionSource<bool> completion)
    {
        bool loaded = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!deadline.IsCancellationRequested && !disposed && ReferenceEquals(current, Controller))
            {
                long before = current.SuccessfulReadGeneration;
                await current.ReadAsync().WaitAsync(deadline.Token);
                if (disposed || !ReferenceEquals(current, Controller)) break;
                if (current.SuccessfulReadGeneration > before && StoreLibraryController.Valid(current.Snapshot)
                    && current.Snapshot.Accounts.Any(account => account.Store == Store)) { loaded = true; break; }
                await Task.Delay(150, deadline.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            lock (gate) { if (ReferenceEquals(loadFlight, completion.Task)) loadFlight = null; }
            completion.TrySetResult(loaded && !disposed && ReferenceEquals(current, Controller)); Notify();
        }
    }

    internal Task<StoreAccountActionResult> RunAsync(StoreAccountIntent intent,
        Func<StoreLibraryController, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(intent)) throw new ArgumentOutOfRangeException(nameof(intent));
        ArgumentNullException.ThrowIfNull(action);
        TaskCompletionSource<StoreAccountActionResult> completion;
        lock (gate)
        {
            if (disposed || cancellationToken.IsCancellationRequested) return Task.FromResult(StoreAccountActionResult.Canceled);
            if (actionFlight is { } pending)
                return activeIntent == intent ? WaitForActionAsync(pending, cancellationToken) : Task.FromResult(StoreAccountActionResult.Unavailable);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actionFlight = completion.Task; activeIntent = intent;
        }
        Notify(); _ = RunOwnedAsync(intent, action, cancellationToken, completion);
        return completion.Task;
    }
    private static async Task<StoreAccountActionResult> WaitForActionAsync(Task<StoreAccountActionResult> flight, CancellationToken cancellationToken)
    { try { return await flight.WaitAsync(cancellationToken); } catch (OperationCanceledException) { return StoreAccountActionResult.Canceled; } }

    private async Task RunOwnedAsync(StoreAccountIntent intent, Func<StoreLibraryController, CancellationToken, Task> action,
        CancellationToken requested, TaskCompletionSource<StoreAccountActionResult> completion)
    {
        StoreAccountActionResult result = StoreAccountActionResult.Failed;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, requested);
        var current = Controller;
        // Cancel the controller's own token too: auth callbacks or provider
        // tasks can ignore the caller token and complete much later.
        using var revoke = attempt.Token.Register(() => Revoke(current));
        try
        {
            bool loaded = await EnsureLoadedAsync(attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            if (!loaded) { result = StoreAccountActionResult.Unavailable; return; }
            var account = Account!;
            if (intent == StoreAccountIntent.SignIn && account.State == StoreConnectionState.Connected
                || intent == StoreAccountIntent.SignOut && account.State == StoreConnectionState.Disconnected)
            { result = StoreAccountActionResult.AlreadyInState; return; }
            if (account.State is StoreConnectionState.Connecting or StoreConnectionState.Unavailable
                || intent == StoreAccountIntent.SignIn && !account.Capabilities.CanConnect)
            { result = StoreAccountActionResult.Unavailable; return; }
            await action(current, attempt.Token).WaitAsync(attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            loaded = await EnsureLoadedAsync(attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            if (!loaded) { result = StoreAccountActionResult.Failed; return; }
            var expected = intent == StoreAccountIntent.SignIn ? StoreConnectionState.Connected : StoreConnectionState.Disconnected;
            result = Account?.State == expected ? StoreAccountActionResult.Completed : StoreAccountActionResult.Failed;
        }
        catch (OperationCanceledException) { result = StoreAccountActionResult.Canceled; }
        catch (Exception) { result = StoreAccountActionResult.Failed; }
        finally
        {
            // WaitAsync's cancellation continuation can run before an older
            // token registration. Revoke synchronously before publishing the
            // result as well; disposal of the registration must not skip it.
            if (attempt.IsCancellationRequested) Revoke(current);
            lock (gate) { if (ReferenceEquals(actionFlight, completion.Task)) { actionFlight = null; activeIntent = null; } }
            completion.TrySetResult(disposed ? StoreAccountActionResult.Canceled : result); Notify();
        }
    }
    private void Revoke(StoreLibraryController expected)
    {
        lock (gate)
        {
            if (disposed || !ReferenceEquals(controller, expected)) return;
            var replacement = expected.CreateIndependent();
            expected.Changed -= ControllerChanged; controller = replacement; loadFlight = null;
            replacement.Changed += ControllerChanged;
        }
        expected.Dispose(); Notify();
    }
    public void Dispose()
    {
        StoreLibraryController current;
        lock (gate) { if (disposed) return; disposed = true; current = controller; current.Changed -= ControllerChanged; Changed = null; }
        lifetime.Cancel(); current.Dispose(); lifetime.Dispose();
    }
}

