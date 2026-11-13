using System.Diagnostics;
using System.Text.Json;
using Playhub.GameSession;
using WSGM.PackagedLaunch;

internal static class XboxChecks
{
    private static int Main(string[] args)
    {
        using var logs = SessionFixtureLogs.Start();
        if(args.SequenceEqual(new[]{"--input-fence-release-checks"}))
        {
            int checks=0;
            InputFenceReleaseChecks.Run((ok,name)=>{if(!ok)throw new Exception(name);checks++;});
            Console.WriteLine($"PASS {checks} actual release producer checks; fake private bridge/sink, no mapping/window/input calls.");
            return 0;
        }
        if(args.SequenceEqual(new[]{"--input-fence-prototype-checks"}))
        {
            int checks=0;
            InputFencePrototypeChecks.Run((ok,name)=>{if(!ok)throw new Exception(name);checks++;});
            Console.WriteLine($"PASS {checks} config/PE input-fence checks; no mapping/injection/input.");
            return 0;
        }
        if(args.SequenceEqual(new[]{"--ordinary-mirror-checks"}))
        {
            int checks=0;
            SteamOrdinaryMirrorMenuChecks.Run((ok,name)=>{if(!ok)throw new Exception(name);checks++;});
            Console.WriteLine($"PASS {checks} ordinary Mirror checks; no controller, layout process, window or native call.");
            return 0;
        }
        if (args.Length == 1 && args[0] == "--fixture-win32-child") { Thread.Sleep(2500); return 0; }
        if (args.Length > 1 && args[0] == "fixture-child") { File.WriteAllText(args[1], JsonSerializer.Serialize(args.Skip(2))); Thread.Sleep(200); return 17; }
        if (args.Length == 2 && args[0] == "fixture-bridge") return AppContainerBrokerChecks.InitializeNativeFixture(args[1]);
        if (args.Length == 3 && args[0] == "--appcontainer-child") return AppContainerChildProof.Run(args[1], args[2]);
        if (args.Length == 2 && args[0] == "--companion") return SteamCompanionChecks.CheckArtifact(args[1]);
        if (args.Length == 3 && args[0] == "--registered-uwp-readonly")
        {
            var readonlyCommand = new XboxCommand(args[1], args[2], "");
            var readonlyPlan = AppContainerProof.Resolve(readonlyCommand, new RegisteredPackageCatalog(physicalPaths: true), verifyImage: false);
            using var manifest = RegisteredUwpManifestProof.Verify(readonlyCommand, readonlyPlan);
            Console.WriteLine($"PASS read-only registered UWP manifest, complete native package identity and physical executable path: {readonlyPlan.PackageFullName}");
            return 0;
        }
        var root = Path.Combine(Path.GetTempPath(), "Playhub-XboxChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        void Denied(Action operation, string name) { try { operation(); } catch (Exception) { Check(true, name); return; } throw new Exception(name); }
        const string aumid = "Fixture.Game_abcd!Game";
        const string raw = "--locale it --path \"a b\" --token=abc";
        var command = XboxCommand.Parse(["--uwp", aumid, "Game.exe", raw]);
        CheckAppContainerPreflight(root, Check, Denied);
        CheckAppContainerTrial(Check, Denied);
        PackageLifetimeChecks.Run(root, Check, Denied);
        CupheadWindowedPreferenceChecks.Run(root, Check, Denied);
        AppContainerBrokerChecks.Run(Check);
        SteamControllerWin32RelayChecks.Run(Check);
        SteamInputRelayChecks.Run(Check);
        SteamInventoryLifetimeChecks.Run(Check);
        SteamTrackedWrapperChainChecks.Run(Check);
        SteamMirrorOpeningChecks.Run(Check);
        SteamReaderNeutralBootstrapChecks.Run(Check);
        SteamMirrorOwnedForegroundChecks.Run(Check);
        SteamOrdinaryMirrorMenuChecks.Run(Check);
        SteamNativeOverlayInputChecks.Run(Check);
        Win32CompanionChecks.Run(Check, Denied);
        UwpCompanionChecks.Run(root, Check, Denied);
        RegisteredDesktopChecks.Run(root, Check, Denied);
        if (args.Length == 2 && args[0] == "--bridge")
        {
            var native = File.ReadAllBytes(args[1]);
            AppContainerProof.VerifyBridge(new MemoryStream(native));
            var sid = TrialBridgeFile.PackageSid("StudioMDHR.20872A364DAA1_tm1s6a95559gt");
            Denied(() => TrialBridgeFile.Open(sid, args[1], new string('0', 64)), "AppContainer trial refuses a bridge that differs from its build digest");
            Check(true, "Native x64 build exposes each expected code export without loading the DLL");
            Check(XboxSessionClient.Run(Environment.ProcessPath!, ["fixture-bridge", args[1]]) == 0,
                "The real native bridge initializes and consumes the transport handles in an isolated fixture process");
            var export = "InitializeInputBridge\0"u8;
            int offset = native.AsSpan().IndexOf(export);
            Check(offset >= 0, "Bridge fixture contains the expected export name");
            native[offset] = (byte)'X';
            Denied(() => AppContainerProof.VerifyBridge(new MemoryStream(native)), "Bridge missing an exact export is refused");
        }
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
        Check(!injector.Load(own.Id, SteamInstallation.OverlayRenderer), "A denied native load cannot become a success on a repeated request");
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

    private static void CheckAppContainerTrial(Action<bool, string> check, Action<Action, string> denied)
    {
        const ulong gameId = 10674787038353424384;
        const string wrapper = @"C:\Program Files\Playhub\Playhub.GameSession.exe";
        const int pid = 30108;
        var birth = new DateTime(2026, 11, 10, 17, 2, 41, DateTimeKind.Local);
        string row = $"[2026-11-10 17:02:41] AppID {gameId} adding PID {pid} as a tracked process \"\"{wrapper}\" --uwp \"Fixture!App\" \"Game.exe\"";
        check(TrialSteamTracking.MatchesLog(row, pid, wrapper, gameId, birth, birth.AddSeconds(2)),
            "Trial accepts the exact Steam-tracked wrapper, GameID and timestamp");
        check(!TrialSteamTracking.MatchesLog(row, pid + 1, wrapper, gameId, birth, birth.AddSeconds(2))
            && !TrialSteamTracking.MatchesLog(row, pid, wrapper + ".old", gameId, birth, birth.AddSeconds(2))
            && !TrialSteamTracking.MatchesLog(row, pid, wrapper, gameId + 1, birth, birth.AddSeconds(2))
            && !TrialSteamTracking.MatchesLog(row, pid, wrapper, gameId, birth.AddSeconds(4), birth.AddSeconds(4)),
            "Trial refuses foreign PID, wrapper path, GameID and stale Steam log evidence");
        long creation = birth.ToFileTimeUtc();
        var handoff = new TrialHandoff(pid, creation, creation - 1);
        check(handoff.IsFresh(creation, creation + 1) && !handoff.IsFresh(creation - 1, creation + 1)
            && !(handoff with { Activation = creation + 1 }).IsFresh(creation, creation + 2)
            && !handoff.IsFresh(creation, creation + TimeSpan.FromSeconds(16).Ticks),
            "Trial freshness requires the exact AAM PID birth after activation within the bounded window");
        using var packet = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            $"{{\"pid\":{pid},\"creationFileTime\":{creation},\"activationFileTime\":{creation - 1}}}\r\n"));
        check(AppContainerTrial.ReadHandoff(packet).GetAwaiter().GetResult() == handoff
            && AppContainerTrial.ReadHandoff(Stream.Null).GetAwaiter().GetResult() is null,
            "Private trial handoff parses the MIT JSON format and treats early EOF as a no-op");
        denied(() => AppContainerTrial.ReadHandoff(new MemoryStream(new byte[256])).GetAwaiter().GetResult(),
            "Oversized or unterminated trial handoff is refused");
        denied(() => AppContainerTrial.ReadSteamGameId("480"), "The native Steam app ID cannot opt into the Cuphead trial");
        check(AppContainerTrial.ReadSteamGameId(gameId.ToString()) == gameId, "Trial parses an exact non-Steam shortcut GameID");

        string[] components = ["tier0_s64.dll", "vstdlib_s64.dll", "steamclient64.dll", "GameOverlayRenderer64.dll"];
        List<string> sequence = [];
        bool Publish(IReadOnlyList<string> variables) { sequence.AddRange(variables); return true; }
        bool Load(string component) { sequence.Add(component); return true; }
        uint? Input() { sequence.Add("input"); return 0; }
        check(AppContainerTrial.PrepareRenderer(["session"], components, Publish, Load, Input) == (true, 0u)
            && sequence.SequenceEqual(["session", .. components, "input"]),
            "Trial publishes Steam identity, loads three dependencies then renderer, and initializes input last");
        sequence.Clear();
        check(AppContainerTrial.PrepareRenderer(["session"], components, Publish,
                component => Load(component) && component != "vstdlib_s64.dll", Input) == (false, null)
            && sequence.SequenceEqual(["session", "tier0_s64.dll", "vstdlib_s64.dll"]),
            "Trial never retries or loads renderer/input after a failed dependency");
    }

