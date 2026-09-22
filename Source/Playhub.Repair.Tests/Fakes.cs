namespace Playhub.Services;

// Compile the production orchestrator without WinUI or launching external components.
public sealed class GamingModeService
{
    public string InstallDir => throw new NotSupportedException();
    public string InstalledExe => throw new NotSupportedException();
    public string ConfigFile => throw new NotSupportedException();
    public Task<bool> IsAgentHealthyAsync(int port) => throw new NotSupportedException();
    public void StartAgent() => throw new NotSupportedException();
    public Task InstallDeckyPluginAsync(string path) => throw new NotSupportedException();
    public static bool NeedsDeckyPluginUpdate(string path) => throw new NotSupportedException();
}
internal static class AppPaths
{
    public static string GamingModePackage => throw new NotSupportedException();
    public static string UwpHookPackage => throw new NotSupportedException();
}
