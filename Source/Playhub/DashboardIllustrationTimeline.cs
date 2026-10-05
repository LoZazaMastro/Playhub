using System;

namespace Playhub;

internal static class DashboardIllustrationTimeline
{
    internal const double DurationSeconds = 8;
    private static readonly (float Progress, float X)[] Frames =
    [
        (0, 0), (.12f, 0), (.27f, 78), (.43f, 78),
        (.58f, 156), (.74f, 156), (.92f, 0), (1, 0)
    ];
    internal static ReadOnlySpan<(float Progress, float X)> FocusFrames => Frames;
}
