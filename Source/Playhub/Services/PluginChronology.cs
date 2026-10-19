using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Playhub.Services;

// Publication dates belong to the upstream project, never to a Playhub import.
public static class PluginChronology
{
    public const int NewsLimit = 100;

    public static DateTimeOffset? FirstPublished(DeckyPluginInfo plugin, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        if (Instant(plugin.FirstPublishedAt, clock) is { } first) return first;
        foreach (var keyword in plugin.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (keyword.StartsWith("first-release:", StringComparison.OrdinalIgnoreCase) &&
                Instant(keyword["first-release:".Length..], clock) is { } legacy) return legacy;
        return null;
    }

    public static DateTimeOffset? LastUpdated(DeckyPluginInfo plugin, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var raw = new[] { Instant(plugin.UpdatedAt, clock), Instant(plugin.ReleasePublishedAt, clock) }
            .Where(date => date.HasValue).Max();
        // A local display date may fall on tomorrow in UTC. Use it only when
        // no source timestamp survives, never to overrule a precise instant.
        var updated = raw ?? new[] { Instant(plugin.UpdatedAt, clock, allowDisplayDate: true), Instant(plugin.ReleasePublishedAt, clock, allowDisplayDate: true) }
            .Where(date => date.HasValue).Max();
        return new[] { updated, FirstPublished(plugin, clock) }.Where(date => date.HasValue).Max();
    }

    public static IEnumerable<DeckyPluginInfo> Sort(IEnumerable<DeckyPluginInfo> plugins, string mode)
    {
        var now = DateTimeOffset.UtcNow;
        return mode switch
        {
            "added" => plugins.OrderByDescending(plugin => FirstPublished(plugin, now) ?? DateTimeOffset.MinValue)
                .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(Identity, StringComparer.OrdinalIgnoreCase),
            "updated" => plugins.OrderByDescending(plugin => LastUpdated(plugin, now) ?? DateTimeOffset.MinValue)
                .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(Identity, StringComparer.OrdinalIgnoreCase),
            _ => plugins.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(Identity, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static IReadOnlyList<DeckyPluginInfo> News(IEnumerable<DeckyPluginInfo> plugins, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        return plugins.Select(plugin => (Plugin: plugin, Date: FirstPublished(plugin, clock)))
            .Where(item => item.Date.HasValue)
            .OrderByDescending(item => item.Date)
            .ThenBy(item => Identity(item.Plugin), StringComparer.OrdinalIgnoreCase)
            .DistinctBy(item => Identity(item.Plugin), StringComparer.OrdinalIgnoreCase)
            .Take(NewsLimit).Select(item => item.Plugin).ToArray();
    }

    public static string Identity(DeckyPluginInfo plugin)
    {
        if (Uri.TryCreate(plugin.RepositoryUrl, UriKind.Absolute, out var repository))
            return repository.Host + ":" + repository.AbsolutePath.Trim('/');
        if (!string.IsNullOrWhiteSpace(plugin.RepositorySlug)) return "github.com:" + plugin.RepositorySlug.Trim().Trim('/');
        if (plugin.CatalogSource == "decky-store" && plugin.CatalogPluginId > 0) return "decky:" + plugin.CatalogPluginId;
        return plugin.CatalogSource + ":" + plugin.FolderName + ":" + plugin.Author + ":" + plugin.Name;
    }

    private static DateTimeOffset? Instant(string? text, DateTimeOffset now, bool allowDisplayDate = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Retain support for existing display-only caches; never use current culture.
        var formats = allowDisplayDate
            ? new[] { "O", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd", "dd/MM/yyyy" }
            : new[] { "O", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd" };
        if (!DateTimeOffset.TryParseExact(text.Trim(), formats,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) return null;
        return parsed <= now ? parsed.ToUniversalTime() : null;
    }
}
