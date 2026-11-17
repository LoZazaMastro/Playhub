// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

internal static class StoreXboxLocalLibrary
{
    internal static StoreXboxOwnedGame[] Merge(IEnumerable<StoreXboxOwnedGame> account,
        IReadOnlyList<WindowsXboxPackage> packages, IReadOnlyList<StoreXboxOwnedGame> metadata)
    {
        var result = account.ToDictionary(g => g.ProductId, StringComparer.Ordinal);
        foreach (var group in packages.Where(p => p.StatusOk && p.StoreProductId is not null && p.Aumid is not null && p.ExecutablePath is not null)
            .GroupBy(p => p.StoreProductId!, StringComparer.Ordinal))
        {
            // Two installed editions for one product need an explicit choice.
            if (group.Count() != 1) continue;
            var package = group.Single();
            var knownFamily = result.Values.Where(g => g.AccessKind == "owned" && string.Equals(g.PackageFamilyName, package.PackageFamilyName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (knownFamily.Length == 1) continue;
            var prior = result.GetValueOrDefault(group.Key);
            var art = metadata.SingleOrDefault(g => g.ProductId == group.Key);
            string? title = prior?.Title ?? art?.Title ?? package.DisplayName;
            if (string.IsNullOrWhiteSpace(title)) continue;
            result[group.Key] = new(group.Key, title, true, prior?.SupportsXbox ?? art?.SupportsXbox ?? false,
                prior?.AccessKind == "owned" ? "owned" : "installed", prior?.ArtworkUrl ?? art?.ArtworkUrl,
                package.PackageFamilyName, prior?.RequiredBytes ?? art?.RequiredBytes,
                prior?.ArtworkFallbackUrls ?? art?.ArtworkFallbackUrls,InstallProvider:prior?.InstallProvider??art?.InstallProvider,InstallProviderId:prior?.InstallProviderId??art?.InstallProviderId);
        }
        return result.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
