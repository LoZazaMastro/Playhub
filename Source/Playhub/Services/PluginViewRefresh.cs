using System;

namespace Playhub.Services;

// Coalesces presentation updates without destroying a focused card. The native
// adapter supplies actual loaded/focus checks and constructs before replacing.
internal sealed class PluginViewRefresh(Func<bool> canUpdate, Func<string> snapshot,
    Func<string, bool> update, Func<Action, bool> enqueue, Action<Exception> failed,
    string initialSnapshot)
{
    private string _rendered = initialSnapshot;
    private bool _active, _queued;
    private int _generation;

    internal void Start() { _active = true; Request(); }
    internal void Stop() { _active = false; _queued = false; _generation++; }

    internal void Request()
    {
        if (!_active || _queued) return;
        _queued = true;
        var generation = _generation;
        if (!enqueue(() =>
        {
            if (!_active || generation != _generation) return;
            _queued = false;
            try
            {
                if (!canUpdate()) return;
                var desired = snapshot();
                if (desired == _rendered) return;
                if (update(desired)) _rendered = desired;
                else Request();
            }
            catch (Exception error) { failed(error); }
        })) _queued = false;
    }
}
