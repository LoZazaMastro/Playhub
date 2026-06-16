using System.Collections.Generic;

namespace Playhub.Models;

public sealed class DeckyPluginInfo
{
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public bool HasUpdate { get; set; }
    public string ShortDescription { get; set; } = "";
    public string LongDescription { get; set; } = "";
    public string Readme { get; set; } = "";
    public string IconGlyph { get; set; } = "";
    public List<PluginMediaInfo> Media { get; set; } = new();
    public string SourceFolder { get; set; } = "";
    public string? InstallerZip { get; set; }
    public string? Image { get; set; }
    public string? CoverImage { get; set; }
    public string RepositoryUrl { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    public string? ReleaseZipUrl { get; set; }
    public string? ReleasePageUrl { get; set; }
    public string ReleaseNotes { get; set; } = "";
    public string ReleasePublishedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string InstalledFolder { get; set; } = "";
}

public sealed class PluginMediaInfo
{
    public string Url { get; set; } = "";
    public string Kind { get; set; } = "image";
    public string Alt { get; set; } = "";
}
