using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

internal static class CupheadWindowedPreferenceChecks
{
    private const string Settings = "{\"screenWidth\":3840, \"screenHeight\":2160, \"fullScreen\":true, \"effects\":{\"fullScreen\":true}, \"name\":\"à\"}";
    private const string Family = "StudioMDHR.20872A364DAA1_tm1s6a95559gt";

    internal static void Run(string root, Action<bool, string> check, Action<Action, string> denied)
    {
        string profile = Path.Combine(root, "cuphead-display");
        string preferences = Path.Combine(profile, "Packages", Family, "LocalState", "playerprefs.dat");
        string journal = Path.Combine(profile, "Playhub", "xbox-session", "cuphead-display", "pending.json");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        try
        {
            check(CupheadWindowedPreference.Crc32("123456789"u8) == 0xcbf43926, "Cuphead Unity checksum matches the standard CRC32 vector");
            check(CupheadWindowedPreference.Eligible(Family + "!App", "Cuphead.exe", CupheadWindowedPreference.SupportedPackage)
                && !CupheadWindowedPreference.Eligible(Family + "!App", "Cuphead.exe", CupheadWindowedPreference.SupportedPackage.Replace("1.3.8.2", "1.3.8.3"))
                && !CupheadWindowedPreference.Eligible(Family + "!Other", "Cuphead.exe", CupheadWindowedPreference.SupportedPackage)
                && !CupheadWindowedPreference.Eligible(Family + "!App", "other.exe", CupheadWindowedPreference.SupportedPackage),
                "Only the exact tested Cuphead package, application and executable qualify");
            byte[] original = Prefs(Settings);
            var originalDisplay = CupheadWindowedPreference.Parse(original);
            byte[] expectedWindowed = Prefs(Settings.Replace("\"fullScreen\":true,", "\"fullScreen\":false,"));
            byte[] changed = CupheadWindowedPreference.SetFullscreen(original, originalDisplay, false);
            check(originalDisplay.Fullscreen && originalDisplay.Width == 3840 && originalDisplay.Height == 2160 && changed.SequenceEqual(expectedWindowed)
                && CupheadWindowedPreference.SetFullscreen(changed, CupheadWindowedPreference.Parse(changed), true).SequenceEqual(original),
                "Cuphead fullscreen splices one top-level JSON token and round-trips every other byte");
            denied(() => CupheadWindowedPreference.Parse(Prefs(Settings.Replace("\"effects\"", "\"fullScreen\":true,\"effects\""))),
                "Duplicate fullscreen keys are refused");
            denied(() => CupheadWindowedPreference.Parse(Prefs(Settings.Replace("2160", "0"))), "Unsupported resolution is refused");
            byte[] corrupt = (byte[])original.Clone();
            corrupt[^1] ^= 0x80;
            denied(() => CupheadWindowedPreference.Parse(corrupt), "Invalid preferences checksum is refused");

            void Write(byte[] value) => File.WriteAllBytes(preferences, value);
            bool gameClosed = true;
            CupheadWindowedPreference? Acquire(bool ownerGone = true) =>
                CupheadWindowedPreference.AcquireForProfile(profile, () => gameClosed, _ => ownerGone, _ => { });

            Write(expectedWindowed);
            check(Acquire() is null && !File.Exists(journal) && File.ReadAllBytes(preferences).SequenceEqual(expectedWindowed),
                "A user's existing windowed setting requires no mutation or recovery record");
            Write(original);
            gameClosed = false;
            denied(() => Acquire(), "An active or ambiguous Cuphead process prevents changes");
            check(File.ReadAllBytes(preferences).SequenceEqual(original), "The refusal preserves the preference file");

            gameClosed = true;
            using (var lease = Acquire() ?? throw new Exception("Cuphead fixture lease missing."))
            {
                check(File.Exists(journal) && File.ReadAllBytes(preferences).SequenceEqual(expectedWindowed),
                    "The private lease applies windowed mode with a durable recovery record");
                denied(() => Acquire(), "A concurrent launcher cannot acquire the Cuphead preference");
                Write(Prefs(Settings.Replace("\"fullScreen\":true,", "\"fullScreen\":false,"), "new progress"));
            }
            check(!File.Exists(journal) && File.ReadAllBytes(preferences).SequenceEqual(Prefs(Settings, "new progress")),
                "Normal exit restores fullscreen in the newest save without discarding progress");

            Write(original);
            var interrupted = Acquire() ?? throw new Exception("Cuphead fixture lease missing.");
            gameClosed = false;
            interrupted.Dispose();
            check(File.Exists(journal) && File.ReadAllBytes(preferences).SequenceEqual(expectedWindowed),
                "Closing the sidecar while Cuphead runs leaves a recovery record and does not alter live preferences");
            gameClosed = true;
            denied(() => Acquire(ownerGone: false), "Recovery refuses an earlier sidecar that may still be alive");
            using (Acquire() ?? throw new Exception("Recovered Cuphead fixture lease missing.")) { }
            check(!File.Exists(journal) && File.ReadAllBytes(preferences).SequenceEqual(original),
                "Next verified cold launch recovers the previous session and restores the original preference on exit");

            using (Acquire() ?? throw new Exception("Cuphead fixture lease missing."))
                Write(Prefs(Settings.Replace("3840", "1920").Replace("\"fullScreen\":true,", "\"fullScreen\":false,"), "new progress"));
            byte[] userChange = File.ReadAllBytes(preferences);
            check(File.Exists(journal) && !CupheadWindowedPreference.Parse(userChange).Fullscreen,
                "A different display resolution during play conflicts with automatic restoration");
            denied(() => Acquire(), "Subsequent recovery refuses a conflicting display preference");
            check(File.ReadAllBytes(preferences).SequenceEqual(userChange), "Conflicting settings and progress remain byte-for-byte intact");

            File.Delete(journal);
            Write(original);
            interrupted = Acquire() ?? throw new Exception("Cuphead fixture lease missing.");
            gameClosed = false;
            interrupted.Dispose();
            gameClosed = true;
            var entry = JsonSerializer.Deserialize<CupheadWindowedPreference.Change>(File.ReadAllBytes(journal))!;
            File.WriteAllBytes(journal, JsonSerializer.SerializeToUtf8Bytes(entry with { Applied = false }));
            using (Acquire() ?? throw new Exception("Recovered prepared Cuphead fixture lease missing.")) { }
            check(!File.Exists(journal) && File.ReadAllBytes(preferences).SequenceEqual(original),
                "Recovery handles a crash after preferences were replaced and before the applied journal was published");
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    private static byte[] Prefs(string settings, string progress = "original progress")
    {
        using var file = new MemoryStream();
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0L);
            writer.Write((byte)'s');
            WriteString(writer, "cuphead_progress_data_v1");
            WriteString(writer, progress);
            writer.Write((byte)'s');
            WriteString(writer, "cuphead_settings_data_v1");
            WriteString(writer, settings);
            writer.Write((byte)'n');
            WriteString(writer, "fixture-integer");
            writer.Write(4);
        }
        byte[] bytes = file.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, CupheadWindowedPreference.Crc32(bytes.AsSpan(4)));
        return bytes;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
