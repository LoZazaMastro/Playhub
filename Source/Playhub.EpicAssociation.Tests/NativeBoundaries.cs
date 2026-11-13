namespace GamingMode.Services;
internal static class UwpSteamSessionAssociation
{
    internal sealed record InstalledImages(string Wrapper, string Companion);
    internal static InstalledImages? ReadInstalledImages() => throw new Exception("Real installation must never be read by these fixtures.");
}
internal static class ValveImageTrust { internal static bool Verify(string path) => throw new Exception("Real signatures must never be read."); }
internal static class OverlayWindowTools
{
    internal static nint GetForegroundWindowHandle() => throw new Exception("No real foreground calls allowed.");
    internal static bool IsPresentGameSurface(nint h) => throw new Exception("No real window calls allowed.");
}
