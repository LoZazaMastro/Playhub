// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.Services;

internal static class SplashLogoSelection
{
    internal static string FromSavedPath(string? savedPath, string assetsRoot, IEnumerable<string> presetKeys)
    {
        if (string.IsNullOrWhiteSpace(savedPath)) return "playhub";
        try
        {
            string saved = Path.GetFullPath(savedPath);
            foreach (string key in presetKeys.Where(key => key != "custom"))
            {
                string candidate = key == "playhub" ? Path.Combine(assetsRoot, "base-logo.png")
                    : Path.Combine(assetsRoot, "logos", key + ".png");
                if (saved.Equals(Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase)) return key;
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
        // A custom image with a preset's filename is still a custom image.
        return "custom";
    }
}
