using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.GameSession;

// A companion uses DWM composition. Keep an eligible Unreal game's original
// full-display presentation, and restore its three display fields after exit.
internal sealed class UnrealDisplayLease : IDisposable
{
    private const int MaxSettingsBytes = 1024 * 1024;
    private static readonly string[] Keys = ["FullscreenMode", "LastConfirmedFullscreenMode", "PreferredFullscreenMode"];
    private readonly string _settings, _journal;
    private readonly FileStream _lock;
    private readonly Change _change;
    private readonly Func<bool> _gameIsAbsent;
    private readonly Action<string> _log;
    private bool _disposed;

    internal sealed record Change(int Version, int OwnerPid, long OwnerBirth, string Executable,
        string Settings, string Section, string Before, string After, bool Applied);
    internal sealed record Layout(string Executable, string Settings);
    internal sealed record Modes(string Section, int[] Values, int[] Offsets, Encoding Encoding);

    private UnrealDisplayLease(Layout layout, string journal, FileStream fileLock, Change change,
        Func<bool> gameIsAbsent, Action<string> log) =>
        (_settings, _journal, _lock, _change, _gameIsAbsent, _log) =
            (layout.Settings, journal, fileLock, change, gameIsAbsent, log);

    internal static UnrealDisplayLease? TryAcquire(string executable, Action<string> log)
    {
        bool enabled = AllowsBuild(typeof(UnrealDisplayLease).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>());
        if (!enabled || !Win32CompanionLease.TryReadGameId(Environment.GetEnvironmentVariable("SteamGameId"), out _)
            || !string.Equals(Environment.ProcessPath, Path.Combine(AppContext.BaseDirectory, "Playhub.GameSession.exe"), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(AppContext.BaseDirectory, "Playhub.XboxSession.exe"))
            || !File.Exists(Path.Combine(AppContext.BaseDirectory, "PlayhubSteamCompanion.dll"))) return null;
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return AcquireForProfile(executable, profile, () => IsGameAbsent(executable), OwnerIsGone, log);
        }
        catch (Exception error) when (Expected(error))
        {
            log("Unreal display preparation unavailable: " + error.Message);
            return null;
        }
    }

    internal static bool AllowsBuild(IEnumerable<AssemblyMetadataAttribute> attributes)
    {
        var metadata = attributes.ToArray();
        return metadata.Any(value => value.Key == "PlayhubWin32SteamCompanion" && value.Value == "true")
            && metadata.Any(value => value.Key == "PlayhubWin32BorderlessSession" && value.Value == "true");
    }

    internal static UnrealDisplayLease? AcquireForProfile(string executable, string profile, Func<bool> gameIsAbsent,
        Func<Change, bool> ownerIsGone, Action<string> log)
    {
        Layout? layout = Resolve(executable, profile);
        if (layout is null) return null;
        RequireAbsent(gameIsAbsent);
        string folder = Path.Combine(profile, "Playhub", "game-session", "unreal-display");
        string key = Digest(Encoding.UTF8.GetBytes(layout.Settings.ToUpperInvariant()));
        string journal = Path.Combine(folder, key + ".json"), lockPath = Path.Combine(folder, key + ".lock");
        RequireLocalPath(profile, lockPath);
        Directory.CreateDirectory(folder);
        RequireLocalPath(profile, lockPath);
        var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        bool acquired = false;
        try
        {
            RequireLocalPath(profile, journal);
            if (ReadJournal(journal) is { } previous)
            {
                Validate(previous, layout);
                if (!ownerIsGone(previous)) throw new InvalidOperationException("An earlier Unreal display session is still active or could not be verified.");
                Restore(layout.Settings, journal, previous, gameIsAbsent);
                log("Unreal display: recovered the earlier session.");
            }
            using var original = OpenSettings(layout.Settings);
            byte[] before = ReadSmall(original, MaxSettingsBytes);
            Modes modes = Parse(before);
            // Respect windowed/borderless choices and mixed user preferences.
            // Only the observed all-exclusive state is temporarily adapted.
            if (modes.Values.Any(value => value != 0)) return null;
            byte[] after = SetModes(before, modes, 1);
            using var self = Process.GetCurrentProcess();
            var change = new Change(1, self.Id, self.StartTime.ToFileTimeUtc(), layout.Executable,
                layout.Settings, modes.Section, Digest(before), Digest(after), Applied: false);
            RequireAbsent(gameIsAbsent);
            WriteJournal(journal, change);
            try
            {
                Commit(layout.Settings, before, after, gameIsAbsent);
                WriteJournal(journal, change with { Applied = true });
            }
            catch
            {
                try { Restore(layout.Settings, journal, change, gameIsAbsent); }
                catch (Exception error) when (Expected(error)) { log("Unreal display: recovery record retained. " + error.Message); }
                throw;
            }
            change = change with { Applied = true };
            acquired = true;
            log("Unreal display: using borderless fullscreen for this Steam companion session.");
            return new(layout, journal, fileLock, change, gameIsAbsent, log);
        }
        finally { if (!acquired) fileLock.Dispose(); }
    }

    internal static Layout? Resolve(string executable, string profile)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(profile)) return null;
        executable = Path.GetFullPath(executable);
        profile = Path.GetFullPath(profile);
        RequireLocalPath(Path.GetPathRoot(profile)!, profile);
        if (!File.Exists(executable)) return null;
        var win64 = Directory.GetParent(executable);
        var binaries = win64?.Parent;
        var project = binaries?.Parent;
        if (win64?.Name != "Win64" || binaries?.Name != "Binaries" || project is null
            || !Regex.IsMatch(project.Name, @"\A[A-Za-z][A-Za-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)) return null;
        string file = Path.GetFileName(executable);
        if (!file.Equals(project.Name + ".exe", StringComparison.OrdinalIgnoreCase)
            && !file.Equals(project.Name + "-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)) return null;
        RequireLocalPath(Path.GetPathRoot(executable)!, executable);
        string paks = Path.Combine(project.FullName, "Content", "Paks");
        if (!Directory.Exists(paks)) return null;
        RequireLocalPath(project.FullName, paks);
        bool container = Directory.EnumerateFiles(paks, "*", SearchOption.TopDirectoryOnly).Take(128).Any(path =>
        {
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".pak", StringComparison.OrdinalIgnoreCase)) return false;
            RequireLocalPath(project.FullName, path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase))
            {
                Span<byte> magic = stackalloc byte[16];
                return stream.Read(magic) == magic.Length && magic.SequenceEqual("-==--==--==--==-"u8);
            }
            // Pak files carry their versioned index footer at the end.
            byte[] tail = new byte[(int)Math.Min(stream.Length, 512)];
            stream.Position = stream.Length - tail.Length;
            stream.ReadExactly(tail);
            for (int index = 0; index <= tail.Length - 44; index++)
            {
                ReadOnlySpan<byte> footer = tail.AsSpan(index);
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(footer) != 0x5A6F12E1) continue;
                int version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(footer[4..]);
                long offset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(footer[8..]);
                long size = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(footer[16..]);
                if (version is >= 1 and <= 12 && offset >= 0 && size > 0 && offset <= stream.Length - size) return true;
            }
            return false;
        });
        if (!container) return null;
        string[] candidates = [Path.Combine(profile, project.Name, "Saved", "Config", "Windows", "GameUserSettings.ini"),
            Path.Combine(profile, project.Name, "Saved", "Config", "WindowsNoEditor", "GameUserSettings.ini")];
        var present = candidates.Where(File.Exists).ToArray();
        if (present.Length != 1) return null;
        RequireLocalPath(profile, present[0]);
        using (var stream = OpenSettings(present[0])) Parse(ReadSmall(stream, MaxSettingsBytes));
        return new(executable, present[0]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using (_lock)
        {
            try
            {
                if (ReadJournal(_journal) != _change) throw new InvalidDataException("Unreal display journal ownership changed.");
                Restore(_settings, _journal, _change, _gameIsAbsent);
                _log("Unreal display: restored the original fullscreen preference.");
            }
            catch (Exception error) when (Expected(error)) { _log("Unreal display: recovery record retained. " + error.Message); }
        }
    }

    private static void Restore(string path, string journal, Change change, Func<bool> gameIsAbsent)
    {
        RequireAbsent(gameIsAbsent);
        RequireLocalPath(Path.GetPathRoot(path)!, path);
        using var source = OpenSettings(path);
        byte[] current = ReadSmall(source, MaxSettingsBytes);
        Modes modes = Parse(current);
        if (modes.Section != change.Section) throw new InvalidDataException("Unreal display settings section changed during the session.");
        if (!change.Applied && Digest(current) != change.Before && Digest(current) != change.After)
            throw new InvalidDataException("An incomplete Unreal display change no longer matches its recovery record.");
        if (modes.Values.All(value => value == 1)) Commit(path, current, SetModes(current, modes, 0), gameIsAbsent);
        else if (modes.Values.Any(value => value != 0))
            throw new InvalidDataException("Unreal display mode fields changed during the session; user changes were preserved.");
        File.Delete(journal);
    }

    internal static Modes Parse(byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaxSettingsBytes) throw new InvalidDataException("Unsupported Unreal settings length.");
        int prefix = 0;
        Encoding encoding = new UTF8Encoding(false, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) prefix = 3;
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { prefix = 2; encoding = new UnicodeEncoding(false, false, true); }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { prefix = 2; encoding = new UnicodeEncoding(true, false, true); }
        string text = encoding.GetString(bytes, prefix, bytes.Length - prefix);
        if (text.Contains('\0')) throw new InvalidDataException("Unsupported Unreal settings encoding.");
        string section = "", selected = "";
        int[] values = [-1, -1, -1], offsets = [-1, -1, -1];
        foreach (Match line in Regex.Matches(text, @"[^\r\n]+", RegexOptions.CultureInvariant))
        {
            string content = line.Value.Trim();
            if (content.StartsWith(';') || content.StartsWith('#')) continue;
            if (content.StartsWith('['))
            {
                if (!content.EndsWith(']')) throw new InvalidDataException("Malformed Unreal settings section.");
                section = content[1..^1];
                continue;
            }
            int equals = content.IndexOf('=');
            if (equals < 0) continue;
            string key = content[..equals].Trim();
            int slot = Array.FindIndex(Keys, item => item.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (slot < 0) continue;
            if (values[slot] >= 0 || !Regex.IsMatch(section, @"\A/Script/[A-Za-z0-9_]+\.[A-Za-z0-9_]*GameUserSettings\z", RegexOptions.CultureInvariant)
                || (selected.Length > 0 && selected != section)) throw new InvalidDataException("Ambiguous Unreal display mode fields.");
            var match = Regex.Match(line.Value, @"\A[ \t]*[A-Za-z]+[ \t]*=[ \t]*(?<mode>[012])[ \t]*(?:;[^\r\n]*)?\z", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidDataException("Unsupported Unreal display mode value.");
            selected = section;
            values[slot] = match.Groups["mode"].Value[0] - '0';
            offsets[slot] = prefix + encoding.GetByteCount(text.AsSpan(0, line.Index + match.Groups["mode"].Index));
        }
        if (values.Any(value => value < 0)) throw new InvalidDataException("Unreal display settings are incomplete.");
        return new(selected, values, offsets, encoding);
    }

    internal static byte[] SetModes(byte[] before, Modes modes, int value)
    {
        if (value is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(value));
        byte[] updated = (byte[])before.Clone(), token = modes.Encoding.GetBytes(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (int offset in modes.Offsets) token.CopyTo(updated, offset);
        return updated;
    }

    private static bool IsGameAbsent(string executable)
    {
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)); }
        catch (Exception error) when (Expected(error)) { return false; }
        try
        {
            foreach (var process in candidates)
            {
                try
                {
                    if (!process.HasExited && (process.MainModule?.FileName is not { } image
                        || image.Equals(executable, StringComparison.OrdinalIgnoreCase))) return false;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { return false; }
            }
            return true;
        }
        finally { foreach (var process in candidates) process.Dispose(); }
    }

    private static bool OwnerIsGone(Change change)
    {
        try { using var owner = Process.GetProcessById(change.OwnerPid); return owner.HasExited || owner.StartTime.ToFileTimeUtc() != change.OwnerBirth; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    private static void Validate(Change change, Layout layout)
    {
        if (change.Version != 1 || change.OwnerPid <= 0 || change.OwnerBirth <= 0 || change.Executable != layout.Executable
            || change.Settings != layout.Settings || string.IsNullOrEmpty(change.Section) || !IsDigest(change.Before) || !IsDigest(change.After)
            || change.Before == change.After) throw new InvalidDataException("Unreal display journal is invalid.");
    }

    private static bool IsDigest(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException
        or InvalidDataException or JsonException or DecoderFallbackException or Win32Exception;
    private static void RequireAbsent(Func<bool> gameIsAbsent)
    {
        if (!gameIsAbsent()) throw new InvalidOperationException("The exact Unreal game must be closed before changing its display preference.");
    }

    private static void RequireLocalPath(string root, string path)
    {
        root = Path.GetFullPath(root);
        path = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, path);
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative.Contains(':'))
            throw new InvalidDataException("Unreal display paths must remain local and within the expected directory.");
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("An Unreal display root is redirected.");
        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            try { if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("An Unreal display path is redirected."); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
    }

    private static FileStream OpenSettings(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
            throw new InvalidDataException("The Unreal settings file is redirected or read-only.");
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    }
    private static byte[] ReadSmall(Stream stream, int max)
    {
        if (stream.Length < 1 || stream.Length > max) throw new InvalidDataException("Unreal display data exceeds its supported length.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
    private static Change? ReadJournal(string path)
    {
        try
        {
            RequireLocalPath(Path.GetPathRoot(path)!, path);
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Change>(ReadSmall(stream, 8192)) ?? throw new InvalidDataException("The Unreal display journal is empty.");
        }
        catch (FileNotFoundException) { return null; }
    }
    private static void WriteJournal(string path, Change change) => Publish(path, JsonSerializer.SerializeToUtf8Bytes(change), pending => File.Move(pending, path, true));
    private static void Commit(string path, byte[] expected, byte[] updated, Func<bool> gameIsAbsent) => Publish(path, updated, pending =>
    {
        RequireAbsent(gameIsAbsent);
        RequireLocalPath(Path.GetPathRoot(path)!, path);
        using var source = OpenSettings(path);
        if (!ReadSmall(source, MaxSettingsBytes).AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Unreal settings changed before the update could be committed.");
        File.Replace(pending, path, null);
    });
    private static void Publish(string path, byte[] content, Action<string> publish)
    {
        string pending = Path.Combine(Path.GetDirectoryName(path)!, Path.GetRandomFileName());
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(content); stream.Flush(true); }
            publish(pending);
        }
        finally { File.Delete(pending); }
    }
}
