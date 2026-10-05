using Playhub.GameSession;
using System.Text.Json;

internal static class GdkProofTests
{
    private const string Aumid = "Fixture.Game_abcd!Game";
    private const string FullName = "Fixture.Game_1.0.0.0_x64__abcd";

    internal static void Run(string fixture)
    {
        var directory = Path.Combine(fixture, "gdk-package");
        Directory.CreateDirectory(directory);
        Write(directory, "Windows.FullTrustApplication", "GameLaunchHelper.exe", "Game.exe");
        var catalog = new Catalog([new RegisteredPackage(FullName, directory)]);
        var plan = GdkLaunchProof.Resolve(Aumid, "Game.exe", catalog);
        Check(plan.Helper == Path.Combine(directory, "GameLaunchHelper.exe") && plan.Game == Path.Combine(directory, "Game.exe") &&
            plan.Directory == directory && catalog.Family == "Fixture.Game_abcd", "GDK launch is resolved from the exact registered user package and application");

        Expect(() => GdkLaunchProof.Resolve(Aumid, "Other.exe", catalog), "Expected game must be declared in the registered game configuration");
        Expect(() => GdkLaunchProof.Resolve(Aumid, "..\\Other.exe", catalog), "Game path cannot escape the package installation");
        Expect(() => GdkLaunchProof.Resolve(Aumid, "C:\\Windows\\notepad.exe", catalog), "Caller cannot provide an absolute executable outside the package");
        Expect(() => GdkLaunchProof.Resolve("Fixture.Game_abcd!Other", "Game.exe", catalog), "Application identity must match the manifest entry");
        Write(directory, "Fixture.UwpApplication", "GameLaunchHelper.exe", "Game.exe");
        Expect(() => GdkLaunchProof.Resolve(Aumid, "Game.exe", catalog), "True UWP applications cannot enter the direct GDK proof");
        Write(directory, "Windows.FullTrustApplication", "OtherHelper.exe", "Game.exe");
        Expect(() => GdkLaunchProof.Resolve(Aumid, "Game.exe", catalog), "Only the manifest declared GameLaunchHelper can bootstrap this proof");
        Write(directory, "Windows.FullTrustApplication", "..\\GameLaunchHelper.exe", "Game.exe");
        Expect(() => GdkLaunchProof.Resolve(Aumid, "Game.exe", catalog), "Manifest helper path must remain inside the registered installation");
        Write(directory, "Windows.FullTrustApplication", "GameLaunchHelper.exe", "Game.exe");
        Expect(() => GdkLaunchProof.Resolve(Aumid, "Game.exe", new Catalog([catalog.Packages[0], catalog.Packages[0]])),
            "Ambiguous registered packages are refused before starting any process");
        File.Delete(Path.Combine(directory, "Game.exe"));
        Expect(() => GdkLaunchProof.Resolve(Aumid, "Game.exe", catalog), "Missing declared game executable is refused before bootstrap");
        Write(directory, "Windows.FullTrustApplication", "GameLaunchHelper.exe", "Game.exe");

        var output = Path.Combine(directory, "raw-arguments.json");
        var values = new[] { "a b", "", "trailing\\", "a\"b" };
        var raw = string.Join(" ", new[] { "child", "10", output }.Concat(values).Select(GameLifetime.Quote));
        var code = GameLifetime.Run(Environment.ProcessPath!, [], directory, raw);
        Check(code == 7 && JsonSerializer.Deserialize<string[]>(File.ReadAllText(output))!.SequenceEqual(values),
            "Direct native bootstrap retains raw Windows game argument interpretation");

        var real = new RegisteredPackageCatalog().ForFamily("BethesdaSoftworks.Doom641997_3275kfvn8vcwc");
        Check(real.Count == 1 && real[0].FullName.StartsWith("BethesdaSoftworks.Doom641997_", StringComparison.Ordinal),
            "Read-only native catalog resolves the real DOOM64 registered package");
        var doom = GdkLaunchProof.Resolve("BethesdaSoftworks.Doom641997_3275kfvn8vcwc!Game", "DOOM64_x64.exe", new RegisteredPackageCatalog());
        Check(File.Exists(doom.Helper) && File.Exists(doom.Game), "Read-only real DOOM64 manifest and game configuration validate without a launch");
        var physical = GdkLaunchProof.Resolve("BethesdaSoftworks.Doom641997_3275kfvn8vcwc!Game", "DOOM64_x64.exe", new RegisteredPackageCatalog(physicalPaths: true));
        Check(physical.Directory == @"C:\XboxGames\DOOM 64\Content" && physical.Game == Path.Combine(physical.Directory, "DOOM64_x64.exe"),
            "Read-only native directory handles resolve the registered junction to its physical DOOM64 Content directory");
        var ownExecutable = Environment.ProcessPath!;
        Expect(() => GdkLaunchProof.RejectRunningGame(new GdkLaunchPlan(FullName, Path.GetDirectoryName(ownExecutable)!, ownExecutable, ownExecutable)),
            "Duplicate guard rejects an exact existing native process without requiring package identity");
    }

    private static void Write(string directory, string entryPoint, string helper, string game)
    {
        File.WriteAllText(Path.Combine(directory, "AppxManifest.xml"),
            "<Package><Identity Name=\"Fixture.Game\"/><Applications><Application Id=\"Game\" Executable=\"" + helper + "\" EntryPoint=\"" + entryPoint + "\"/></Applications></Package>");
        File.WriteAllText(Path.Combine(directory, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Id=\"Game\" Name=\"" + game + "\"/></ExecutableList></Game>");
        File.WriteAllText(Path.Combine(directory, "GameLaunchHelper.exe"), "fixture only");
        File.WriteAllText(Path.Combine(directory, "Game.exe"), "fixture only");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    private static void Expect(Action action, string name)
    {
        try { action(); } catch (InvalidOperationException) { Check(true, name); return; }
        throw new Exception(name);
    }
    private sealed class Catalog(IReadOnlyList<RegisteredPackage> packages) : IRegisteredPackageCatalog
    {
        public IReadOnlyList<RegisteredPackage> Packages => packages;
        public string Family = "";
        public IReadOnlyList<RegisteredPackage> ForFamily(string family) { Family = family; return packages; }
    }
}
