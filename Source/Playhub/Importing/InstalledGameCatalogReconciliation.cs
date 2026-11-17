using System.Collections.ObjectModel;
using Playhub.Models;

namespace Playhub.Importing;

// Reconciles a completed explicit scan only. This class does not scan, write
// Steam shortcuts, remove games, or infer that a failed scanner means uninstall.
internal static class InstalledGameCatalogReconciliation
{
    internal static bool Apply(ObservableCollection<UwpGameEntry> current,
        IReadOnlyList<UwpGameEntry> observed)
    {
        var previous = UniqueByIdentity(current);
        var incoming = UniqueByIdentity(observed);
        var next = new List<UwpGameEntry>(observed.Count);
        foreach (var item in observed)
        {
            string? identity = Identity(item);
            if (identity is not null && incoming.ContainsKey(identity) &&
                previous.TryGetValue(identity, out var retained))
            {
                if (SameSource(retained, item)) { next.Add(retained); continue; }
                // Fresh launch arguments and paths always come from this scan.
                // Only the user's pending selection crosses an exact identity.
                item.Selected = retained.Selected;
            }
            next.Add(item);
        }
        if (current.Count == next.Count && current.Zip(next).All(pair => ReferenceEquals(pair.First, pair.Second)))
            return false;
        current.Clear();
        foreach (var item in next) current.Add(item);
        return true;
    }

    private static Dictionary<string, UwpGameEntry> UniqueByIdentity(IEnumerable<UwpGameEntry> items)
    {
        var result = new Dictionary<string, UwpGameEntry>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            string? identity = Identity(item);
            if (identity is null || ambiguous.Contains(identity)) continue;
            if (!result.TryAdd(identity, item)) { result.Remove(identity); ambiguous.Add(identity); }
        }
        return result;
    }

    private static string? Identity(UwpGameEntry item)
    {
        if (item.EpicNamespace.Length > 0 || item.EpicCatalogItemId.Length > 0 || item.EpicAppName.Length > 0)
            return ValidEpicId(item.EpicNamespace) && ValidEpicId(item.EpicCatalogItemId) && ValidEpicId(item.EpicAppName)
                ? "epic\0" + item.EpicNamespace + "\0" + item.EpicCatalogItemId + "\0" + item.EpicAppName : null;
        if (!item.IsLocalExecutable || !Path.IsPathFullyQualified(item.LocalExecutablePath)) return null;
        // GOG currently exposes no provider product ID in UwpGameEntry. Keep
        // its exact launch route, rather than assuming an exe always means one game.
        if (item.SourceLaunchArguments.Contains('\0') || item.SourceWorkingDirectory?.Contains('\0') == true) return null;
        try
        {
            string working = item.SourceWorkingDirectory ?? "";
            if (working.Length > 0 && !Path.IsPathFullyQualified(working)) return null;
            return "local\0" + Path.GetFullPath(item.LocalExecutablePath).ToUpperInvariant() + "\0" +
                item.SourceLaunchArguments + "\0" + (working.Length == 0 ? "" : Path.GetFullPath(working).ToUpperInvariant());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }
    }

    private static bool ValidEpicId(string value) => value.Length is > 0 and <= 256 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    private static bool SameSource(UwpGameEntry a, UwpGameEntry b) =>
        a.Name == b.Name && a.Aumid == b.Aumid && a.Executable == b.Executable && a.Logo == b.Logo &&
        a.PackageFamilyName == b.PackageFamilyName && a.IsLocalExecutable == b.IsLocalExecutable &&
        a.LocalExecutablePath == b.LocalExecutablePath && a.SourceLaunchArguments == b.SourceLaunchArguments &&
        a.SourceWorkingDirectory == b.SourceWorkingDirectory && a.EpicNamespace == b.EpicNamespace &&
        a.EpicCatalogItemId == b.EpicCatalogItemId && a.EpicAppName == b.EpicAppName &&
        a.SteamIdentity == b.SteamIdentity && a.Publisher == b.Publisher && a.FileSize == b.FileSize &&
        a.InSteamLibrary == b.InSteamLibrary && a.SteamGridDbArtworkDisabled == b.SteamGridDbArtworkDisabled &&
        (b.SteamGridDbGameId == 0 || a.SteamGridDbGameId == b.SteamGridDbGameId);
}
