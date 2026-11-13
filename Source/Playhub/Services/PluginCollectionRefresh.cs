using System;
using System.Collections.Generic;
using System.Linq;

namespace Playhub.Services;

// Collection moves can recreate an entire virtualized grid row. Hold only this
// result set while it owns focus, then apply the latest snapshot after departure.
internal sealed class PluginCollectionRefresh<T>(Func<bool> canUpdate,
    Action<IReadOnlyList<T>> apply, Func<Action, bool> enqueue, Action<Exception> failed)
{
    private IReadOnlyList<T>? _pending;
    private bool _active, _queued, _applying;
    private int _generation;

    internal void Set(IReadOnlyList<T> desired) { _pending = desired.ToArray(); Resume(); }
    internal void Start() { _active = true; Resume(); }
    internal void Stop() { _active = false; _queued = false; _generation++; }

    internal void Resume()
    {
        if (!_active || _queued || _applying || _pending is null) return;
        _queued = true;
        var generation = _generation;
        if (!enqueue(() =>
        {
            if (!_active || generation != _generation) return;
            _queued = false;
            IReadOnlyList<T>? desired = null;
            var completed = false;
            _applying = true;
            try
            {
                if (!canUpdate() || _pending is not { } next) return;
                desired = next;
                _pending = null;
                apply(desired);
                completed = true;
            }
            catch (Exception error) { _pending ??= desired; failed(error); }
            finally
            {
                _applying = false;
                // WinRT collection notifications may pump the dispatcher. Finish
                // this transaction before a newer desired list starts applying.
                if (completed) Resume();
            }
        })) _queued = false;
    }
}
