namespace GamingMode.Services;

internal static partial class XboxCompanionOverlaySession
{
    internal static bool AllowsRequest(int scope, bool overlayOpen, long keyAt, long now, bool keyboard, bool menu)
    {
        if (scope is not (1 or 2) || (menu && scope != 2 && !overlayOpen)) return false;
        // Cold CEF startup can take over a second. A recent native key proof
        // remains usable only inside the exact foreground game/companion scope.
        if (keyboard) return keyAt > 0 && keyAt <= now && now - keyAt <= 3000;
        return menu || (scope == 1 && !overlayOpen);
    }

    // The status sample can lag a frame while the exact game and companion
    // exchange focus. Only read-only ownership checks may accept that change.
    internal static bool MatchesForegroundScope(int reported, int foreground, bool verificationOnly) =>
        reported is 1 or 2 && (foreground == reported || (verificationOnly && foreground is 1 or 2));
}
