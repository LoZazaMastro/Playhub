using System;
using System.Collections.Generic;

namespace Playhub.Models;

// Schema 3 extends the bundled external catalog with a monotonic publication revision.
public sealed record RemotePluginCatalog
{
    public int SchemaVersion { get; init; } = 3;
    public long CatalogRevision { get; init; }
    public string VerifiedAt { get; init; } = "";
    public string OfficialDeckyCatalogUrl { get; init; } = "https://plugins.deckbrew.xyz/plugins";
    public IReadOnlyList<RemotePluginCatalogEntry> Plugins { get; init; } = Array.Empty<RemotePluginCatalogEntry>();
}

public sealed record RemotePluginCatalogEntry
{
    public bool Active { get; init; } = true;
    public string Name { get; init; } = "";
    public string InstallFolder { get; init; } = "";
    public string Author { get; init; } = "";
    public string Repository { get; init; } = "";
    public string RepositoryUrl { get; init; } = "";
    public string Version { get; init; } = "";
    public string ReleaseAssetName { get; init; } = "";
    public string CatalogReleaseUrl { get; init; } = "";
    public string ReleasePublishedAt { get; init; } = "";
    public string Category { get; init; } = "";
    public string ShortDescription { get; init; } = "";
    public string LongDescription { get; init; } = "";
    public string CoverUrl { get; init; } = "";
    public string IconGlyph { get; init; } = "";
    public string CatalogStatus { get; init; } = "github";
    public string CatalogSource { get; init; } = "outside-store";
    public int CatalogPluginId { get; init; }
    public string Compatibility { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}

public sealed record RemotePluginCatalogResult(RemotePluginCatalog Catalog, string Origin, string? Error = null);
