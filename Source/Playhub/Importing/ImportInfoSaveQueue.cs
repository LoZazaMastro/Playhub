namespace Playhub.Importing;

/// <summary>Serializes user changes and flushes pending typing when Info closes. No idle timer.</summary>
public sealed class ImportInfoSaveQueue(Func<Task> saveFields, Action<string> status, TimeSpan? delay = null, Func<bool>? hasPendingFields = null)
{
    private readonly SemaphoreSlim _order = new(1, 1);
    private readonly TimeSpan _delay = delay ?? TimeSpan.FromMilliseconds(450);
    private CancellationTokenSource? _typing;
    private Task _pending = Task.CompletedTask;
    private readonly List<Task> _actions = new();
    public bool HasFailure { get; private set; }
    public void FieldsChanged()
    {
        _typing?.Cancel();
        var typing = _typing = new CancellationTokenSource();
        _pending = SaveAfterDelayAsync(typing);
    }
    private async Task SaveAfterDelayAsync(CancellationTokenSource typing)
    {
        try { await Task.Delay(_delay, typing.Token); if (hasPendingFields?.Invoke() != false) await RunAsync(saveFields); }
        catch (OperationCanceledException) when (typing.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_typing, typing)) _typing = null; typing.Dispose(); }
    }
    public Task ChangeAsync(Func<Task> action)
    {
        var task = RunAsync(action); _actions.Add(task); return task;
    }
    private async Task RunAsync(Func<Task> action)
    {
        await _order.WaitAsync();
        try { status("saving"); await action(); HasFailure = false; status("saved"); }
        catch (Exception) { HasFailure = true; status("error"); }
        finally { _order.Release(); }
    }
    public async Task FlushAsync()
    {
        _typing?.Cancel();
        await _pending;
        await Task.WhenAll(_actions);
        if (hasPendingFields?.Invoke() != false) await RunAsync(saveFields);
        _actions.RemoveAll(task => task.IsCompleted);
    }
}
