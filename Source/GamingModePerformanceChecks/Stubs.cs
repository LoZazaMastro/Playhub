namespace GamingMode.Services;

// Headless checks never install an input hook or change a window's focus.
public sealed record OverlayWindowInfo(nint Handle, int ProcessId, string Title,
    string ProcessName, string Path, object? Icon, bool IsMinimized);

internal enum SteamUiHapticsState { Allowed, SteamUnavailable, GameActive, SteamNotForeground }
internal sealed class SteamUiHapticsGate
{
    public bool CanPlay() => false;
    public SteamUiHapticsState ReadState() => SteamUiHapticsState.SteamNotForeground;
}
