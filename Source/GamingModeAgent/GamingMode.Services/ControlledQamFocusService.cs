using System;
using System.Collections.Generic;

namespace GamingMode.Services;

public readonly record struct QamWindowIdentity(nint Handle, uint OwnerPid, long OwnerBirth, uint GamePid, long GameBirth);
public readonly record struct QamFocusResult(bool Ok, string RequestId, string Reason, long Generation);

public interface IQamFocusWindows
{
    QamWindowIdentity? CaptureGame(uint appId);
    QamWindowIdentity? CaptureSteam();
    bool IsValid(QamWindowIdentity window);
    bool IsSteamForeground(QamWindowIdentity steam);
    bool IsSourceForeground(QamWindowIdentity source);
    bool IsTopmost(QamWindowIdentity window);
    bool SetTopmost(QamWindowIdentity window, bool topmost);
    bool Activate(QamWindowIdentity window);
}

// A user-initiated main-window handoff. This never creates an in-game overlay.
// Only the chosen Steam window's layer is changed; unrelated windows are untouched.
public sealed class ControlledQamFocusService : IDisposable
{
    private sealed record Session(string Id, long Generation, QamWindowIdentity Source, QamWindowIdentity Steam, bool WasTopmost);
    private readonly IQamFocusWindows _windows;
    private readonly object _sync = new();
    private readonly HashSet<string> _cancelled = new(StringComparer.Ordinal);
    private readonly Queue<string> _cancelOrder = new();
    private Session? _session;
    private bool _gaming;
    private long _generation;
    public ControlledQamFocusService(IQamFocusWindows windows, bool gaming) { _windows = windows; _gaming = gaming; }

    // Normal COM UWP sessions use Game Bar. Keep their discovery/fullscreen/exit
    // association, but never turn Guide into an automatic Steam focus handoff.
    internal static bool AllowsAutomaticGame(bool steamTree, bool normalUwp) => steamTree && !normalUwp;

    public QamFocusResult Acquire(string id, uint appId = 0)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return Result(false, id, "Invalid request.");
            if (!_gaming || _cancelled.Contains(id)) return Result(false, id, "Request cancelled or mode changed.");
            if (_session is { } current)
                return Result(current.Id == id, id, current.Id == id ? "Already acquired." : "Another menu owns focus.");
            var source = _windows.CaptureGame(appId);
            var steam = _windows.CaptureSteam();
            if (source is null || steam is null || !_windows.IsValid(source.Value) || !_windows.IsValid(steam.Value))
                return Result(false, id, "No exact live game and Steam window.");
            var session = new Session(id, _generation, source.Value, steam.Value, _windows.IsTopmost(steam.Value));
            _session = session;
            if (!_windows.SetTopmost(session.Steam, true) || !_windows.Activate(session.Steam)
                || !_windows.IsSteamForeground(session.Steam))
            {
                End(session, false);
                return Result(false, id, "Windows did not grant Steam foreground.");
            }
            return Result(true, id, "Main Steam window acquired.");
        }
    }

    public QamFocusResult Close(string id)
    {
        lock (_sync)
        {
            Cancel(id); // Close may arrive before a delayed acquire HTTP request.
            if (_session is not { } session || session.Id != id) return Result(false, id, "No matching menu.");
            bool restored = End(session, true);
            return Result(restored, id, restored ? "Exact game restored." : "Released without changing application focus.");
        }
    }

    public QamFocusResult Release(string id)
    {
        lock (_sync)
        {
            Cancel(id);
            if (_session is { } session && session.Id == id) End(session, false);
            return Result(true, id, "Released.");
        }
    }

    public void ModeChanging()
    {
        lock (_sync)
        {
            _gaming = false;
            _generation++;
            if (_session is { } session) { Cancel(session.Id); End(session, false); }
        }
    }
    public void ModeCompleted(bool gaming) { lock (_sync) _gaming = gaming; }

    // Called only by foreground/destroy events, never an idle timer.
    public void WindowChanged()
    {
        lock (_sync)
        {
            if (_session is not { } session) return;
            if (!_windows.IsValid(session.Source) || !_windows.IsValid(session.Steam)
                || !_windows.IsSteamForeground(session.Steam))
            { Cancel(session.Id); End(session, false); }
        }
    }

    private bool End(Session session, bool restore)
    {
        _session = null;
        bool canRestore = restore && _gaming && session.Generation == _generation
            && _windows.IsValid(session.Source) && _windows.IsSteamForeground(session.Steam);
        // A later foreign layer change belongs to its caller, not this transaction.
        if (_windows.IsValid(session.Steam) && _windows.IsTopmost(session.Steam))
            _windows.SetTopmost(session.Steam, session.WasTopmost);
        return canRestore && _windows.Activate(session.Source) && _windows.IsSourceForeground(session.Source);
    }
    private void Cancel(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !_cancelled.Add(id)) return;
        _cancelOrder.Enqueue(id);
        if (_cancelOrder.Count > 64) _cancelled.Remove(_cancelOrder.Dequeue());
    }
    private QamFocusResult Result(bool ok, string id, string reason) => new(ok, id, reason, _generation);
    public void Dispose() => ModeChanging();
}
