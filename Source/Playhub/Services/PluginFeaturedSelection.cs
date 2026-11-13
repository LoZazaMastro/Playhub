using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playhub.Services;

internal static class PluginFeaturedSelection
{
    internal static bool IsPinned(DeckyPluginInfo plugin) =>
        plugin.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("featured", StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<DeckyPluginInfo> Select(IReadOnlyList<DeckyPluginInfo> shuffled)
    {
        var unique = shuffled.DistinctBy(PluginChronology.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = unique.Where(plugin => plugin.IsPlayhubPlugin)
            .OrderByDescending(IsPinned).Take(3)
            .Concat(unique.Where(plugin => !plugin.IsPlayhubPlugin).OrderByDescending(IsPinned).Take(5))
            .Select(PluginChronology.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return unique.Where(plugin => selected.Contains(PluginChronology.Identity(plugin)))
            .OrderByDescending(IsPinned).ToArray();
    }
}
