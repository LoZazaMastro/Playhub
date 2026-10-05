using System.Text.RegularExpressions;

namespace Playhub.Importing;

internal static class MediaDisplayName
{
    internal static string Resolve(string? acquiredTitle,string filePath)
    {
        if(!string.IsNullOrWhiteSpace(acquiredTitle))return acquiredTitle.Trim();
        string name=Path.GetFileNameWithoutExtension(filePath);
        // yt-dlp's verified-length ID suffix is technical data; retain other bracketed title text.
        return Regex.Replace(name,@"\s*\[[A-Za-z0-9_-]{11}\]$","").Replace('_',' ').Trim();
    }
}
