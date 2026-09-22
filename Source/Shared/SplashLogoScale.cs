using System.IO;

namespace Playhub.Shared;

internal static class SplashLogoScale
{
    internal static double ForPath(string? path) => Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant() switch
    {
        "xbox" => 2,
        "playstation" or "steam-deck" or "rog" => 1.5,
        "base-logo" or "steamos" or "msi" => 1.2,
        _ => 1
    };
}
