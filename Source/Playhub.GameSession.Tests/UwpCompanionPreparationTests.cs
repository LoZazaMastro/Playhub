using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.GameSession;

internal static class UwpCompanionPreparationTests
{
    internal static void Run()
    {
        foreach (string executable in new[] { "Game.exe", @"Binaries\NativeGame.exe", "Binaries/NativeGame.exe" })
        {
            UwpCompanionPreparation.ValidateApplication("Fixture.Family_abcdef!Game", executable);
            Check(true, "UWP preparation accepts a contained registered executable: " + executable);
        }
        foreach (var input in new[] { ("missing", "Game.exe"), ("Family!", "Game.exe"), ("Family!App!Other", "Game.exe"),
            ("Family!App", @"C:\Game.exe"), ("Family!App", @"..\Game.exe"), ("Family!App", @"Folder\..\Game.exe"),
            ("Family!App", "Game.exe:stream"), ("Family!App", "Game.exe\0"), ("Family!App", "Game.dll") })
            Denied(() => UwpCompanionPreparation.ValidateApplication(input.Item1, input.Item2), "UWP preparation refuses malformed or escaping identity");
        byte[] image = "owned native fixture"u8.ToArray();
        UwpCompanionPreparation.VerifyDigest(new MemoryStream(image), Convert.ToHexString(SHA256.HashData(image)));
        Denied(() => UwpCompanionPreparation.VerifyDigest(new MemoryStream(image), new string('0', 64)), "The native image must match the build before activation");
        Denied(() => UwpCompanionPreparation.VerifyDigest(new MemoryStream(image), ""), "A missing native digest cannot prepare the UWP companion");
        var ready = new UwpCompanionPreparation.Ready("playhub-uwp-companion-ready", "Fixture!App", "Fixture_1.0.0.0_x64__abc", @"C:\Fixture\Game.exe");
        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ready) + "\n"));
        Check(UwpCompanionPreparation.ReadReady(inputStream).GetAwaiter().GetResult() == ready, "Bounded readiness transport preserves the registered identity");
        foreach (byte[] data in new[] { Array.Empty<byte>(), "{}"u8.ToArray(), new byte[65536], "null\n"u8.ToArray(), new byte[] { 0xff, 10 } })
            Denied(() => UwpCompanionPreparation.ReadReady(new MemoryStream(data)).GetAwaiter().GetResult(), "Readiness refuses incomplete, oversized and malformed private messages");
        foreach (byte[] data in new[] { Array.Empty<byte>(), "partial"u8.ToArray(), "null\n"u8.ToArray(), new byte[65536], new byte[] { 0xff, 10 } })
        {
            var messages = new List<string>();
            Check(UwpCompanionPreparation.PrepareOptional(() =>
                UwpCompanionPreparation.ReadReady(new MemoryStream(data)).GetAwaiter().GetResult()!, messages.Add) is null
                && messages.Count == 1, "Optional readiness failure is logged once without escaping into the original launch");
        }
        foreach (Exception error in new Exception[] { new InvalidDataException("Owned empty sidecar"),
            new TimeoutException("Owned preparation timeout"), new JsonException("Owned malformed readiness") })
        {
            var optionalFailure = new PreparationRuntime { PreparationFailure = error };
            Check(UwpLifetime.Run("Fixture!App", "Game.exe", "original args", optionalFailure) == 7
                && optionalFailure.Activations == 1 && optionalFailure.Disposals == 0 && optionalFailure.Messages.Count == 1,
                "Unavailable optional companion preserves exactly one Windows activation and the original arguments: " + error.GetType().Name);
        }
        Check(UwpCompanionPreparation.TryStart("Fixture!App", "Game.exe", _ => throw new Exception("Preview was enabled")) is null,
            "Public builds leave registered UWP preparation disabled");
        var lifetime = new PreparationRuntime();
        Check(UwpLifetime.Run("Fixture!App", "Game.exe", "original args", lifetime) == 7
            && lifetime.Activations == 1 && lifetime.Disposals == 1 && lifetime.Prepared,
            "UWP preparation precedes the one original activation and survives the exact game wait");
        var failed = new PreparationRuntime { FailActivation = true };
        Denied(() => UwpLifetime.Run("Fixture!App", "Game.exe", "original args", failed), "Activation failure is preserved with prepared companion cleanup");
        Check(failed.Activations == 1 && failed.Disposals == 1, "A failed original activation disposes preparation once and never launches another game");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static void Denied(Action action, string name) { try { action(); } catch (Exception) { Check(true, name); return; } throw new Exception(name); }

    private sealed class PreparationRuntime : IUwpRuntime
    {
        public long Now { get; private set; }
        public bool ShellReady => true;
        public bool Prepared, FailActivation;
        public Exception? PreparationFailure;
        public List<string> Messages = [];
        public int Activations, Disposals;
        public void StartShell() => throw new Exception("Unexpected shell startup");
        public void Delay(int milliseconds) => Now += milliseconds;
        public void Log(string text) { }
        public IDisposable PrepareLaunch(string aumid, string executable)
        {
            Check(Activations == 0 && !Prepared, "Native readiness is acquired before Windows activation");
            if (PreparationFailure is { } error)
                return UwpCompanionPreparation.PrepareOptional<Release>(() => throw error, Messages.Add)!;
            Prepared = true; return new Release(() => Disposals++);
        }
        public IUwpProcess Activate(string aumid, string arguments)
        {
            Check((Prepared || PreparationFailure is not null) && Disposals == 0 && arguments == "original args", "The original UWP activation retains readiness and user arguments");
            Activations++;
            if (FailActivation) throw new InvalidOperationException("Owned activation failure");
            return new FixtureGame(this);
        }
        public IUwpProcess? FindGame(string family, string executable) => throw new Exception("No guessed game process");
        private sealed class FixtureGame(PreparationRuntime owner) : IUwpProcess
        {
            public uint Id => 123;
            public string PackageFamily => "Fixture";
            public string Executable => "Game.exe";
            public int WaitForExit() { Check((owner.Prepared || owner.PreparationFailure is not null) && owner.Disposals == 0, "Companion preparation remains owned until exact game exit"); return 7; }
            public void Dispose() { }
        }
    }
    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
}
