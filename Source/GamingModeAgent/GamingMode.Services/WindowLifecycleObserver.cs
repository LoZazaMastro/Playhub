using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace GamingMode.Services;

// WinEvent callbacks are delivered on the registering thread. Keeping its
// message pump asleep avoids repeated global EnumWindows scans when idle.
internal sealed class WindowLifecycleObserver : IDisposable
{
    private delegate void WinEventCallback(nint hook, uint eventId, nint window,
        int objectId, int childId, uint eventThread, uint eventTime);
    private readonly WinEventCallback _callback;
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private int _disposed;

    public WindowLifecycleObserver(Action changed)
    {
        _callback = (_, eventId, window, objectId, childId, _, _) =>
        {
            // Ignore accessibility events for controls and menus inside a window.
            if (window != 0 && (eventId == 3 || (objectId == 0 && childId == 0))) changed();
        };
        _thread = new Thread(Run) { IsBackground = true, Name = "Playhub window events" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        if (Volatile.Read(ref _disposed) != 0) return;
        List<nint> hooks = new();
        try
        {
            hooks.Add(SetWinEventHook(3, 3, 0, _callback, 0, 0, 2)); // foreground, skip this process
            hooks.Add(SetWinEventHook(0x8000, 0x8003, 0, _callback, 0, 0, 2)); // create/destroy/show/hide
            Dispatcher.Run();
        }
        finally { foreach (nint hook in hooks) if (hook != 0) UnhookWinEvent(hook); }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if (Thread.CurrentThread != _thread) _thread.Join(500);
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint minimum, uint maximum, nint module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);
}