    private static void CheckAppContainerPreflight(string root, Action<bool, string> check, Action<Action, string> denied)
    {
        const string family = "Fixture.Native_abcd";
        const string fullName = "Fixture.Native_1.0.0.0_x64__abcd";
        var command = XboxCommand.Parse(["--uwp", family + "!App", "Game.exe"]);
        var directory = Directory.CreateDirectory(Path.Combine(root, "appcontainer")).FullName;
        var manifest = Path.Combine(directory, "AppxManifest.xml");
        var executable = Path.Combine(directory, "Game.exe");
        string Package(string application, string architecture = "x64") =>
            $"<Package xmlns:uap10='http://schemas.microsoft.com/appx/manifest/uap/windows10/10'><Identity Name='Fixture.Native' ProcessorArchitecture='{architecture}'/><Applications>{application}</Applications></Package>";

        try
        {
            File.Copy(Environment.ProcessPath!, executable);
            var registered = new RegisteredPackage(fullName, directory);
            var catalog = new AppContainerCatalog([registered]);
            File.WriteAllText(manifest, Package("<Application Id='App' Executable='Game.exe' EntryPoint='fixture.App'/>"));
            var plan = AppContainerProof.Resolve(command, catalog);
            check(plan == new AppContainerLaunchPlan(fullName, executable),
                "Exact installed identity and legacy UWP manifest resolve without activation");
            check(AppContainerProof.Resolve(command, catalog, _ => throw new UnauthorizedAccessException()) == plan with { ExecutableVerified = false },
                "Protected executable retains registered manifest evidence but cannot pass static image verification");
            check(AppContainerProof.Resolve(command, catalog, _ => throw new Exception("Game image opened"), verifyImage: false)
                == plan with { ExecutableVerified = false }, "Running target preflight does not open the protected image");
            denied(() => AppContainerProof.Resolve(command with { Executable = "Other.exe" }, catalog), "Different requested executable refused");
            denied(() => AppContainerProof.Resolve(command, new AppContainerCatalog([registered, registered])), "Ambiguous registered package refused");
            denied(() => AppContainerProof.Resolve(command, new AppContainerCatalog([registered with { FullName = "Fixture.Native_1.0.0.0_x64__other" }])), "Mismatched registered family refused");

            File.WriteAllText(manifest, Package("<Application Id='App' Executable='Game.exe' EntryPoint='fixture.App' uap10:RuntimeBehavior='windowsApp' uap10:TrustLevel='appContainer'/>"));
            check(AppContainerProof.Resolve(command, catalog).Executable == executable, "Explicit x64 AppContainer manifest resolves");
            File.WriteAllText(manifest, Package("<Application Id='App' Executable='Game.exe' EntryPoint='Windows.FullTrustApplication'/>"));
            denied(() => AppContainerProof.Resolve(command, catalog), "GDK full-trust helper refused by AppContainer preflight");
            File.WriteAllText(manifest, Package("<Application Id='App' Executable='Game.exe' EntryPoint='fixture.App' uap10:TrustLevel='mediumIL'/>"));
            denied(() => AppContainerProof.Resolve(command, catalog), "Medium-integrity package refused even with a custom entry point");
            File.WriteAllText(manifest, Package("<Application Id='App' Executable='../Game.exe' EntryPoint='fixture.App'/>"));
            denied(() => AppContainerProof.Resolve(command, catalog), "Manifest executable outside the package refused");
            File.WriteAllText(manifest, Package("<Application Id='App' Executable='Game.exe' EntryPoint='fixture.App'/>", "arm64"));
            denied(() => AppContainerProof.Resolve(command, catalog), "ARM64 package refused before bridge setup");
            File.WriteAllText(manifest, "<!DOCTYPE Package [<!ENTITY embedded 'test'>]>" +
                Package("<Application Id='App' Executable='Game.exe' EntryPoint='&embedded;'/>") );
            denied(() => AppContainerProof.Resolve(command, catalog), "Manifest entity expansion refused");
            using var stream = File.OpenRead(executable);
            denied(() => AppContainerProof.VerifyBridge(stream), "Ordinary x64 executable rejected as a bridge");

            using var current = Process.GetCurrentProcess();
            var session = (uint)current.SessionId;
            var currentImage = plan with { Executable = Environment.ProcessPath! };
            using var actual = RetainedAppContainerTarget.Inspect((uint)current.Id, command, currentImage, session);
            check(actual is null, "The real x64 fixture process is rejected by the AppContainer token gate");
            var identity = new AppContainerProcessIdentity(1234, 42, session, executable, fullName, family, command.Aumid, true, true);
            check(identity.Matches(command, plan, session) && identity.Matches(command, plan with { ExecutableVerified = false }, session),
                "Exact live package identity can establish readiness independently of protected executable bytes");
            AppContainerProcessIdentity[] wrongTargets =
            [
                identity with { Image = Environment.ProcessPath! }, identity with { Aumid = family + "!Other" },
                identity with { PackageFullName = "Fixture.Native_1.0.0.1_x64__abcd" }, identity with { Family = "Fixture.Native_other" },
                identity with { Pid = 0 }, identity with { Creation = 0 }, identity with { Session = session + 1 },
                identity with { NativeX64 = false }, identity with { AppContainer = false }
            ];
            check(wrongTargets.All(target => !target.Matches(command, plan, session)),
                "Live gate refuses wrong image, AUMID, full package, family, birth, session, architecture or token");
        }
        finally
        {
            File.Delete(manifest);
            File.Delete(executable);
            Directory.Delete(directory);
        }
    }

    private sealed class AppContainerCatalog(IReadOnlyList<RegisteredPackage> packages) : IRegisteredPackageCatalog
    {
        public IReadOnlyList<RegisteredPackage> ForFamily(string family) => packages;
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
