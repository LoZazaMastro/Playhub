using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.Services;

/// <summary>Read-only, streaming collection of Decky logs into the single support report.</summary>
public static class DeckyDiagnostics
{
    private static readonly Regex Secrets = new(
        "(?i)([\\\"']?(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|passwd|secret|authorization|cookie)[\\\"']?\\s*[:=]\\s*)(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s,;&}]+)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Bearer = new(@"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9+/_.=~-]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Errors = new(@"(?i)\b(error|critical|fatal|exception|traceback|crash|failed|failure)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Timestamp = new(@"\b\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Redact(string text)
    {
        try { return Secrets.Replace(Bearer.Replace(text, "$1 <redacted>"), "$1<redacted>"); }
        catch (RegexMatchTimeoutException) { return "<line omitted: credential redaction timeout>"; }
    }

    public static void Append(TextWriter output, string homebrew, string? steamDirectory = null)
    {
        output.WriteLine("\n---------- DECKY: INSTALLED PLUGINS AND COMPLETE AVAILABLE LOGS ----------");
        output.WriteLine("Snapshot: " + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        output.WriteLine("Local time zone: " + TimeZoneInfo.Local.Id);
        output.WriteLine("Log timestamps retain their source time zone; file dates below are UTC.");
        output.WriteLine("All discovered text logs are included, including rotations and historical plugin folders.");
        output.WriteLine("Configuration files are not collected; .log files in settings folders are included. Recognizable credentials are redacted.");
        output.WriteLine("A missing log is not evidence of a healthy plugin; frontend errors may appear in Steam logs.");
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installed = Directories(Path.Combine(homebrew, "plugins"), output).ToArray();
        foreach (var plugin in installed)
        {
            var folder = Path.GetFileName(plugin);
            var name = folder;
            var version = "unknown";
            try
            {
                name = ReadField(Path.Combine(plugin, "plugin.json"), "name") ?? folder;
                version = ReadField(Path.Combine(plugin, "package.json"), "version")
                    ?? ReadField(Path.Combine(plugin, "plugin.json"), "version") ?? "unknown";
            }
            catch (Exception ex) when (IsFileError(ex) || ex is JsonException)
            { output.WriteLine("Manifest unavailable for " + folder + ": " + Redact(ex.Message)); }
            output.WriteLine("PLUGIN | " + Redact(name) + " | version=" + Redact(version) + " | folder=" + folder);
            var before = files.Count;
            Collect(Path.Combine(homebrew, "logs", folder), "Decky/logs/" + folder, files, output, true);
            Collect(plugin, "Decky/plugins/" + folder, files, output, false);
            foreach (var sub in new[] { "logs", "log" })
                Collect(Path.Combine(plugin, sub), "Decky/plugins/" + folder + "/" + sub, files, output, true);
            output.WriteLine("  Discovered log files: " + (files.Count - before) + " (folder match; other historical names are included below)");
        }
        if (installed.Length == 0) output.WriteLine("<no installed plugin folders found>");
        // Include old names/uninstalled plugins too: folder names are not always manifest names.
        Collect(Path.Combine(homebrew, "logs"), "Decky/logs", files, output, true);
        Collect(Path.Combine(homebrew, "settings"), "Decky/settings", files, output, true, strictLogs: true);
        Collect(homebrew, "Decky", files, output, false);
        Collect(Path.Combine(homebrew, "services"), "Decky/services", files, output, false);
        Collect(Path.Combine(homebrew, "services", "logs"), "Decky/services/logs", files, output, true);
        if (!string.IsNullOrWhiteSpace(steamDirectory))
        {
            var steamLogs = Path.Combine(steamDirectory, "logs");
            foreach (var path in FilePaths(steamLogs, output))
            {
                var name = Path.GetFileName(path);
                if (new[] { "cef", "webhelper", "console", "gameoverlay", "bootstrap" }
                    .Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) && IsLog(path))
                    files.TryAdd(path, "Steam/logs/" + name);
            }
        }
        output.WriteLine("\nLOG INDEX (complete source snapshots; no tail-only limit)");
        var index = 0;
        foreach (var entry in files) output.WriteLine($"[{++index:D4}] {entry.Value}");
        var errorSummary = new PriorityQueue<(string Time, string Source, int Line, string Text), string>(StringComparer.Ordinal);
        var totalErrors = 0;
        var unreadable = 0;
        index = 0;
        foreach (var entry in files)
        {
            output.WriteLine($"\n===== LOG [{++index:D4}] {entry.Value} =====");
            try
            {
                using var file = new FileStream(entry.Key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var length = file.Length;
                output.WriteLine($"Snapshot bytes: {length}; last write UTC: {File.GetLastWriteTimeUtc(entry.Key):O}");
                // Bound the read to the opening length, even while the plugin continues writing.
                using var snapshot = new SnapshotStream(file, length);
                using Stream decoded = entry.Key.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? new GZipStream(snapshot, CompressionMode.Decompress, leaveOpen: true) : snapshot;
                using var reader = new StreamReader(decoded, Encoding.UTF8, true);
                string? line;
                var lineNumber = 0;
                while ((line = reader.ReadLine()) is not null)
                {
                    lineNumber++;
                    var safe = Redact(line);
                    output.WriteLine(safe);
                    if (!Errors.IsMatch(safe)) continue;
                    totalErrors++;
                    // Only the navigational summary is bounded. Full lines stay above.
                    var timestamp = Timestamp.Match(safe).Value;
                    errorSummary.Enqueue((timestamp, entry.Value, lineNumber,
                        safe.Length > 400 ? safe[..400] + "..." : safe), timestamp);
                    if (errorSummary.Count > 2000) errorSummary.Dequeue();
                }
                output.WriteLine($"===== END LOG [{index:D4}] ({lineNumber} lines) =====");
            }
            catch (Exception ex) when (IsFileError(ex) || ex is RegexMatchTimeoutException)
            {
                unreadable++;
                output.WriteLine("<log unavailable or incomplete: " + Redact(ex.Message) + ">");
            }
        }
        output.WriteLine("\n---------- CROSS-COMPONENT ERROR INDEX ----------");
        output.WriteLine($"Files: {files.Count}; unreadable/incomplete: {unreadable}; error-like lines: {totalErrors}.");
        output.WriteLine("Heuristic index, not a crash diagnosis. Times are as logged, without assumed time-zone conversion.");
        if (totalErrors > errorSummary.Count) output.WriteLine("Index limited to 2000 latest timestamped entries; remaining errors are included in the full logs above.");
        foreach (var item in errorSummary.UnorderedItems.Select(entry => entry.Element).OrderBy(item => item.Time, StringComparer.Ordinal))
            output.WriteLine($"{item.Time} | {item.Source}:{item.Line} | {item.Text}");
    }

    private static string? ReadField(string path, string field)
    {
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 1024 * 1024) return null;
        using var json = JsonDocument.Parse(file);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Manifest must be an object.");
        return json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private static bool IsLog(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".log.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".jsonl.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Entries(string path, TextWriter output, bool directories)
    {
        try
        {
            if (!Directory.Exists(path)) return Array.Empty<string>();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            { output.WriteLine("<linked directory skipped: " + path + ">"); return Array.Empty<string>(); }
            return (directories ? Directory.GetDirectories(path) : Directory.GetFiles(path))
                .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (IsFileError(ex))
        { output.WriteLine("<directory unavailable: " + path + ": " + Redact(ex.Message) + ">"); return Array.Empty<string>(); }
    }
    private static IEnumerable<string> Directories(string path, TextWriter output) => Entries(path, output, true);
    private static IEnumerable<string> FilePaths(string path, TextWriter output) => Entries(path, output, false);
    private static void Collect(string root, string label, IDictionary<string, string> files, TextWriter output, bool recursive, bool strictLogs = false)
    {
        foreach (var path in FilePaths(root, output))
            if (IsLog(path) && (recursive || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                && (!strictLogs || path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains(".log.", StringComparison.OrdinalIgnoreCase)))
                files.TryAdd(path, label + "/" + Path.GetFileName(path));
        if (!recursive) return;
        foreach (var directory in Directories(root, output))
            Collect(directory, label + "/" + Path.GetFileName(directory), files, output, true, strictLogs);
    }

    private sealed class SnapshotStream(Stream source, long remaining) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, (int)Math.Min(count, remaining));
            remaining -= read;
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
