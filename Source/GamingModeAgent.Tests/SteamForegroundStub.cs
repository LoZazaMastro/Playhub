namespace GamingMode.Services;

public sealed record OverlayWindowInfo(int ProcessId, string Title, string ProcessName, string Path);

internal static class OverlayWindowTools
{
	public static bool IsSteamForeground() => false;
}
