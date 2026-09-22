using System.Text.RegularExpressions;
using Playhub.Models;
using Playhub.Services;

var passed = 0;

// 1. Default: verticale, cosi' chi aggiorna non vede cambiare nulla.
Check(new PlayhubSettings().CoverFormat == CoverFormat.Vertical, "default cover format is vertical");
Check(CoverFormat.Normalize(new PlayhubSettings().CoverFormat) == CoverFormat.Vertical, "default normalizes to vertical");
Check(CoverFormat.Normalize(null) == CoverFormat.Vertical, "null falls back to vertical");
Check(CoverFormat.Normalize("boh") == CoverFormat.Vertical, "unknown value falls back to vertical");
Check(CoverFormat.Normalize(" SQUARE ") == CoverFormat.Square, "square is recognized case/space insensitively");
Check(!CoverFormat.IsSquare(CoverFormat.Vertical) && CoverFormat.IsSquare(CoverFormat.Square), "IsSquare");

// 2. Il parametro di fetch di SteamGridDB cambia con la scelta.
Check(CoverFormat.GridDimensions(CoverFormat.Vertical) == "600x900,342x482,660x930", "vertical keeps today's portrait dimensions");
Check(CoverFormat.GridDimensions(CoverFormat.Square) == "1024x1024,512x512", "square asks SteamGridDB for 1:1 grids");
Check(CoverFormat.GridDimensions(CoverFormat.Square) != CoverFormat.GridDimensions(CoverFormat.Vertical), "fetch parameter differs between formats");

// 3. La griglia di importazione cambia forma con la scelta.
Check(Math.Abs(CoverFormat.CoverAspectRatio(CoverFormat.Vertical) - 1.5) < 0.0001, "vertical grid keeps the 2:3 cover");
Check(Math.Abs(CoverFormat.CoverAspectRatio(CoverFormat.Square) - 1.0) < 0.0001, "square grid uses a 1:1 cover");
Check(CoverFormat.CoverCacheFolder(CoverFormat.Vertical) != CoverFormat.CoverCacheFolder(CoverFormat.Square), "each format caches its own covers");

// 4. Regressioni sulle sorgenti: nessuna dimensione dei grid resta cablata,
//    e la griglia usa davvero il rapporto del formato scelto.
var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "Source", "Playhub"))) repository = repository.Parent;
if (repository is null) throw new Exception("Repository not found for cover format regression checks.");
var source = Path.Combine(repository.FullName, "Source", "Playhub");
var service = File.ReadAllText(Path.Combine(source, "Services", "UwpXboxService.cs"));
var window = File.ReadAllText(Path.Combine(source, "MainWindow.xaml.cs"));
var localization = File.ReadAllText(Path.Combine(source, "Services", "LocalizationService.cs"));

Check(!service.Contains("dimensions=600x900") && !service.Contains("dimensions=1024x1024"), "no hardcoded cover dimensions left in the SteamGridDB requests");
foreach (var dimensions in new[] { "coverDimensions", "gridsDimensions" })
{
    Check(service.Contains($"var {dimensions} = global::Playhub.Services.CoverFormat.GridDimensions(CoverFormat);"), "cover dimensions follow the selected format: " + dimensions);
    Check(service.Contains("grids/game/{gameId}?dimensions={" + dimensions + "}"), "cover request consumes the selected dimensions: " + dimensions);
}
Check(service.Contains("CoverFormat.CoverCacheFolder(CoverFormat)"), "the cover cache is scoped to the selected format");
Check(window.Contains("CoverFormat.CoverAspectRatio(_settings.CoverFormat)"), "the import grid uses the selected cover aspect ratio");
Check(!window.Contains("args.NewSize.Width * 1.5"), "the import grid no longer hardcodes the vertical ratio");
Check(window.Contains("\"Formato delle cover\"") && window.Contains("\"Verticali\"") && window.Contains("\"Quadrate\""), "the cover format card exists on the import page");

foreach (var key in new[] { "Formato delle cover", "Verticali", "Quadrate" })
{
    var line = localization.Split('\n').SingleOrDefault(l => l.TrimStart().StartsWith("[\"" + key + "\"] = V("));
    Check(line is not null, "localized cover format string: " + key);
    Check(Regex.Matches(line!.Split("= V(", 2)[1], "\"[^\"]*\"").Count == 11, "all translations: " + key);
}

// 5. La selezione manuale degli artwork segue il formato scelto: con le cover quadrate
//    una cella verticale ritaglierebbe proprio l'artwork che l'utente sta scegliendo.
Check(window.Contains("ArtworkPreviewSize(artworkType, _settings.CoverFormat)"),
    "l'anteprima del selettore riceve il formato scelto");
Check(window.Contains("IsSquare(coverFormat) ? (170d, 170d) : (150d, 225d)"),
    "cella quadrata con le cover quadrate, verticale altrimenti");
Check(!window.Contains("Stretch = artworkType == \"cover\" ? Stretch.UniformToFill : Stretch.Uniform"),
    "nel selettore l'artwork si vede intero, non ritagliato dalla cella");

Console.WriteLine($"PASS: {passed} focused cover format checks.");

void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++;
}
