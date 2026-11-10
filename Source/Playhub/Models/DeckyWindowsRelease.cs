using System;

namespace Playhub.Models;

public sealed record DeckyWindowsRelease(string Tag, DateTimeOffset PublishedAt)
{
    public string Display => $"{Tag} · {PublishedAt.ToLocalTime():yyyy-MM-dd}";
}
