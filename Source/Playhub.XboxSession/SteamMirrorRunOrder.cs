// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal static class SteamMirrorRunOrder
{
    // Called only after route configuration and bridge attachment (or explicit
    // legacy capability absence). Factory lifetime ends before Bridge.Detach.
    internal static int Run(Func<IDisposable?>? onConfiguredLayout, Func<int> nativeRun)
    {
        using var controllerLayout = onConfiguredLayout?.Invoke();
        return nativeRun();
    }
}
