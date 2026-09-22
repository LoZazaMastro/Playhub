using System;

namespace Playhub.Services;

// Formato delle cover scelto nella pagina "Importa Giochi".
// "vertical" e' il comportamento storico (grid ritratto 600x900): resta il
// default cosi' chi aggiorna non vede cambiare nulla.
// "square" chiede a SteamGridDB i grid quadrati 1:1 (1024x1024, 512x512),
// gli stessi che usa il plugin Playhub Artworks (src/hooks/useAssetSearch.tsx,
// setCoverAspect -> dimensions ['1024x1024','512x512']).
public static class CoverFormat
{
    public const string Vertical = "vertical";
    public const string Square = "square";

    public const string VerticalDimensions = "600x900,342x482,660x930";
    public const string SquareDimensions = "1024x1024,512x512";

    public static string Normalize(string? value)
        => string.Equals(value?.Trim(), Square, StringComparison.OrdinalIgnoreCase)
            ? Square
            : Vertical;

    public static bool IsSquare(string? value) => Normalize(value) == Square;

    // Parametro ?dimensions= dell'endpoint grids/game/{id} di SteamGridDB.
    public static string GridDimensions(string? value)
        => IsSquare(value) ? SquareDimensions : VerticalDimensions;

    // Altezza / larghezza della cover nella griglia di importazione.
    public static double CoverAspectRatio(string? value) => IsSquare(value) ? 1.0 : 1.5;

    // Cache separata per formato: cambiando scelta non si riusano le cover vecchie.
    public static string CoverCacheFolder(string? value) => IsSquare(value) ? "covers-square" : "covers";
}
