// SPDX-License-Identifier: MIT
using System;
using System.Globalization;

namespace Playhub.StoreLibrary;

internal static class StoreLibraryPresentation
{
    public static bool Active(StoreJobState s) => s is not (StoreJobState.Completed or StoreJobState.Canceled or StoreJobState.Failed or StoreJobState.Interrupted or StoreJobState.ExternalHandoff);
    public static string Name(StoreKind store) => store switch { StoreKind.Epic => "Epic Games", StoreKind.Gog => "GOG", StoreKind.Xbox => "Xbox", StoreKind.Amazon => "Amazon Games", _ => "" };
    public static string? ServiceLogo(StoreKind store) => store switch
    {
        StoreKind.Epic => "Epic", StoreKind.Gog => "Gog", StoreKind.Xbox => "Xbox", StoreKind.Amazon => "Amazon", _ => null
    };
    public static string StateKey(StoreJobState state) => "StoreLibrary.Job." + state;
    public static string StateGlyph(StoreJobState state) => state switch
    {
        StoreJobState.Completed => "\uE73E",
        StoreJobState.PausedByUser or StoreJobState.PausedBySteam or StoreJobState.Pausing => "\uE769",
        StoreJobState.Failed or StoreJobState.Interrupted => "\uE7BA",
        StoreJobState.Canceled or StoreJobState.Canceling => "\uE711",
        StoreJobState.Installing or StoreJobState.Verifying => "\uE713",
        _ => "\uE896"
    };
    public static double? Percent(StoreDownloadJob job) => job.State == StoreJobState.Completed ? 100 : job.ProgressPercent is double p && double.IsFinite(p) && p >= 0 && p <= 100 ? p
        : job.TotalBytes is > 0 && job.DownloadedBytes >= 0 ? Math.Clamp((double)job.DownloadedBytes / job.TotalBytes.Value * 100, 0, 100) : null;
    public static string Bytes(long value)
    {
        double n = Math.Max(0, value); string[] units = ["B", "KiB", "MiB", "GiB", "TiB"]; int i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return n.ToString(i == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[i];
    }
    public static string ProgressText(StoreDownloadJob job)
    {
        if (job.ReasonCode == "windows_store_managed") return Percent(job) is { } percent ? percent.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "—";
        var value = Bytes(job.DownloadedBytes);
        if (job.TotalBytes is > 0) value += " / " + Bytes(job.TotalBytes.Value);
        if (job.BytesPerSecond is double speed && speed > 0 && double.IsFinite(speed)) value += " · " + Bytes((long)Math.Min(speed, long.MaxValue)) + "/s";
        return value;
    }
}
