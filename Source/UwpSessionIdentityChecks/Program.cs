using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using GamingMode.Services;
using Playhub.GameSession;
using Playhub.Shared;

internal static class IdentityChecks
{
    private static int _passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _passed++; Console.WriteLine("PASS " + name); }
    private static async Task Main()
    {
        foreach (string bootstrap in new[] { "gamingservicesui", "GamingServicesUI.EXE", "gamelaunchhelper.exe", "gamingservices", "gamingservicesnet" })
            Check(SteamGameIdentityPolicy.IsXboxLaunchHelper(bootstrap), "Xbox bootstrap excluded: " + bootstrap);
        foreach (string game in new[] { "DOOM64_x64.exe", "Cuphead.exe", "Rustler.exe", "DREDGE.exe" })
            Check(!SteamGameIdentityPolicy.IsXboxLaunchHelper(game), "Actual game remains eligible: " + game);
        Check(UwpSteamSessionAssociation.FindForApp(0).Count == 0, "Unselected shortcut cannot enumerate UWP sessions");
        string brokerFolder = Path.Combine(Path.GetTempPath(), "PlayhubReadOnlyAuth", Guid.NewGuid().ToString("N"));
        try
        {
            int shellWrites = 0;
            var broker = new XboxShellBroker(brokerFolder, () => false, () => shellWrites++);
            string token = File.ReadAllText(Path.Combine(brokerFolder, "xbox-shell-token"));
            Check(broker.IsAuthorized(System.Net.IPAddress.Loopback, token), "Authenticated loopback read query admitted");
            Check(!broker.IsAuthorized(System.Net.IPAddress.Parse("192.0.2.1"), token), "Remote read query rejected despite correct token");
            Check(!broker.IsAuthorized(System.Net.IPAddress.Loopback, "stale"), "Stale session token rejected");
            Check(shellWrites == 0, "Read-only authorization never starts Explorer");
        }
        finally { Directory.Delete(brokerFolder, true); }
        long clock = 1000; int chords = 0; bool verified = true;
        var guide = new GameBarGuideService(app => { if (!verified) return new(false, false, "unverifiedForeground"); chords++; return new(true, true, "opened"); }, () => clock);
        Check(!guide.Handle("zero", 0, true).Sent && chords == 0, "No selected Xbox session sends no chord");
        Check(guide.Handle("press1", 12, true).Sent && chords == 1, "Verified foreground receives one completed Win G chord");
        Check(!guide.Handle("repeat", 12, true).Sent && chords == 1, "Held guide repeat cannot toggle twice");
        guide.Handle("unrelated", 12, false);
        Check(!guide.Handle("repeat2", 12, true).Sent, "Unrelated release cannot clear the held edge");
        guide.Handle("press1", 12, false);
        Check(!guide.Handle("press1", 12, true).Sent, "Replayed request remains rejected after release");
        Check(!guide.Handle("press2", 12, true).Sent, "Duplicate event source is time debounced");
        clock += 300;
        Check(guide.Handle("press3", 12, true).Sent && chords == 2, "New completed guide pulse can close verified Game Bar");
        clock += 2100;
        Check(guide.Handle("press4", 12, true).Sent && chords == 3, "Lost release expires bounded held state");
        guide.Handle("press4", 12, false); clock += 300; verified = false;
        Check(!guide.Handle("ordinary", 12, true).Sent && chords == 3, "Ordinary foreground or ended session never receives chord");
        Check(!guide.Handle("", 12, true).Ok, "Malformed edge identity rejected");
        int visibleChords = 0;
        var alreadyVisible = new GameBarGuideService(app => new(true, false, "alreadyVisible"), () => clock);
        Check(alreadyVisible.Handle("visible", 12, true) is { Ok: true, Sent: false, Reason: "alreadyVisible" } && visibleChords == 0,
            "Already visible Game Bar is consumed without a second chord that would close it");
        int interactionReads = 0, sends = 0;
        GameBarGuideResult SendChord() { sends++; return new(true, true, "opened"); }
        var interactive = GameBarInteractionGuard.Run(true, () => false, () => { interactionReads++; return true; }, SendChord);
        Check(interactive is { Ok: true, Sent: false, Reason: "alreadyInteractive" } && sends == 0,
            "Interactive GPU composed Game Bar consumes Guide without closing chord or foreground HWND");
        Check(interactionReads == 1, "Already interactive bar performs only one state query");
        var closed = GameBarInteractionGuard.Run(true, () => true, () => false, SendChord);
        Check(closed.Sent && sends == 1, "Closed bar on verified game sends one complete chord");
        var pinned = GameBarInteractionGuard.Run(true, () => true, () => false, SendChord);
        Check(pinned.Sent && sends == 2, "Pinned widget presence has no effect on authoritative closed input state");
        interactionReads = 0;
        var unverified = GameBarInteractionGuard.Run(false, () => true, () => { interactionReads++; return true; }, SendChord);
        Check(!unverified.Ok && sends == 2 && interactionReads == 0, "Identity remains required before any bar state or input");
        var ordinary = GameBarInteractionGuard.Run(true, () => false, () => false, SendChord);
        Check(!ordinary.Ok && sends == 2, "Closed bar with ordinary foreground sends no chord");
        var unavailable = GameBarInteractionGuard.Run(true, () => true, () => throw new InvalidOperationException("fixture unavailable"), SendChord);
        Check(unavailable is { Ok: false, Sent: false, Reason: "gameBarStateUnavailable" } && sends == 2,
            "Unavailable WinRT state fails closed without widget or generic keyboard fallback");
        interactionReads = 0;
        var openedDuringQuery = GameBarInteractionGuard.Run(true, () => true, () => ++interactionReads == 2, SendChord);
        Check(openedDuringQuery is { Ok: true, Sent: false } && sends == 2, "OS opens bar between state reads and suppresses duplicate chord");
        interactionReads = 0;
        var failedRecheck = GameBarInteractionGuard.Run(true, () => true, () => { if (++interactionReads == 2) throw new Exception("fixture"); return false; }, SendChord);
        Check(!failedRecheck.Ok && sends == 2, "Second state read failure also sends no chord");
        string helper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playhub", "Playhub.GameSession.exe");
        DateTime trackedAt = DateTime.Now;
        long wrapperBirth = trackedAt.AddMilliseconds(-100).ToUniversalTime().ToFileTimeUtc();
        long gameBirth = wrapperBirth + 1;
        var session = new UwpSteamSessionAssociation.Tracked(10, 10674787038353424384, trackedAt, helper, "Fixture_abc!App", "Cuphead.exe");
        var identity = new UwpSessionIdentity(10, wrapperBirth, 20, gameBirth, session.Aumid, "Fixture_abc", @"C:\Fixture\Cuphead.exe");
        bool Match(UwpSessionIdentity? value=null, uint server=10, uint game=20, uint app=2485417537, long? wb=null, long? gb=null, string? image=null, string? package=null) =>
            UwpSteamSessionAssociation.Matches(session, value??identity, server, game, app, wb??wrapperBirth, gb??gameBirth, image??helper, package??identity.PackageFamily, identity.Executable);
        Check(Match(), "exact Steam wrapper app AUMID package and both births accepted");
        Check(!Match(server:11), "another pipe server PID rejected");
        Check(!Match(wb:wrapperBirth+1), "recycled wrapper PID birth rejected");
        Check(!Match(gb:gameBirth+1), "recycled game PID birth rejected");
        Check(!Match(game:21), "different actual game PID rejected");
        Check(!Match(app:620), "different selected Steam app rejected");
        Check(!Match(image:@"C:\Fixture\Playhub.GameSession.exe"), "same helper basename at untrusted path rejected");
        Check(!Match(package:"Other_abc"), "same executable in different package rejected");
        Check(!Match(identity with { Aumid="Fixture_abc!Other" }), "different AUMID in same package rejected");
        Check(!Match(identity with { WrapperPid=11 }), "response wrapper identity cannot override OS pipe owner");
        Check(!Match(identity with { Executable=@"C:\Other\Cuphead.exe" }), "response executable must match actual image");
        string added=$"[{trackedAt:yyyy-MM-dd HH:mm:ss}] AppID {session.GameId} adding PID 10 as a tracked process \"\"{helper}\" --uwp \"Fixture_abc!App\" \"Cuphead.exe\" \"\"\"";
        Check(UwpSteamSessionAssociation.Parse(added).Single().Aumid==session.Aumid, "real Steam double-quoted production command parsed without losing 64-bit app ID");
        Check(UwpSteamSessionAssociation.Parse(added+"\n[2026-10-03 16:00:00] AppID 1 no longer tracking PID 10").Count==0, "Steam process removal invalidates association");
        Check(UwpSteamSessionAssociation.Parse(added+"\n[2026-10-03 16:00:00] Client version: fixture").Count==0, "Steam client session reset invalidates association");
        Check(UwpSteamSessionAssociation.Parse(added+"\n[2026-10-03 16:00:00] AppID 620 adding PID 10 as a tracked process other.exe").Count==0, "same PID re-tracked as non-helper removes prior association");

        using var process=Process.GetCurrentProcess();
        long birth=process.StartTime.ToUniversalTime().ToFileTimeUtc();
        var fixture=identity with {WrapperPid=(uint)process.Id,WrapperBirth=birth};
        var publication=new UwpIdentityPublication(fixture);
        async Task<UwpSessionIdentity> Read()
        {
            using var timeout=new CancellationTokenSource(1500);
            using var client=new NamedPipeClientStream(".",UwpSessionIdentity.PipeName(fixture.WrapperPid,birth),PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token);
            Check(GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(),out uint owner)&&owner==(uint)process.Id,"actual pipe OS owner is exact publisher process");
            using var reader=new StreamReader(client);
            return JsonSerializer.Deserialize<UwpSessionIdentity>((await reader.ReadLineAsync(timeout.Token))!)!;
        }
        Check(await Read()==fixture,"production CurrentUserOnly pipe transmits complete immutable identity");
        Check(await Read()==fixture,"repeated client does not replace or duplicate game identity");
        var watch=Stopwatch.StartNew();publication.Dispose();
        Check(watch.ElapsedMilliseconds<1000,"idle WaitForConnection cancels at game lifetime end");
        using (var client=new NamedPipeClientStream(".",UwpSessionIdentity.PipeName(fixture.WrapperPid,birth),PipeDirection.In,PipeOptions.Asynchronous))
        {
            using var timeout=new CancellationTokenSource(80);
            bool disconnected=false;try{await client.ConnectAsync(timeout.Token);}catch(OperationCanceledException){disconnected=true;}
            Check(disconnected,"disposed publisher leaves no listener after game exit");
        }
        var runtime=new FixtureRuntime();
        Check(UwpLifetime.Run("Fixture_abc!App","Cuphead.exe","raw",runtime)==7&&runtime.Publications==1&&runtime.PublicationDisposed,
            "identity publication is exactly once and disposed by unchanged game wait lifecycle");
        Console.WriteLine($"UWP identity checks: {_passed} passed.");
    }
    private sealed class FixtureRuntime : IUwpRuntime
    {
        public long Now{get;private set;} public bool ShellReady=>true; public int Publications;public bool PublicationDisposed;
        public void StartShell()=>throw new Exception();public void Delay(int ms)=>Now+=ms;public void Log(string text){}
        public IUwpProcess Activate(string aumid,string args)=>new FixtureProcess();public IUwpProcess? FindGame(string f,string e)=>null;
        public IDisposable PublishIdentity(string aumid,IUwpProcess game){Publications++;return new Release(()=>PublicationDisposed=true);}
    }
    private sealed class Release(Action action):IDisposable{public void Dispose()=>action();}
    private sealed class FixtureProcess:IUwpProcess{public uint Id=>20;public string Executable=>"Cuphead.exe";public string PackageFamily=>"Fixture_abc";public int WaitForExit()=>7;public void Dispose(){}}
    [DllImport("kernel32.dll")]private static extern bool GetNamedPipeServerProcessId(nint pipe,out uint owner);
}
