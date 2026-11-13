// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class CupheadWindowedPreference : IDisposable
{
    private const string Family = "StudioMDHR.20872A364DAA1_tm1s6a95559gt";
    internal const string SupportedPackage = "StudioMDHR.20872A364DAA1_1.3.8.2_x64__tm1s6a95559gt";
    internal const string Aumid = Family + "!App";
    private const int MaxPreferencesBytes = 8 * 1024 * 1024;
    private readonly string _preferences;
    private readonly string _journal;
    private readonly FileStream _lock;
    private readonly Func<bool> _gameIsAbsent;
    private readonly Action<string> _log;
    private readonly Change _change;
    private bool _disposed;

    internal sealed record Change(int Version, string Package, int Pid, long Birth, int Width, int Height,
        string Before, string After, bool Applied);

    internal readonly record struct DisplayPreference(bool Fullscreen, int Width, int Height,
        int LengthOffset, int RecordLength, int TokenOffset, int TokenLength);

    private CupheadWindowedPreference(string preferences, string journal, FileStream fileLock, Change change,
        Func<bool> gameIsAbsent, Action<string> log)
        => (_preferences, _journal, _lock, _change, _gameIsAbsent, _log) =
            (preferences, journal, fileLock, change, gameIsAbsent, log);

    internal static CupheadWindowedPreference? TryAcquire(string aumid, string executable, string packageFullName,
        Func<bool> gameIsAbsent, Action<string> log)
    {
        if (!Eligible(aumid, executable, packageFullName)) return null;
        return AcquireForProfile(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            gameIsAbsent, OwnerIsGone, log);
    }

    internal static bool Eligible(string aumid, string executable, string packageFullName) =>
        string.Equals(aumid, Aumid, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(executable), "Cuphead.exe", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(packageFullName, SupportedPackage, StringComparison.OrdinalIgnoreCase);

    internal static CupheadWindowedPreference? AcquireForProfile(string profile, Func<bool> gameIsAbsent,
        Func<Change, bool> ownerIsGone, Action<string> log)
    {
        if (!Path.IsPathFullyQualified(profile)) throw new ArgumentException("The local profile is required.");
        string preferences = Path.Combine(profile, "Packages", Family, "LocalState", "playerprefs.dat");
        string folder = Path.Combine(profile, "Playhub", "xbox-session", "cuphead-display");
        string journal = Path.Combine(folder, "pending.json");
        string lockPath = Path.Combine(folder, "session.lock");
        if (!File.Exists(preferences) && !File.Exists(journal)) return null;
        RequireGameClosed(gameIsAbsent);
        RequireLocalPath(profile, preferences);
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
                Validate(previous);
                if (!ownerIsGone(previous)) throw new InvalidOperationException("An earlier Cuphead display session is still active or could not be verified.");
                Restore(preferences, journal, previous, gameIsAbsent);
                log("Cuphead display: recovered the earlier session.");
            }

            using var original = OpenPreferences(preferences);
            byte[] before = ReadSmall(original, MaxPreferencesBytes);
            DisplayPreference display = Parse(before);
            if (!display.Fullscreen) return null;
            byte[] after = SetFullscreen(before, display, false);
            using var self = Process.GetCurrentProcess();
            var change = new Change(1, SupportedPackage, self.Id, self.StartTime.ToFileTimeUtc(),
                display.Width, display.Height, Digest(before), Digest(after), Applied: false);
            RequireGameClosed(gameIsAbsent);
            WriteJournal(journal, change);
            try
            {
                Commit(preferences, before, after, gameIsAbsent);
                WriteJournal(journal, change with { Applied = true });
            }
            catch
            {
                try { Restore(preferences, journal, change, gameIsAbsent); }
                catch (Exception error) { log("Cuphead display: recovery record retained. " + error.Message); }
                throw;
            }
            change = change with { Applied = true };
            acquired = true;
            log("Cuphead display: using windowed mode for this Steam overlay session.");
            return new(preferences, journal, fileLock, change, gameIsAbsent, log);
        }
        finally { if (!acquired) fileLock.Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using (_lock)
        {
            try
            {
                if (ReadJournal(_journal) != _change)
                    throw new InvalidDataException("Cuphead display journal ownership changed.");
                Restore(_preferences, _journal, _change, _gameIsAbsent);
                _log("Cuphead display: restored the original fullscreen preference.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or JsonException)
            {
                _log("Cuphead display: recovery record retained. " + error.Message);
            }
        }
    }

    private static void Restore(string preferences, string journal, Change change, Func<bool> gameIsAbsent)
    {
        RequireGameClosed(gameIsAbsent);
        using var source = OpenPreferences(preferences);
        byte[] current = ReadSmall(source, MaxPreferencesBytes);
        DisplayPreference display = Parse(current);
        if (!change.Applied)
        {
            string digest = Digest(current);
            if (digest != change.Before && digest != change.After)
                throw new InvalidDataException("An incomplete Cuphead display change no longer matches the saved preference.");
        }
        if (!display.Fullscreen)
        {
            if (display.Width != change.Width || display.Height != change.Height)
                throw new InvalidDataException("Cuphead's display settings changed during the session.");
            Commit(preferences, current, SetFullscreen(current, display, true), gameIsAbsent);
        }
        File.Delete(journal);
    }

    private static bool OwnerIsGone(Change change)
    {
        try
        {
            using var owner = Process.GetProcessById(change.Pid);
            return owner.HasExited || owner.StartTime.ToFileTimeUtc() != change.Birth;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static void RequireGameClosed(Func<bool> gameIsAbsent)
    {
        if (!gameIsAbsent()) throw new InvalidOperationException("Cuphead must be closed to change its display preference.");
    }

    private static void RequireLocalPath(string root, string path)
    {
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("The local profile is redirected.");
        string current = root;
        foreach (string part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("A Cuphead display path is redirected.");
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
    }

    private static FileStream OpenPreferences(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("The Cuphead preference file is redirected.");
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    }

    private static byte[] ReadSmall(Stream stream, int max)
    {
        if (stream.Length is < 1 || stream.Length > max) throw new InvalidDataException("Cuphead display data has an unsupported length.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static Change? ReadJournal(string path)
    {
        try
        {
            using var journal = File.OpenRead(path);
            return JsonSerializer.Deserialize<Change>(ReadSmall(journal, 2048)) ?? throw new InvalidDataException("The Cuphead display journal is empty.");
        }
        catch (FileNotFoundException) { return null; }
    }

    private static void Validate(Change change)
    {
        if (change.Version != 1 || change.Package != SupportedPackage || change.Pid <= 0 || change.Birth <= 0 ||
            change.Width <= 0 || change.Height <= 0 || !IsDigest(change.Before) || !IsDigest(change.After) || change.Before == change.After)
            throw new InvalidDataException("The Cuphead display journal is invalid.");
    }

    private static bool IsDigest(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void WriteJournal(string path, Change change) =>
        Publish(path, JsonSerializer.SerializeToUtf8Bytes(change), pending => File.Move(pending, path, overwrite: true));

    private static void Commit(string path, byte[] expected, byte[] updated, Func<bool> gameIsAbsent)
    {
        Publish(path, updated, pending =>
        {
            RequireGameClosed(gameIsAbsent);
            using var latest = OpenPreferences(path);
            if (!ReadSmall(latest, MaxPreferencesBytes).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("Cuphead's preferences changed before the update could be committed.");
            File.Replace(pending, path, null);
        });
    }

    private static void Publish(string path, byte[] content, Action<string> publish)
    {
        string pending = Path.Combine(Path.GetDirectoryName(path)!, Path.GetRandomFileName());
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            publish(pending);
        }
        finally { File.Delete(pending); }
    }

    internal static DisplayPreference Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length is < 8 or > MaxPreferencesBytes || BinaryPrimitives.ReadInt32LittleEndian(file[4..]) != file.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(file) != Crc32(file[4..]))
            throw new InvalidDataException("Cuphead's preferences failed the Unity format and checksum check.");

        DisplayPreference? display = null;
        for (int offset = 8; offset < file.Length;)
        {
            byte type = file[offset++];
            if (type is not ((byte)'s' or (byte)'n' or (byte)'f')) throw new InvalidDataException("Unsupported Unity preference type.");
            int keyLength = ReadLength(file, ref offset);
            bool settings = file.Slice(offset, keyLength).SequenceEqual("cuphead_settings_data_v1"u8);
            offset += keyLength;
            int lengthOffset = offset;
            int length = type == 's' ? ReadLength(file, ref offset) : 4;
            if (length > file.Length - offset) throw new InvalidDataException("A Unity preference is incomplete.");
            if (settings)
            {
                if (type != 's' || display is not null) throw new InvalidDataException("Cuphead's settings record is ambiguous.");
                display = ReadDisplay(file.Slice(offset, length), offset, lengthOffset);
            }
            offset += length;
        }
        return display ?? throw new InvalidDataException("Cuphead's display settings are unavailable.");
    }

    private static int ReadLength(ReadOnlySpan<byte> file, ref int offset)
    {
        if (file.Length - offset < 4) throw new InvalidDataException("A Unity preference length is incomplete.");
        int length = BinaryPrimitives.ReadInt32LittleEndian(file[offset..]);
        offset += 4;
        if (length < 0 || length > file.Length - offset) throw new InvalidDataException("A Unity preference length is invalid.");
        return length;
    }

    private static DisplayPreference ReadDisplay(ReadOnlySpan<byte> value, int start, int lengthOffset)
    {
        var reader = new Utf8JsonReader(value);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("Cuphead's settings must be a JSON object.");
        bool? fullscreen = null;
        int width = 0, height = 0, tokenOffset = 0, tokenLength = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool screen = reader.ValueTextEquals("fullScreen"u8);
            bool isWidth = reader.ValueTextEquals("screenWidth"u8);
            bool isHeight = reader.ValueTextEquals("screenHeight"u8);
            if (!reader.Read()) throw new InvalidDataException("A Cuphead setting has no value.");
            if (screen)
            {
                if (fullscreen is not null || reader.TokenType is not (JsonTokenType.True or JsonTokenType.False))
                    throw new InvalidDataException("Cuphead's fullscreen preference is ambiguous.");
                fullscreen = reader.GetBoolean();
                tokenOffset = checked(start + (int)reader.TokenStartIndex);
                tokenLength = checked((int)(reader.BytesConsumed - reader.TokenStartIndex));
            }
            else if (isWidth || isHeight)
            {
                ref int dimension = ref (isWidth ? ref width : ref height);
                if (dimension != 0 || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out dimension) || dimension is < 1 or > 32768)
                    throw new InvalidDataException("Cuphead's display resolution is ambiguous.");
            }
            else reader.Skip();
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || fullscreen is null || width == 0 || height == 0)
            throw new InvalidDataException("Cuphead's display settings are incomplete.");
        return new(fullscreen.Value, width, height, lengthOffset, value.Length, tokenOffset, tokenLength);
    }

    internal static byte[] SetFullscreen(ReadOnlySpan<byte> file, DisplayPreference display, bool fullscreen)
    {
        if (display.Fullscreen == fullscreen) return file.ToArray();
        ReadOnlySpan<byte> token = fullscreen ? "true"u8 : "false"u8;
        int change = token.Length - display.TokenLength;
        if (file.Length + change > MaxPreferencesBytes) throw new InvalidDataException("The updated Cuphead preferences would be too large.");
        var updated = new byte[file.Length + change];
        file[..display.TokenOffset].CopyTo(updated);
        token.CopyTo(updated.AsSpan(display.TokenOffset));
        file[(display.TokenOffset + display.TokenLength)..].CopyTo(updated.AsSpan(display.TokenOffset + token.Length));
        BinaryPrimitives.WriteInt32LittleEndian(updated.AsSpan(display.LengthOffset), display.RecordLength + change);
        BinaryPrimitives.WriteInt32LittleEndian(updated.AsSpan(4), updated.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(updated, Crc32(updated.AsSpan(4)));
        return updated;
    }

    internal static uint Crc32(ReadOnlySpan<byte> input)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in input)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        return ~crc;
    }
}
