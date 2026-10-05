using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;

namespace Playhub.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private AppInstance? _current;
    private EventHandler<AppActivationArguments>? _activated;
    private bool _ownsKey;
    private Action<string>? _log;

    public async Task<bool> RegisterAsync(string key, EventHandler<AppActivationArguments> activated, Action<string>? log = null)
    {
        _log = log;
        _log?.Invoke("Single-instance current begin");
        _current = AppInstance.GetCurrent();
        _activated = activated;
        // Subscribe before publishing the key: another process may already be starting.
        _current.Activated += _activated;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                _log?.Invoke("Single-instance registration begin");
                var instance = AppInstance.FindOrRegisterForKey(key);
                if (instance?.IsCurrent == true)
                {
                    _ownsKey = true;
                    _log?.Invoke("Single-instance primary registered");
                    return true;
                }
                if (instance is null) { await Task.Delay(100); continue; }
                Process? owner = null;
                try { owner = Process.GetProcessById((int)instance.ProcessId); _ = owner.Handle; }
                catch (ArgumentException) { owner?.Dispose(); await Task.Delay(100); continue; }
                using (owner)
                {
                    if (owner.HasExited) { await Task.Delay(100); continue; }
                    var activation = _current.GetActivatedEventArgs();
                    _log?.Invoke($"Single-instance redirect begin owner={owner.Id}");
                    var redirect = Task.Run(async () => await instance.RedirectActivationToAsync(activation));
                    if (await SingleInstanceRedirect.WaitAsync(redirect, () => owner.HasExited, TimeSpan.FromSeconds(5)))
                    {
                        _log?.Invoke("Single-instance redirect acknowledged");
                        Dispose();
                        return false;
                    }
                    _log?.Invoke("Single-instance owner exited; retry registration");
                    await Task.Delay(100);
                }
            }
            throw new InvalidOperationException("Single-instance owner was unavailable during registration.");
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_current != null)
        {
            try { if (_ownsKey) _current.UnregisterKey(); }
            finally
            {
                _ownsKey = false;
                if (_activated != null) _current.Activated -= _activated;
            }
        }
        _activated = null;
        _current = null;
    }
}
