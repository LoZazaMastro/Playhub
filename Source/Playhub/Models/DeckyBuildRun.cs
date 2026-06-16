namespace Playhub.Models;

public sealed class DeckyBuildRun
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string HeadSha { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
    public string Url { get; init; } = "";
    public string ArtifactsUrl { get; init; } = "";
    public string Display => $"{CreatedAt} - {Title}";
}
