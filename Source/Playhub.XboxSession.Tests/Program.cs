using System.Diagnostics;
using System.Text.Json;
using Playhub.GameSession;
using WSGM.PackagedLaunch;

internal static class XboxChecks
{
    private static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "fixture-child") { File.WriteAllText(args[1], JsonSerializer.Serialize(args.Skip(2))); Thread.Sleep(200); return 17; }
        var root = Path.Combine(Path.GetTempPath(), "Playhub-XboxChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        void Denied(Action operation, string name) { try { operation(); } catch (Exception) { Check(true, name); return; } throw new Exception(name); }
        const string aumid = "Fixture.Game_abcd!Game";
        const string raw = "--locale it --path \"a b\" --token=abc";
        var command = XboxCommand.Parse(["--uwp", aumid, "Game.exe", raw]);
        Check(command.Arguments == raw && command.Aumid == aumid, "Generic command retains raw user arguments without DOOM identity");
        Check(XboxCommand.Parse(["--uwp", aumid, "Game.exe", raw, "extra value"]).Arguments == raw + " \"extra value\"", "Extra user argument tokens retain Windows quoting");
        Denied(() => XboxCommand.Parse(["--pid", "123", "Game.exe"]), "Caller cannot select a process target");
        Denied(() => XboxCommand.Parse(["--uwp", "invalid", "Game.exe"]), "Malformed application identity rejected");
        Check(XboxCommand.IsSteamShortcutSession(["SteamGameId=12123629805379780608"]), "Valid attended shortcut session accepted");
        Check(XboxCommand.IsSteamShortcutSession(["SteamGameId=" + (((ulong)0xf0000000 << 32) | 0x02000000)]), "Another valid shortcut identity is accepted generically");
        Check(!XboxCommand.IsSteamShortcutSession(["SteamGameId=480"]) && !XboxCommand.IsSteamShortcutSession(["SteamAppId=0"]), "Native app or missing shortcut session rejected");
        Check(!XboxCommand.IsSteamShortcutSession(["SteamGameId=12123629805379780608", "SteamGameId=12123629805379780608"]), "Ambiguous session identity rejected");
        using var own = Process.GetCurrentProcess();
        var guard = new TargetHandleGuard(own.Id, own.Handle);
        Check(guard.Allows(own.Id, own.Handle), "Retained native process handle has exact current creation identity");
        Check(!guard.Allows(own.Id + 1, own.Handle) && !guard.Allows(own.Id, 0), "Different process or missing handle denied");
        Check(!new TargetHandleGuard(own.Id, own.Handle, creationOverride: 1).Allows(own.Id, own.Handle), "Recycled creation identity denied");
        int guards = 0;
        var injector = new GameInjector(new PrivilegeJournal(), (_, _) => { guards++; return false; });
        Check(!injector.SetEnvironment(own.Id, ["SteamFixture=unused"]) && !injector.Load(own.Id, SteamInstallation.OverlayRenderer), "Guard denial blocks actual upstream environment and DLL operations before any remote write");
        Check(guards == 2 && !GameInjector.HasModule(own.Id, "GameOverlayRenderer64.dll"), "Fixture process has no injected renderer");
        using (var trusted = TrustedSteamFiles.Open()) Check(trusted.UnchangedRoot(), "Actual same-session Steam and four signed Valve x64 components validate read-only");
        Check(!TrustedSteamFiles.ValveSignature(Environment.ProcessPath!), "Unsigned fixture cannot impersonate a trusted Valve component");
        Denied(() => TrustedSteamFiles.VerifyX64(new MemoryStream(new byte[64])), "Invalid PE component rejected");
        var output = Path.Combine(root, "arguments.json");
        var values = new[] { "", "a b", "a\"b", "trailing\\" };
        var elapsed = Stopwatch.StartNew();
        int exit = XboxSessionClient.Run(Environment.ProcessPath!, new[] { "fixture-child", output }.Concat(values));
        Check(exit == 17 && elapsed.ElapsedMilliseconds >= 150 && JsonSerializer.Deserialize<string[]>(File.ReadAllText(output))!.SequenceEqual(values), "MIT CLI delegation waits exact child and preserves every argument and exit code");
        var plan = new GdkLaunchPlan("Fixture.Game_1.0_x64__abcd", root, Path.Combine(root, "GameLaunchHelper.exe"), Path.Combine(root, "Game.exe"));
        var fixture = new Runtime(own.Id, plan.Game);
        int originalSelections = 0, experimentalSelections = 0;
        IUwpRuntime Original() { originalSelections++; return fixture; }
        IUwpRuntime ForbiddenExperiment(XboxCommand _) { experimentalSelections++; throw new Exception("Automatic experimental route selected"); }
        Check(XboxSession.Run(command, Original, ForbiddenExperiment) == 19 && originalSelections == 1 && experimentalSelections == 0
            && fixture.Activations == 1 && fixture.GameWaits == 1 && fixture.Arguments == raw,
            "Normal shortcut runs one original activation and exact lifetime without entering experimental setup");
        fixture = new Runtime(own.Id, plan.Game);
        var userSwitch = XboxCommand.Parse(["--uwp", aumid, "Game.exe", "--gdk-experimental"]);
        Check(XboxSession.Run(userSwitch, Original, ForbiddenExperiment) == 19 && experimentalSelections == 0
            && fixture.Arguments == "--gdk-experimental", "User game arguments cannot opt a normal shortcut into experimental setup");
        fixture = new Runtime(own.Id, plan.Game) { MissingGame = true };
        Denied(() => XboxSession.Run(command, Original, ForbiddenExperiment), "Normal activation timeout does not retry through experimental setup");
        Check(fixture.Activations == 1 && fixture.Now < 16000 && experimentalSelections == 0, "Normal missing-game startup stays bounded with no injection fallback");
        fixture = new Runtime(own.Id, plan.Game);
        var experimental = XboxCommand.Parse(["--gdk-experimental", aumid, "Game.exe", raw]);
        Check(XboxSession.Run(experimental, () => throw new Exception("Wrong route"), _ => { experimentalSelections++; return fixture; }) == 19
            && experimentalSelections == 1 && fixture.Activations == 1 && fixture.Arguments == raw,
            "Explicit experimental command selects its isolated runtime once and retains user arguments");
        fixture = new Runtime(own.Id, plan.Game);
        var handoff = new HandoffRuntime(plan, aumid, fixture, () => throw new InvalidOperationException("fixture trust failure"));
        Check(UwpLifetime.Run(aumid, "Game.exe", raw, handoff) == 19 && fixture.Activations == 1 && fixture.GameWaits == 1 && fixture.Arguments == raw,
            "Preactivation Steam failure falls back to one original activation and exact game supervision");
        fixture = new Runtime(own.Id, plan.Game);
        handoff = new HandoffRuntime(plan, aumid, fixture);
        Check(UwpLifetime.Run(aumid, "Game.exe", raw, handoff) == 19 && fixture.Activations == 1 && fixture.GameWaits == 1 && !GameInjector.HasModule(own.Id, "GameOverlayRenderer64.dll"),
            "Returned helper identity refusal performs no write and never reactivates the game");
        fixture = new Runtime(own.Id, plan.Game) { MissingGame = true };
        handoff = new HandoffRuntime(plan, aumid, fixture, () => throw new InvalidOperationException("fixture trust failure"));
        Denied(() => UwpLifetime.Run(aumid, "Game.exe", "", handoff), "Silent launch-helper death with no game ends at the startup deadline");
        Check(fixture.Now < 16000 && fixture.Activations == 1 && fixture.GameWaits == 0, "Missing handoff has bounded searches and no duplicate activation or indefinite polling");
        Console.WriteLine($"PASS {count} checks; fixtures only, no game activation or remote process writes.");
        return 0;
    }

    private sealed class Runtime(int ownPid, string gamePath) : IUwpRuntime
    {
        public long Now { get; private set; }
        public bool ShellReady => true;
        public int Activations, GameWaits;
        public string Arguments = "";
        public bool MissingGame;
        public void StartShell() => throw new Exception("Already ready");
        public void Delay(int milliseconds) => Now += milliseconds;
        public void Log(string message) { }
        public IUwpProcess Activate(string aumid, string arguments) { Activations++; Arguments = arguments; return new Fixture((uint)ownPid, Environment.ProcessPath!, () => 0); }
        public IUwpProcess? FindGame(string family, string executable) => MissingGame ? null : new Fixture((uint)ownPid, gamePath, () => { GameWaits++; return 19; });
        private sealed class Fixture(uint id, string executable, Func<int> wait) : IUwpProcess
        {
            public uint Id => id;
            public string Executable => Path.GetFileName(executable).Equals("Game.exe") ? executable : "GameLaunchHelper.exe";
            public string PackageFamily => "Fixture.Game_abcd";
            public int WaitForExit() => wait();
            public void Dispose() { }
        }
    }
}
