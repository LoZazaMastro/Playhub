using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GamingMode.Services;
using Microsoft.Win32.SafeHandles;

internal sealed class FakeProcessApi : IOwnedProcessApi
{
    internal readonly Dictionary<uint, OwnedProcessFacts> Processes = new();
    internal readonly Dictionary<SafeProcessHandle, (OwnedProcessFacts Facts, bool Live)> Handles = new();
    internal Action? BeforeAlive { get; set; }
    internal bool ThrowRead;
    public SafeProcessHandle Open(uint pid)
    {
        var h = new SafeProcessHandle((nint)(Handles.Count + 1), ownsHandle: false);
        Handles[h] = (Processes.GetValueOrDefault(pid), Processes.ContainsKey(pid)); return h;
    }
    public OwnedProcessFacts? Read(SafeProcessHandle h, uint pid)
    { if (ThrowRead) throw new IOException("fake read"); return Handles[h].Live ? Handles[h].Facts : null; }
    public bool Alive(SafeProcessHandle h, OwnedProcessFacts expected)
    { BeforeAlive?.Invoke(); return !h.IsClosed && Handles[h].Live && Handles[h].Facts == expected; }
    internal void Exit(uint pid)
    { foreach (var h in Handles.Keys.ToArray()) if (Handles[h].Facts.Pid == pid) Handles[h] = (Handles[h].Facts, false); Processes.Remove(pid); }
    internal void Replace(uint pid) { var old = Processes[pid]; Exit(pid); Processes[pid] = old with { Birth = old.Birth + 1 }; }
    internal bool AllClosed => Handles.Keys.All(h => h.IsClosed);
}
internal sealed class Fixture : IWin32OwnedSystem, IDisposable
{
    internal const uint Canonical = 2262950469;
    internal const ulong Full = ((ulong)Canonical << 32) | 0x02000000;
    internal const string Wrapper = @"C:\Playhub\Playhub.GameSession.exe";
    internal readonly DateTime Start = new(2026, 11, 12, 5, 0, 0, DateTimeKind.Local);
    internal readonly FakeProcessApi Api = new();
    internal (uint Pid, string Image)? Registration = (100, @"C:\Steam\steam.exe");
    internal UwpSteamSessionAssociation.InstalledImages Images = new(Wrapper, @"C:\Playhub\Playhub.XboxSession.exe");
    internal bool Signed = true;
    internal readonly Dictionary<uint, OwnedHandoff> Publications = new();
    internal string Text = "", FileId = "file-one";
    internal long Offset, Now = 10000;
    internal (nint Window, uint Pid)? ForegroundValue = (new nint(555), 300);
    internal Func<uint, CancellationToken, Task>? BeforeHandoff;
    internal int PipeReads;
    internal Playhub.Shared.EpicLaunchProvenance? ExpectedEpic;
    internal bool EpicFull = true, EpicFast = true;
    internal readonly List<FakeEpic> EpicLeases = new();
    public IOwnedEpicProvenance? RetainEpic(Playhub.Shared.EpicLaunchProvenance proof, uint wrapperPid, long wrapperBirth)
    {
        if (proof != ExpectedEpic || proof.WrapperPid != wrapperPid || proof.WrapperBirth != wrapperBirth || !EpicFull) return null;
        var lease = new FakeEpic(this); EpicLeases.Add(lease); return lease;
    }
    internal sealed class FakeEpic(Fixture f) : IOwnedEpicProvenance
    {
        internal int Disposals;
        public bool Current => Disposals == 0 && f.EpicFull && f.EpicFast;
        public bool RetainedCurrent => Disposals == 0 && f.EpicFast;
        public void Dispose() { if (++Disposals != 1) throw new Exception("Epic scope double dispose"); }
    }
    internal void EpicLaunch()
    {
        Api.Processes[300] = Api.Processes[300] with { Image = @"C:\Epic\Rustler\Rustler.exe" };
        var w = Api.Processes[200]; var g = Api.Processes[300];
        ExpectedEpic = new(1, new string('a',32), Playhub.Shared.EpicLaunchCatalog.RustlerNamespace,
            Playhub.Shared.EpicLaunchCatalog.RustlerItem, Playhub.Shared.EpicLaunchCatalog.RustlerApp,
            @"C:\EpicMeta\rustler.item", new string('b',64), @"C:\Epic\Rustler", g.Image,
            w.Pid,w.Birth,400,Start.AddSeconds(-10).ToFileTimeUtc(),@"C:\EpicLauncher\EpicGamesLauncher.exe",new string('c',64),
            w.Birth+1,g.Pid,g.Birth,555,0);
        Publications[200] = new(200,new(200,w.Birth,300,g.Birth,Full,g.Image,ExpectedEpic));
        Text = Row(-1,Full,"adding PID 200 as a tracked process");
    }
    internal readonly WindowsOwnedAssociationRuntime Runtime;
    internal readonly Win32OwnedAssociationCatalog Catalog;
    internal Fixture(bool alias = true)
    {
        Api.Processes[100] = Facts(100, -20, @"C:\Steam\steam.exe");
        AddPair(200, 300, 0, alias ? 480 : null);
        Runtime = new(this); Catalog = new(Runtime);
    }
    private OwnedProcessFacts Facts(uint pid, int seconds, string path) => new(pid, Start.AddSeconds(seconds).ToFileTimeUtc(), path, "S-1-5-21-fixture", 1);
    internal void AddPair(uint wrapper, uint game, int seconds, uint? alias)
    {
        Api.Processes[wrapper] = Facts(wrapper, seconds - 1, Wrapper);
        Api.Processes[game] = Facts(game, seconds, @"C:\Games\Forza Horizon 6\forzahorizon6.exe");
        Publications[wrapper] = new(wrapper, new(wrapper, Api.Processes[wrapper].Birth, game, Api.Processes[game].Birth, Full, Api.Processes[game].Image));
        Text += Row(seconds - 1, Full, $"adding PID {wrapper} as a tracked process") + Row(seconds, Full, $"adding PID {game} as a tracked process");
        if (alias is uint id) Text += Row(seconds + 1, Full, $"no longer tracking PID {game}") + Row(seconds + 1, id, $"adding PID {game} as a tracked process");
    }
    internal string Row(int seconds, ulong id, string rest) => $"[{Start.AddSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] AppID {id} {rest}\n";
    public IOwnedProcessApi Processes => Api;
    public OwnedProcessFacts Self => Facts(999, 0, @"C:\Playhub\GamingMode.exe");
    public (uint Pid, string Image)? SteamRegistration() => Registration;
    public UwpSteamSessionAssociation.InstalledImages InstalledImages() => Images;
    public bool ValveImage(string path) => Signed;
    public Task<OwnedLogWindow?> Log(string root, CancellationToken token)
    { token.ThrowIfCancellationRequested(); byte[] b = Encoding.UTF8.GetBytes(Text); return Task.FromResult<OwnedLogWindow?>(new(FileId, Offset, Offset + b.Length, b)); }
    public async Task<OwnedHandoff?> Handoff(uint wrapper, long birth, CancellationToken token)
    { PipeReads++; if (BeforeHandoff is not null) await BeforeHandoff(wrapper, token); token.ThrowIfCancellationRequested(); return Publications.GetValueOrDefault(wrapper); }
    public (nint Window, uint Pid)? Foreground() => ForegroundValue;
    public long Tick => Now;
    public DateTime LocalTime => Start.AddSeconds(10);
    public void Dispose() { Catalog.Dispose(); if (EpicLeases.Any(e => e.Disposals != 1)) throw new Exception("Epic lease leaked"); if (!Api.AllClosed) throw new Exception("An opened fake retained handle leaked after fixture disposal."); }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool pass, string message) { checks++; if (!pass) throw new Exception(message); }
    private static Task<Win32OwnedAssociationCatalog.Lease?> Find(Fixture f) => f.Catalog.FindAsync(Fixture.Canonical, default);
    internal static async Task Main()
    {
        await FirstObservedIdentityChecks.Run(Check);
        using (var f = new Fixture())
        {
            using var a = await Find(f); Check(a?.Current == true, "actual Windows runtime adapter captures authenticated same-child alias");
            Check(a!.Route?.ObservedSteamGameId == "480" && a.Route.CanonicalShortcutAppId == Fixture.Canonical, "observed/canonical separate");
            Check(f.PipeReads == 1, "ordinary first capture adds no redundant private read");
            Check(a.Route!.GameBirth == f.Api.Processes[300].Birth.ToString(CultureInfo.InvariantCulture), "birth string exact");
            int opens = f.Api.Handles.Count; Check(a.Current && f.Api.Handles.Count == opens, "Current never reopens PID");
            using var b = await Find(f); Check(b?.Current == true && f.Api.Handles.Count == opens, "same retained cache reused");
            Check(f.PipeReads == 2, "one bounded private refresh per request");
            var route = await f.Catalog.DiscoverAsync(480, default); Check(route?.CanonicalShortcutAppId == Fixture.Canonical && route.RoutingOnly, "foreground discovery exact original transaction");
            Check(await f.Catalog.DiscoverAsync(570, default) is null, "different observed hint not mapped");
            f.Now += 3600000; f.FileId = "rolled-file"; f.Text = "unrelated new log\n"; f.Offset = 0;
            using var longSession = await Find(f); Check(longSession?.Current == true, "long retained session survives bounded tail rollover");
            Check(longSession!.Route?.ObservedFresh == false, "rolled history not represented as fresh observed identity");
            Check((await f.Catalog.DiscoverAsync(480, default))?.CanonicalShortcutAppId == Fixture.Canonical, "cached exact foreground discovery after rollover");
            f.Catalog.Dispose(); Check(!a.Current && !b!.Current, "catalog shutdown revokes borrowed leases");
        }
        using (var f = new Fixture(false)) { using var a = await Find(f); Check(a?.Route?.ObservedSteamGameId == Fixture.Full.ToString(), "ordinary canonical path retained"); }
        using (var f = new Fixture()) { f.Publications.Clear(); Check(await Find(f) is null, "missing private publication refused"); }
        using (var f = new Fixture()) { f.Publications[200] = f.Publications[200] with { ServerPid = 777 }; Check(await Find(f) is null, "wrong private server refused"); }
        using (var f = new Fixture()) { f.Api.Processes.Remove(200); Check(await f.Catalog.DiscoverAsync(480, default) is null, "direct FH6 with no wrapper remains absent"); }
        using (var f = new Fixture()) { f.AddPair(201, 301, 2, 480); Check(await Find(f) is null, "multiple authenticated launches ambiguous"); }
        using (var f = new Fixture()) { using var a = await Find(f); f.AddPair(201, 301, 2, 480); Check(await Find(f) is null && !a!.Current, "new ambiguity revokes old lease"); }
        foreach (uint pid in new uint[] { 100, 200, 300 }) using (var f = new Fixture())
        { using var a = await Find(f); f.Api.Replace(pid); Check(!a!.Current, $"retained {pid} replacement refused without PID following"); }
        using (var f = new Fixture()) { using var a = await Find(f); f.Registration = (101, @"C:\Steam\steam.exe"); Check(!a!.Current, "registered Steam generation change while old alive refused"); }
        using (var f = new Fixture()) { using var a = await Find(f); f.Images = f.Images with { Wrapper = @"C:\Other\Playhub.GameSession.exe" }; Check(!a!.Current, "installed image identity change refused"); }
        using (var f = new Fixture()) { using var a = await Find(f); f.Text += f.Row(3, Fixture.Full, "no longer tracking PID 200"); Check(await Find(f) is null && !a!.Current, "explicit wrapper untracking revokes"); }
        using (var f = new Fixture()) { using var a = await Find(f); f.Text += f.Row(3, 480, "no longer tracking PID 300"); Check(await Find(f) is null && !a!.Current, "incomplete child transfer does not stay current"); }
        using (var f = new Fixture()) { using var a = await Find(f); f.ForegroundValue = (new nint(555), 999); Check(await f.Catalog.DiscoverAsync(480, default) is null, "foreign foreground no route"); }
        using (var f = new Fixture()) { f.Signed = false; Check(await Find(f) is null, "non-Valve Steam refused"); }
        using (var f = new Fixture()) { f.Api.Processes[300] = f.Api.Processes[300] with { Sid = "foreign" }; Check(await Find(f) is null, "different game user refused"); }
        using (var f = new Fixture()) { f.BeforeHandoff = (_, _) => { f.Catalog.Dispose(); return Task.CompletedTask; }; Check(await Find(f) is null && f.Api.AllClosed, "dispose during await has exact cleanup, no deadlock"); }
        using (var f = new Fixture())
        {
            using var canceled = new CancellationTokenSource();
            f.BeforeHandoff = (_, token) => { canceled.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
            try { using var unexpected = await f.Catalog.FindAsync(Fixture.Canonical, canceled.Token); Check(false, "cancellation expected"); }
            catch (OperationCanceledException) { Check(true, "cancellation preserved"); }
            f.Catalog.Dispose(); Check(f.Api.AllClosed, "cancel cleanup closes each opened handle");
        }
        using (var f = new Fixture()) { using var a = await Find(f); f.Api.BeforeAlive = () => { f.Api.BeforeAlive = null; f.Catalog.Dispose(); }; Check(!a!.Current, "dispose during retained getter cannot return stale current"); }
        using (var f = new Fixture())
        {
            using var old = await Find(f); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.BeforeHandoff = (_, _) => release.Task;
            Task<Win32OwnedAssociationCatalog.Lease?>? refreshing = null;
            f.Api.BeforeAlive = () => { f.Api.BeforeAlive = null; refreshing = Find(f); };
            Check(old!.Current, "independent pending refresh does not revoke an already fresh same-session proof");
            release.SetResult(); using var renewed = await refreshing!; Check(renewed?.Current == true, "fresh handoff reestablishes same identity after refresh");
        }
        using (var f = new Fixture())
        {
            using var old = await Find(f); var publication = f.Publications[200]; f.Publications.Remove(200);
            Check(await Find(f) is null && !old!.Current, "unknown private refresh revokes previous request proof");
            f.Publications[200] = publication; using var fresh = await Find(f);
            Check(fresh?.Current == true && !old!.Current, "fresh handoff recovers only a new request epoch");
        }
        using (var f = new Fixture())
        {
            f.BeforeHandoff = (_, _) => throw new System.Text.Json.JsonException("fake malformed private publication");
            Check(await Find(f) is null, "malformed initial publication becomes unavailable with exact handle cleanup");
        }
        using (var f = new Fixture())
        {
            using var old = await Find(f);
            f.BeforeHandoff = (_, _) => throw new System.Text.Json.JsonException("fake malformed private refresh");
            Check(await Find(f) is null && !old!.Current, "malformed refresh becomes unavailable and revokes old request");
            f.BeforeHandoff = null; using var recovered = await Find(f);
            Check(recovered?.Current == true && !old!.Current, "only new borrow recovers after malformed private refresh");
        }
        using (var f = new Fixture(false))
        {
            f.EpicLaunch(); using var lease = await Find(f);
            Check(lease?.Current == true && lease.Identity.Epic == f.ExpectedEpic,"positive typed Epic retained lease can replace missing game Steam ancestry");
            Check(lease!.Route?.ObservedSteamGameId is null && lease.Route?.ObservedFresh == false,"Epic provenance never manufactures observed full game ID or480");
            Check(lease.Route!.OriginalGameId==Fixture.Full.ToString() && lease.Route.CanonicalShortcutAppId==Fixture.Canonical,"original wrapper canonical transaction remains exact");
            Check((await f.Catalog.DiscoverUiAsync(null,Fixture.Canonical,default))?.GamePid==300,"raw null routes via exact retained foreground and canonical hint");
            Check(await f.Catalog.DiscoverAsync(480,default) is null,"unobserved480 never becomes a global Epic mapping");
            f.Now += 3600000;f.FileId="rollover";f.Text="new tail";
            using var later=await Find(f);Check(later?.Current==true && later.Route?.ObservedSteamGameId is null,"long Epic session survives tail rollover without inventing identity");
            var publication=f.Publications[200];
            f.Publications[200]=System.Text.Json.JsonSerializer.Deserialize<OwnedHandoff>(System.Text.Json.JsonSerializer.Serialize(publication))!;
            using var roundtrip=await Find(f);Check(roundtrip?.Current==true,"scalar Epic provenance equality survives a new JSON roundtrip");
            f.EpicFast=false;Check(!lease.Current && !roundtrip!.Current,"lost exact retained Epic generation revokes borrowed leases");
        }
        using(var f=new Fixture(false)){f.EpicLaunch();Check((await f.Catalog.DiscoverUiAsync(Fixture.Canonical,null,default))?.GamePid==300,"cold Epic UI discovery can authenticate tracked wrapper private handoff");}
        using(var f=new Fixture(false)){f.EpicLaunch();f.ForegroundValue=(new nint(999),999);Check(await f.Catalog.DiscoverUiAsync(null,Fixture.Canonical,default) is null,"foreign foreground cannot acquire the canonical hint");}
        using(var f=new Fixture(false)){f.EpicLaunch();f.EpicFull=false;Check(await Find(f) is null,"unverified optional provenance leaves normal URI unowned");}
        using(var f=new Fixture(false)){f.EpicLaunch();f.Publications[200]=f.Publications[200] with { Publication=f.Publications[200].Publication with { Epic=null } };Check(await Find(f) is null,"missing Epic proof never relaxes ordinary SteamInitialIdentity");}
        using(var f=new Fixture(false))
        {
            f.EpicLaunch();using var old=await Find(f);f.EpicFull=false;
            Check(await Find(f) is null && !old!.Current,"metadata/registered-Epic drift in full refresh revokes previous request");
        }
        using(var f=new Fixture(false))
        {
            f.EpicLaunch();using var old=await Find(f);f.Text+=f.Row(3,480,"adding PID 300 as a tracked process");
            using var observed=await Find(f);Check(observed?.Route?.ObservedSteamGameId=="480" && observed.Route.ObservedFresh,"only a real later game event can add observed480");
        }
        using(var f=new Fixture(false)){f.EpicLaunch();f.Publications[200]=f.Publications[200] with { Publication=f.Publications[200].Publication with { Epic=f.ExpectedEpic! with { GameBirth=f.ExpectedEpic!.GameBirth+1 } } };Check(await Find(f) is null,"handoff game/provenance birth mismatch rejected");}
        using(var f=new Fixture(false)){f.EpicLaunch();f.Publications[200]=f.Publications[200] with { ServerPid=999 };Check(await Find(f) is null,"typed provenance cannot replace authenticated pipe server PID");}
        var badApi = new FakeProcessApi { ThrowRead = true }; badApi.Processes[1] = new(1, 1, "x", "s", 1);
        try { using var unused = RetainedOwnedWin32Process.Open(badApi, 1); } catch (IOException) { }
        Check(badApi.AllClosed, "actual retained adapter closes handle on read exception");
        using (var f = new Fixture())
        {
            using var first = await Find(f);
            f.Text += f.Row(3, 570, "adding PID 300 as a tracked process");
            Check(await Find(f) is null && !first!.Current, "alias transfer without removal refused on retained cache");
        }
        using (var f = new Fixture())
        {
            using var first = await Find(f);
            f.Text += f.Row(3, 480, "no longer tracking PID 300") + f.Row(3, 570, "adding PID 300 as a tracked process");
            using var next = await Find(f); Check(!first!.Current, "old request epoch cannot revive after alias transition"); Check(next?.Route?.ObservedSteamGameId == "570", "second explicit same-child transfer supported without special480");
            Check((await f.Catalog.DiscoverAsync(570, default))?.CanonicalShortcutAppId == Fixture.Canonical, "arbitrary alias routing hint supported");
        }
        using (var f = new Fixture()) { f.Text += f.Row(30, 480, "no longer tracking PID 300"); Check(await Find(f) is null, "future log event not current proof"); }
        using (var f = new Fixture()) { f.Text += f.Row(3, 480, "no longer tracking PID 300") + f.Row(3, ulong.MaxValue, "adding PID 300 as a tracked process"); Check(await Find(f) is null, "invalid full alias gameID refused"); }
        using (var f = new Fixture())
        {
            var field = typeof(Win32SteamSessionAssociation).GetField("owned", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            field.SetValue(null, f.Catalog);
            using var lease = await Win32SteamSessionAssociation.FindLeaseAsync(Fixture.Canonical, default);
            Check(lease?.Current == true, "actual facade returns shared retained lease");
            Check((await Win32SteamSessionAssociation.FindAsync(Fixture.Canonical, default))?.GameId == Fixture.Full, "compat FindAsync returns original canonical identity");
            Check((await Win32SteamSessionAssociation.DiscoverOwnedRouteAsync(480, default))?.GamePid == 300, "actual facade bounded route discovery");
            Win32SteamSessionAssociation.DisposeOwned(); Check(!lease!.Current, "actual facade teardown revokes lease");
            Check(await Win32SteamSessionAssociation.FindLeaseAsync(Fixture.Canonical, default) is null, "shutdown cannot recreate native catalog");
        }
        string exe = @"C:\Steam\gameoverlayui64.exe";
        string[] renderer = [exe, "-pid", "400", "-steampid", "100", "-gameid", Fixture.Full.ToString(CultureInfo.InvariantCulture)];
        Check(NativeSteamOverlayHost.Matches(renderer, exe, 400, 100, Fixture.Full), "unchanged real renderer policy accepts original companion transaction");
        Check(!NativeSteamOverlayHost.Matches(renderer, exe, 400, 100, 480), "unchanged real renderer policy never treats480as shortcut renderer");
        Check(!NativeSteamOverlayHost.Matches(renderer, exe, 401, 100, Fixture.Full), "foreign companion remains refused");
        var nativeInfo = typeof(NativeOwnedAssociationSystem).GetNestedType("FileInfo", System.Reflection.BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(nativeInfo) == 52, "native file identity ABI matches BY_HANDLE_FILE_INFORMATION");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { checks, passed = true, adapter = "actual Windows orchestration with fake API/handle/pipe seams", nativeCalls = 0, gamesOpened = 0 }));
    }
}
