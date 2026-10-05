namespace Playhub.Importing;

/// <summary>Owns only resources actually created by an Info dialog.</summary>
internal sealed class ImportInfoSession : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private readonly List<Action> _cleanup = new();
    private bool _closed;
    public ImportInfoSession() => _token = _cancellation.Token;
    public CancellationToken Token => _token;
    public bool IsClosed => _closed;
    public void Cancel() { if (!_closed) _cancellation.Cancel(); }
    public void Register(Action cleanup)
    {
        if (_closed) { TryCleanup(cleanup); return; }
        _cleanup.Add(cleanup);
    }
    private static void TryCleanup(Action cleanup) { try { cleanup(); } catch (Exception) { /* Closing one player must not abort the window or its remaining cleanup. */ } }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _cancellation.Cancel();
        for (var i = _cleanup.Count - 1; i >= 0; i--) TryCleanup(_cleanup[i]);
        _cleanup.Clear();
        _cancellation.Dispose();
    }
}

/// <summary>Detach the XAML renderer before releasing its app-owned native player.</summary>
internal sealed class ImportMediaResource(Action pause, Action detach, Action clearSource, Action disposeSource, Action disposePlayer) : IDisposable
{
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        try { pause(); } catch (Exception) { }
        // Disposing a player still bound to XAML is unsafe; allow an Unloaded retry if detach fails.
        try { detach(); } catch (Exception) { return; }
        _disposed = true;
        foreach (var cleanup in new[] { clearSource,disposeSource,disposePlayer })
            try { cleanup(); } catch (Exception) { }
    }
}

internal readonly record struct ImportInfoSize(double Width, double Height)
{
    public static ImportInfoSize ForRoot(double width, double height)
    {
        // Logical pixels: ContentDialog adds its native title, padding and buttons.
        var usableWidth = Math.Max(0, double.IsFinite(width) ? width - 120 : 1000);
        var usableHeight = Math.Max(0, double.IsFinite(height) ? height - 240 : 600);
        return new(Math.Min(1000, usableWidth), Math.Min(600, usableHeight));
    }
}
