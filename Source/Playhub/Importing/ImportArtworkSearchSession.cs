namespace Playhub.Importing;

internal readonly record struct ImportArtworkSearchKey(string Provider, string Query, string Category, bool SquareCover)
{
    internal static ImportArtworkSearchKey Create(string provider, string query, string category, bool squareCover) =>
        new(provider.Trim().ToLowerInvariant(), query.Trim(), category, category == "cover" && squareCover);
    internal static int MatchingSteamAppId(string query, string title, int knownId) =>
        knownId > 0 && query.Trim().Equals(title.Trim(), StringComparison.OrdinalIgnoreCase) ? knownId : 0;
}

/// <summary>Dialog-local alternatives, with one current request and no artwork application side effects.</summary>
internal sealed class ImportArtworkSearchSession<T> : IDisposable
{
    private sealed class Request(ImportArtworkSearchKey key, CancellationToken token)
    {
        internal readonly ImportArtworkSearchKey Key = key;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        internal readonly TaskCompletionSource<IReadOnlyList<T>> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object gate = new();
    private readonly Dictionary<ImportArtworkSearchKey, (DateTimeOffset At, IReadOnlyList<T> Results)> cache = [];
    private readonly Func<DateTimeOffset> clock;
    private Request? active;
    private bool disposed;
    internal ImportArtworkSearchSession(Func<DateTimeOffset>? clock = null) => this.clock = clock ?? (() => DateTimeOffset.UtcNow);

    internal Task<IReadOnlyList<T>> SearchAsync(ImportArtworkSearchKey key,
        Func<CancellationToken, Task<IReadOnlyList<T>>> search, CancellationToken token, bool refresh = false)
    {
        token.ThrowIfCancellationRequested();
        Request? previous, next = null;
        Task<IReadOnlyList<T>> task;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active?.Key == key) return active.Completion.Task.WaitAsync(token);
            previous = active; active = null;
            if (refresh) cache.Remove(key);
            if (!refresh && cache.TryGetValue(key, out var found) && clock() - found.At < TimeSpan.FromMinutes(5))
                task = Task.FromResult(found.Results);
            else
            {
                next = new Request(key, token); active = next;
                task = next.Completion.Task;
            }
        }
        Cancel(previous);
        if (next is not null) _ = RunAsync(next, search);
        return task.WaitAsync(token);
    }
    private async Task RunAsync(Request request, Func<CancellationToken, Task<IReadOnlyList<T>>> search)
    {
        try
        {
            var results = (await search(request.Cancellation.Token).ConfigureAwait(false)).ToArray();
            lock (gate)
            {
                request.Cancellation.Token.ThrowIfCancellationRequested();
                if (disposed || !ReferenceEquals(active, request)) throw new OperationCanceledException();
                if (cache.Count >= 32) cache.Remove(cache.MinBy(item => item.Value.At).Key);
                cache[request.Key] = (clock(), results); active = null;
            }
            request.Completion.TrySetResult(results);
        }
        catch (OperationCanceledException) { request.Completion.TrySetCanceled(); }
        catch (Exception error) { request.Completion.TrySetException(error); }
        finally
        {
            lock (gate) { if (ReferenceEquals(active, request)) active = null; }
            request.Cancellation.Dispose();
        }
    }
    private static void Cancel(Request? request)
    {
        try { request?.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { /* The request completed before this category switch. */ }
    }
    internal void Invalidate(bool clearCache = false)
    {
        Request? previous;
        lock (gate) { previous = active; active = null; if (clearCache) cache.Clear(); }
        Cancel(previous);
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        Invalidate(clearCache: true);
    }
}
