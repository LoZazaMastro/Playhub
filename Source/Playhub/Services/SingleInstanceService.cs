using Microsoft.Windows.AppLifecycle;

namespace Playhub.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private AppInstance? _current;
    private EventHandler<AppActivationArguments>? _activated;

    public async Task<bool> RegisterAsync(string key, EventHandler<AppActivationArguments> activated)
    {
        _current = AppInstance.GetCurrent();
        _activated = activated;
        // Subscribe before publishing the key: another process may already be starting.
        _current.Activated += _activated;
        var instance = AppInstance.FindOrRegisterForKey(key);
        if (instance.IsCurrent) return true;

        try
        {
            await instance.RedirectActivationToAsync(_current.GetActivatedEventArgs());
            return false;
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        if (_current != null && _activated != null)
            _current.Activated -= _activated;
        _activated = null;
        _current = null;
    }
}
