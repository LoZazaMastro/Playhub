using System.Text;
using System.Text.Json;
using Playhub.GameSession;

internal static class UnrealDisplayTests
{
    private const string Text = "; original game settings 日本語\r\n[/Script/Fixture.CustomGameUserSettings]\r\n" +
        "bUseVSync=False\r\nFullscreenMode=0\r\nLastConfirmedFullscreenMode=0\r\nPreferredFullscreenMode=0\r\n" +
        "ResolutionSizeX=3840\r\nResolutionSizeY=2160\r\nFrameRateLimit=120.000000\r\n\r\n[/Script/Engine.GameUserSettings]\r\nbUseDesiredScreenHeight=False\r\n";

    internal static void Run(string root)
    {
        Check(!UnrealDisplayLease.AllowsBuild([])
            && !UnrealDisplayLease.AllowsBuild([new("PlayhubWin32SteamCompanion", "true")])
            && !UnrealDisplayLease.AllowsBuild([new("PlayhubWin32BorderlessSession", "true")])
            && !UnrealDisplayLease.AllowsBuild([new("PlayhubWin32SteamCompanion", "true"), new("PlayhubWin32BorderlessSession", "false")])
            && UnrealDisplayLease.AllowsBuild([new("PlayhubWin32SteamCompanion", "true"), new("PlayhubWin32BorderlessSession", "true")]),
            "Only an explicit borderless-session trial build can prepare Unreal display settings");
        var logs = new List<string>();
        var fixture = Create(root, "display-roundtrip", new UTF8Encoding(false));
        byte[] original = File.ReadAllBytes(fixture.Settings);
        using (var lease = Acquire(fixture, () => true, logs.Add))
        {
            Check(lease is not null, "Eligible Unreal layout gets a bounded display lease");
            byte[] updated = File.ReadAllBytes(fixture.Settings);
            Check(UnrealDisplayLease.Parse(updated).Values.All(value => value == 1)
                && original.Zip(updated).Count(pair => pair.First != pair.Second) == 3,
                "Only the three exclusive fullscreen digits change to borderless");
            Expect<IOException>(() => Acquire(fixture, () => true, logs.Add),
                "A second wrapper cannot acquire the same settings lease");
        }
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(original) && !Journals(fixture).Any(),
            "Normal disposal restores exact original bytes and removes its journal");

        foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
        {
            fixture = Create(root, "display-encoding-" + encoding.CodePage + "-" + encoding.Preamble.Length, encoding);
            original = File.ReadAllBytes(fixture.Settings);
            using (Acquire(fixture, () => true, logs.Add))
            {
                Check(UnrealDisplayLease.Parse(File.ReadAllBytes(fixture.Settings)).Values.All(value => value == 1),
                    "Native settings encoding supports exact mode replacement: " + encoding.WebName);
            }
            Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(original),
                "BOM, Unicode text and newlines survive round trip: " + encoding.WebName);
        }

        fixture = Create(root, "display-game-progress", new UTF8Encoding(false));
        byte[] expected;
        using (Acquire(fixture, () => true, logs.Add))
        {
            string current = File.ReadAllText(fixture.Settings).Replace("FrameRateLimit=120.000000", "FrameRateLimit=90.000000", StringComparison.Ordinal);
            current += "\r\n[UserProgress]\r\nCompletedChapter=8\r\n";
            File.WriteAllText(fixture.Settings, current, new UTF8Encoding(false));
            expected = UnrealDisplayLease.SetModes(File.ReadAllBytes(fixture.Settings), UnrealDisplayLease.Parse(File.ReadAllBytes(fixture.Settings)), 0);
        }
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(expected),
            "Rollback preserves all new progress, graphics options and other game-written bytes");

        foreach (int mode in new[] { 1, 2 })
        {
            fixture = Create(root, "display-original-mode-" + mode, new UTF8Encoding(false));
            byte[] bytes = File.ReadAllBytes(fixture.Settings);
            File.WriteAllBytes(fixture.Settings, mode == 1 ? UnrealDisplayLease.SetModes(bytes, UnrealDisplayLease.Parse(bytes), 1)
                : Encoding.UTF8.GetBytes(Text.Replace("Mode=0", "Mode=2", StringComparison.Ordinal)));
            original = File.ReadAllBytes(fixture.Settings);
            Check(Acquire(fixture, () => true, logs.Add) is null && File.ReadAllBytes(fixture.Settings).SequenceEqual(original),
                "Existing borderless/windowed presentation is respected: " + mode);
        }

        fixture = Create(root, "display-live-refusal", new UTF8Encoding(false));
        original = File.ReadAllBytes(fixture.Settings);
        Expect<InvalidOperationException>(() => Acquire(fixture, () => false, logs.Add), "A live exact game blocks preparation");
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(original), "Live game refusal leaves settings untouched");
        bool absent = true;
        var liveLease = Acquire(fixture, () => absent, logs.Add)!;
        absent = false;
        liveLease.Dispose();
        Check(UnrealDisplayLease.Parse(File.ReadAllBytes(fixture.Settings)).Values.All(value => value == 1) && Journals(fixture).Count() == 1,
            "Disposal while exact game remains alive retains recovery and defers writes");
        absent = true;
        using (Acquire(fixture, () => absent, logs.Add)) { }
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(original) && !Journals(fixture).Any(),
            "A later absent-game acquisition recovers the journal and restores normally");

        fixture = Create(root, "display-owner-alive", new UTF8Encoding(false));
        var ownerLease = Acquire(fixture, () => true, logs.Add)!;
        string journal = Journals(fixture).Single();
        ownerLease.Dispose();
        // Dispose above restored; publish a valid previous record using its actual
        // schema to verify the independent owner gate before any write.
        byte[] before = File.ReadAllBytes(fixture.Settings);
        byte[] after = UnrealDisplayLease.SetModes(before, UnrealDisplayLease.Parse(before), 1);
        var change = new UnrealDisplayLease.Change(1, Environment.ProcessId, DateTime.UtcNow.ToFileTimeUtc(), fixture.Executable,
            fixture.Settings, UnrealDisplayLease.Parse(before).Section, Hash(before), Hash(after), true);
        File.WriteAllBytes(fixture.Settings, after);
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllBytes(journal, JsonSerializer.SerializeToUtf8Bytes(change));
        Expect<InvalidOperationException>(() => UnrealDisplayLease.AcquireForProfile(fixture.Executable, fixture.Profile,
            () => true, _ => false, logs.Add), "An unverified/live recovery owner blocks new display writes");
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(after), "Unverified owner refusal preserves the previous session");
        using (Acquire(fixture, () => true, logs.Add)) { }

        fixture = Create(root, "display-user-conflict", new UTF8Encoding(false));
        using (Acquire(fixture, () => true, logs.Add))
            File.WriteAllText(fixture.Settings, File.ReadAllText(fixture.Settings).Replace("FullscreenMode=1", "FullscreenMode=2", StringComparison.Ordinal), new UTF8Encoding(false));
        original = File.ReadAllBytes(fixture.Settings);
        Check(Journals(fixture).Count() == 1 && UnrealDisplayLease.Parse(original).Values[0] == 2,
            "A user's explicit mode change is preserved with a retained conflict record");
        Expect<InvalidDataException>(() => Acquire(fixture, () => true, logs.Add), "A mode conflict cannot be silently reapplied");
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(original), "Conflicting mode fields remain byte-for-byte unchanged");

        fixture = Create(root, "display-prepared-recovery", new UTF8Encoding(false));
        before = File.ReadAllBytes(fixture.Settings);
        after = UnrealDisplayLease.SetModes(before, UnrealDisplayLease.Parse(before), 1);
        using (var pending = Acquire(fixture, () => true, logs.Add))
        {
            journal = Journals(fixture).Single();
            change = JsonSerializer.Deserialize<UnrealDisplayLease.Change>(File.ReadAllBytes(journal))!;
            File.WriteAllBytes(journal, JsonSerializer.SerializeToUtf8Bytes(change with { Applied = false }));
        }
        using (Acquire(fixture, () => true, logs.Add)) { }
        Check(File.ReadAllBytes(fixture.Settings).SequenceEqual(before) && !Journals(fixture).Any(),
            "Crash between settings commit and applied-journal commit recovers only matching data");

        foreach (var bad in new[] {
            Text + "\r\n[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\n",
            Text.Replace("PreferredFullscreenMode=0\r\n", "", StringComparison.Ordinal),
            Text.Replace("LastConfirmedFullscreenMode=0", "LastConfirmedFullscreenMode=7", StringComparison.Ordinal),
            Text.Replace("/Script/Fixture.CustomGameUserSettings", "Unrelated", StringComparison.Ordinal),
            Text.Replace("PreferredFullscreenMode=0", "[/Script/Other.GameUserSettings]\r\nPreferredFullscreenMode=0", StringComparison.Ordinal) })
            Expect<InvalidDataException>(() => UnrealDisplayLease.Parse(Encoding.UTF8.GetBytes(bad)),
                "Incomplete, duplicate, unsupported or split display fields are rejected");

        fixture = Create(root, "display-bad-container", new UTF8Encoding(false));
        File.WriteAllText(fixture.Container, "not an Unreal container");
        Check(UnrealDisplayLease.Resolve(fixture.Executable, fixture.Profile) is null, "A Paks directory alone does not establish Unreal capability");
        fixture = Create(root, "display-ambiguous-config", new UTF8Encoding(false));
        string second = Path.Combine(fixture.Profile, "FixtureGame", "Saved", "Config", "WindowsNoEditor", "GameUserSettings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.Copy(fixture.Settings, second);
        Check(UnrealDisplayLease.Resolve(fixture.Executable, fixture.Profile) is null, "Ambiguous Windows/WindowsNoEditor profiles are refused");

        fixture = Create(root, "display-readonly", new UTF8Encoding(false));
        File.SetAttributes(fixture.Settings, File.GetAttributes(fixture.Settings) | FileAttributes.ReadOnly);
        try { Expect<InvalidDataException>(() => Acquire(fixture, () => true, logs.Add), "Read-only settings are never replaced"); }
        finally { File.SetAttributes(fixture.Settings, FileAttributes.Normal); }

        fixture = Create(root, "display-redirected", new UTF8Encoding(false));
        string real = fixture.Settings + ".original";
        File.Move(fixture.Settings, real);
        File.CreateSymbolicLink(fixture.Settings, real);
        try { Expect<InvalidDataException>(() => Acquire(fixture, () => true, logs.Add), "Reparse-point settings are refused before opening"); }
        finally { File.Delete(fixture.Settings); File.Move(real, fixture.Settings); }
        Check(logs.Any(line => line.Contains("recovered the earlier session", StringComparison.Ordinal)), "Recovery is observable in the session log");
    }

    private sealed record Fixture(string Executable, string Profile, string Settings, string Container);
    private static Fixture Create(string root, string name, Encoding encoding)
    {
        string folder = Path.Combine(root, name), project = Path.Combine(folder, "games", "FixtureGame");
        string executable = Path.Combine(project, "Binaries", "Win64", "FixtureGame-Win64-Shipping.exe");
        string container = Path.Combine(project, "Content", "Paks", "FixtureGame-Windows.utoc");
        string profile = Path.Combine(folder, "profile");
        string settings = Path.Combine(profile, "FixtureGame", "Saved", "Config", "Windows", "GameUserSettings.ini");
        foreach (string path in new[] { executable, container, settings }) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(executable, "MZfixture"u8.ToArray());
        File.WriteAllBytes(container, "-==--==--==--==-"u8.ToArray());
        File.WriteAllBytes(settings, encoding.GetPreamble().Concat(encoding.GetBytes(Text)).ToArray());
        return new(executable, profile, settings, container);
    }
    private static UnrealDisplayLease? Acquire(Fixture fixture, Func<bool> absent, Action<string> log) =>
        UnrealDisplayLease.AcquireForProfile(fixture.Executable, fixture.Profile, absent, _ => true, log);
    private static IEnumerable<string> Journals(Fixture fixture)
    {
        string folder = Path.Combine(fixture.Profile, "Playhub", "game-session", "unreal-display");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
    }
    private static string Hash(byte[] value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value));
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { Check(true, message); return; }
        throw new Exception(message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
