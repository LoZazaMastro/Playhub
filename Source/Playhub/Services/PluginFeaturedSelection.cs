using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playhub.Services;

internal static class PluginFeaturedSelection
{
    internal static bool IsExcluded(DeckyPluginInfo plugin) =>
        plugin.RepositorySlug.Equals("jessebofill/DeckWebBrowser", StringComparison.OrdinalIgnoreCase) ||
        (plugin.CatalogSource.Equals("decky-store", StringComparison.OrdinalIgnoreCase) && plugin.CatalogPluginId == 59) ||
        plugin.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("exclude-featured", StringComparer.OrdinalIgnoreCase);

    internal static bool IsPinned(DeckyPluginInfo plugin) =>
        plugin.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("featured", StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<DeckyPluginInfo> Select(IReadOnlyList<DeckyPluginInfo> shuffled)
    {
        var unique = shuffled.Where(plugin => !IsExcluded(plugin))
            .DistinctBy(PluginChronology.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = unique.Where(plugin => plugin.IsPlayhubPlugin)
            .OrderByDescending(IsPinned).Take(3)
            .Concat(unique.Where(plugin => !plugin.IsPlayhubPlugin).OrderByDescending(IsPinned).Take(5))
            .Select(PluginChronology.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return unique.Where(plugin => selected.Contains(PluginChronology.Identity(plugin)))
            .OrderByDescending(IsPinned).ToArray();
    }
}
