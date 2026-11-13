using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Playhub.Services;

// Catalog workers return detached snapshots. Keep the objects owned by visible
// controls and install actions, and publish only actual changes to presentation.
internal static class PluginCatalogReconciliation
{
    private static readonly PropertyInfo[] Properties = typeof(DeckyPluginInfo).GetProperties()
        .Where(property => property.CanRead && property.CanWrite).ToArray();

    internal sealed record Result(IReadOnlyList<DeckyPluginInfo> Plugins,
        IReadOnlySet<string> Changed, bool MembershipChanged)
    {
        internal bool HasChanges => MembershipChanged || Changed.Count != 0;
    }

    internal static string Snapshot(DeckyPluginInfo plugin) => JsonSerializer.Serialize(plugin);

    internal static string Presentation(DeckyPluginInfo plugin) => JsonSerializer.Serialize(new
    {
        plugin.Name, plugin.FolderName, plugin.Author, plugin.RepositoryName, plugin.Version, plugin.InstalledVersion, plugin.HasUpdate,
        plugin.ShortDescription, plugin.LongDescription, plugin.Readme, plugin.IconGlyph, plugin.Media,
        plugin.Image, plugin.CoverImage, plugin.Category, plugin.Keywords, plugin.IsPlayhubPlugin,
        plugin.CatalogStatus, plugin.CatalogSource, plugin.CatalogPluginId, plugin.Compatibility,
        plugin.ReleaseNotes, plugin.ReleaseNotesVersion, plugin.ReleaseNotesPublishedAt, plugin.ReleasePublishedAt,
        plugin.FirstPublishedAt, plugin.UpdatedAt, plugin.IsInstalled,
        HasInstaller = !string.IsNullOrWhiteSpace(plugin.InstallerZip),
        HasSource = !string.IsNullOrWhiteSpace(plugin.SourceFolder),
        HasRelease = !string.IsNullOrWhiteSpace(plugin.ReleaseZipUrl) || !string.IsNullOrWhiteSpace(plugin.CatalogReleaseZipUrl),
        HasRepository = !string.IsNullOrWhiteSpace(plugin.RepositoryUrl)
    });

    internal static Result Apply(IReadOnlyList<DeckyPluginInfo> current,
        IReadOnlyList<DeckyPluginInfo> incoming, Func<DeckyPluginInfo, string> identity)
    {
        // Validate before changing any retained object. An ambiguous source must
        // never partially update a model that an installation action also owns.
        var previous = current.ToDictionary(identity, StringComparer.OrdinalIgnoreCase);
        var next = incoming.ToDictionary(identity, StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DeckyPluginInfo>(incoming.Count);
        foreach (var source in incoming)
        {
            var key = identity(source);
            if (!previous.TryGetValue(key, out var retained))
            {
                result.Add(source);
                changed.Add(key);
                continue;
            }
            // README enrichment belongs to this repository and may complete
            // after the offline baseline was cached. Do not erase it on every scan.
            var normalized = new DeckyPluginInfo();
            Copy(source, normalized);
            if (!string.IsNullOrEmpty(retained.Readme) && string.IsNullOrEmpty(normalized.Readme))
            {
                normalized.Readme = retained.Readme;
                normalized.LongDescription = retained.LongDescription;
                if (normalized.Media.Count == 0) normalized.Media = retained.Media;
            }
            if (Snapshot(retained) != Snapshot(normalized))
            {
                var presentationChanged = Presentation(retained) != Presentation(normalized);
                Copy(normalized, retained);
                if (presentationChanged) changed.Add(key);
            }
            result.Add(retained);
        }
        return new(result, changed, previous.Count != next.Count || previous.Keys.Any(key => !next.ContainsKey(key)));
    }

    private static void Copy(DeckyPluginInfo source, DeckyPluginInfo destination)
    {
        foreach (var property in Properties) property.SetValue(destination, property.GetValue(source));
    }

    internal static void Synchronize<T>(ObservableCollection<T> items, IReadOnlyList<T> desired)
    {
        // Insert/move before removing obsolete entries. Existing results never
        // pass through an empty collection during a nonempty refresh.
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < items.Count && EqualityComparer<T>.Default.Equals(items[index], desired[index])) continue;
            var found = items.IndexOf(desired[index]);
            if (found >= 0) items.Move(found, index);
            else items.Insert(index, desired[index]);
        }
        while (items.Count > desired.Count) items.RemoveAt(items.Count - 1);
    }

    internal static IReadOnlyList<IReadOnlyList<DeckyPluginInfo>> Rows(
        IReadOnlyList<IReadOnlyList<DeckyPluginInfo>> current, IReadOnlyList<DeckyPluginInfo> desired, int columns)
    {
        var rows = new List<IReadOnlyList<DeckyPluginInfo>>();
        for (var start = 0; start < desired.Count; start += columns)
        {
            var row = desired.Skip(start).Take(columns).ToArray();
            rows.Add(current.FirstOrDefault(previous => previous.SequenceEqual(row)) ?? row);
        }
        return rows;
    }
}
