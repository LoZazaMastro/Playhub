using System;
using System.Threading;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly AsyncLocal<string?> _notificationOrigin = new();
    private bool IsStoreNotificationContext()
        => _currentPageTag is "plugins" or "plugin-detail" ||
           _notificationOrigin.Value is "plugins" or "plugin-detail";

    private IDisposable BeginNotificationContext(string? page = null)
    {
        var previous = _notificationOrigin.Value;
        _notificationOrigin.Value = page ?? _currentPageTag;
        return new NotificationContext(() => _notificationOrigin.Value = previous);
    }

    private sealed class NotificationContext(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

}
