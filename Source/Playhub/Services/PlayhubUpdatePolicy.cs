namespace Playhub.Services;

internal static class PlayhubUpdatePolicy
{
#if PLAYHUB_UPDATE_PREVIEW
    internal const bool IsPreview = true;
#else
    internal const bool IsPreview = false;
#endif

    internal static string Repository(string configured) =>
        IsPreview ? "LoZazaMastro/Playhub" : configured;

    internal static string? ReleaseTag => IsPreview ? "v1.2.1" : null;

    internal static bool ShouldOffer(PlayhubUpdateService.UpdateInfo? info) =>
        info is not null && (IsPreview
            ? info.LatestVersion == "1.2.1" && !string.IsNullOrWhiteSpace(info.DownloadUrl)
            : info.IsNewer);
}
