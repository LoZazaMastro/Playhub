using System.Diagnostics;
namespace Playhub.Services;

internal static class XboxGameBarStartupPolicy
{
    internal static bool HasActiveLaunchHelper() => HasActiveLaunchHelper(Process.GetProcessesByName);
    internal static bool HasActiveLaunchHelper<T>(Func<string, T[]> find) where T : IDisposable
    {
        foreach (var name in new[] { "UWPHook", "Playhub.GameSession", "Playhub.XboxSession" })
        {
            var processes = find(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
}
