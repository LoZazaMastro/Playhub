using System.IO;
using System.Text.Json;

namespace GamingMode.Services;

internal sealed record LivePerformanceOptions(bool Enabled = false, int Detail = 1, int Corner = 1,
    bool Transparent = false, int RefreshMs = LivePerformanceCadence.Milliseconds, string TextColor = "white")
{
    internal bool Valid => Detail is >= 1 and <= 4 && Corner is >= 1 and <= 6 && LivePerformanceCadence.Accepted(RefreshMs)
        && LivePerformanceHudPalette.Valid(TextColor);
    internal LivePerformanceOptions Normalized() => this with { RefreshMs = LivePerformanceCadence.Milliseconds };
    internal LivePerformanceHudSettings Hud => new(Enabled, Detail, Corner switch {
        1 => HudPosition.TopLeft, 2 => HudPosition.TopCenter, 3 => HudPosition.TopRight,
        4 => HudPosition.BottomLeft, 5 => HudPosition.BottomCenter, _ => HudPosition.BottomRight
    }, !Transparent, TextColor: TextColor);
}

// This is an app preference, separate from Windows and Steam settings. A lease
// is deliberately never persisted: restarting the agent starts no ETW worker.
internal sealed class LivePerformanceSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _directory, _path;
    internal LivePerformanceSettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode")) { }
    internal LivePerformanceSettingsStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "performance-overlay.json");
    }

    internal LivePerformanceOptions Load()
    {
        try {
            if (!SafePath() || !File.Exists(_path)) return new();
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 4096) return new();
            var value = JsonSerializer.Deserialize<LivePerformanceOptions>(stream, Json);
            return value is { Valid: true } ? value.Normalized() : new();
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal bool Save(LivePerformanceOptions value)
    {
        if (!value.Valid || !SafePath()) return false;
        string temp = Path.Combine(_directory, ".performance-overlay-" + Guid.NewGuid().ToString("N") + ".tmp");
        try {
            Directory.CreateDirectory(_directory);
            if (!SafePath()) return false;
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough)) {
                JsonSerializer.Serialize(stream, value.Normalized(), Json); stream.Flush(true);
            }
            if (!SafePath()) return false;
            File.Move(temp, _path, overwrite: true);
            return true;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private bool SafePath()
    {
        try {
            for (DirectoryInfo? part = new(_directory); part is not null; part = part.Parent)
                if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return !File.Exists(_path) || (File.GetAttributes(_path) & FileAttributes.ReparsePoint) == 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
