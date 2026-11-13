using Playhub.GameSession;
using Playhub.Shared;

if (args.Length == 5 && args[0] == "--resolve")
{
    var value=EpicLaunchCatalog.ResolveInstalled(args[1],args[2],args[3],args[4]);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{available=value is not null,plan=value,readOnly=true,protocolDispatches=0,customAssociation=false}));
    return;
}
int checks = 0;
void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
var fn = new EpicInstalledLaunch("fn", "4fe75bbc5a674f4f9b356b5c90567da5", "Fortnite", @"C:\Fixture\fn.egi", new string('a',64),
    @"C:\Epic Games\Fortnite", @"C:\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteBootstrapper.exe",
    "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true");
string arguments = EpicShortcutArguments.Build(fn.Namespace, fn.CatalogItemId, fn.AppName, fn.GameExecutable);
Check(arguments.Contains("--epic") && !arguments.Contains("--game ") && !arguments.Contains("AUTH"), "Epic export mode carries catalog identity, never auth or raw game launch");
Check(EpicShortcutArguments.TryRead(new[] { "--epic", fn.Namespace, fn.CatalogItemId, fn.AppName, fn.GameExecutable }, out var parsed)
    && parsed!.GameExecutable == fn.GameExecutable, "actual Program entry arguments preserve exact bootstrapper identity");
Check(!EpicShortcutArguments.TryRead(new[] { "--epic", "fn?other", fn.CatalogItemId, fn.AppName, fn.GameExecutable }, out _), "catalog injection refused");
Check(!EpicShortcutArguments.TryRead(new[] { "--epic", fn.Namespace, fn.CatalogItemId, fn.AppName, "relative.exe" }, out _), "relative target refused");
Check(!EpicShortcutArguments.TryRead(new[] { "--epic", fn.Namespace, fn.CatalogItemId, fn.AppName, fn.GameExecutable, "extra" }, out _), "raw argument tail not forwarded through URI");
var f = new Fake(fn);
int result = EpicProtocolSession.Run(fn, f);
Check(result == 0 && f.Dispatches.Count == 1 && f.Dispatches[0] == fn.LaunchUri, "normal Epic URI dispatched exactly once");
Check(f.PayloadObserved && f.Tick >= 1000 && f.Tick < EpicProtocolSession.BootstrapGapMs, "actual payload exit ends lifetime promptly, not launcher exit");
Check(f.Resources.All(p => p.Disposals == 1), "every retained snapshot process is disposed exactly once");
Check(f.Logs.All(log => !log.Contains("AUTH_PASSWORD")), "no auth parameter replay/logging");
var initialPayload = new Fake(fn) { ExistingPayload = true };
bool refused = false; try { EpicProtocolSession.Run(fn, initialPayload); } catch (InvalidOperationException) { refused = true; }
Check(refused && initialPayload.Dispatches.Count == 0, "already-running payload refuses a second launch");
Check(initialPayload.Resources.All(p => p.Disposals == 1), "preflight refusal releases observed handles");
var missing = new Fake(fn) { NoProcesses = true };
Check(EpicProtocolSession.Run(fn, missing) == 2 && missing.Tick >= EpicProtocolSession.StartupBudgetMs && missing.Dispatches.Count == 1,
    "auth/no-payload wait bounded without launcher-as-game success");
var blocked = new Fake(fn) { DispatchFails = true };
bool failed = false; try { EpicProtocolSession.Run(fn, blocked); } catch (IOException) { failed = true; }
Check(failed && blocked.Dispatches.Count == 1 && blocked.Resources.All(p => p.Disposals == 1), "protocol failure propagates without raw fallback");
var unknown = new Fake(fn) { UnknownPayloadAtExit = true };
Check(EpicProtocolSession.Run(fn, unknown) == 0 && unknown.Tick >= 2000, "unknown retained state holds lifetime until positive exit");
Check(unknown.Resources.All(p => p.Disposals == 1), "unknown/late-exit cleanup once");
var old = new Fake(fn) { OnlyOldProcesses = true };
Check(EpicProtocolSession.Run(fn, old) == 2, "old preexisting nonpayload bootstrapper is never adopted as new game");
Check(old.Resources.All(p => p.Disposals == 1), "old process observation cleanup");
var unreadableImages = EpicProtocolSession.ObservationImages(fn, _ => throw new UnauthorizedAccessException("fixture"));
Check(unreadableImages.SequenceEqual(new[] { fn.GameExecutable }), "optional unreadable directory retains declared observation target and cannot block URI");
IEnumerable<string> PartialFiles(string directory) { yield return Path.Combine(directory,"partial.exe"); throw new IOException("fixture partial directory"); }
Check(EpicProtocolSession.ObservationImages(fn, PartialFiles).SequenceEqual(new[] { fn.GameExecutable }), "partial enumeration is not treated as a complete absence or authority");
var manyImages = EpicProtocolSession.ObservationImages(fn, directory => Enumerable.Range(0,1000).Select(n => Path.Combine(directory,$"candidate{n}.exe")));
Check(manyImages.Count <= 128 && manyImages.Contains(fn.GameExecutable), "optional inventory cap remains bounded without blocking URI");
var observationFailed = new Fake(fn) { ObservationFails = true };
Check(EpicProtocolSession.Run(fn, observationFailed) == 2 && observationFailed.Dispatches.Count == 1,
    "pre-dispatch and later observation errors cannot block the one normal protocol request");
Check(observationFailed.Logs.Count(x => x.Contains("process observation unavailable")) == 1,
    "unavailable observation is reported once and never upgraded to a confirmed game");
Check(f.BindingCalls==1&&f.DispatchesAtBinding==1&&f.BeforeComplete,"optional association is notified only after one normal URI dispatch");
Check(f.Candidates.Any(c=>c.Pid==99&&c.Birth==f.FileTime+2&&c.Window==123),"post-dispatch candidate preserves actual immutable process/window facts");
Check(observationFailed.BindingCalls==1&&!observationFailed.BeforeComplete,"unknown preflight can dispatch Epic but never starts positive association");
var bindingFailure=new Fake(fn){BindingThrows=true};
Check(EpicProtocolSession.Run(fn,bindingFailure)==0&&bindingFailure.Dispatches.Count==1,"optional association exception cannot repeat or block normal launch lifetime");
var incomplete=new Fake(fn){IncompleteObservation=true};
Check(EpicProtocolSession.Run(fn,incomplete)==0&&!incomplete.BeforeComplete,"partial source inventory never becomes empty-before proof");
var lateRole=new Fake(fn){LatePayloadRole=true};
Check(EpicProtocolSession.Run(fn,lateRole)==0&&lateRole.Tick<30000&&lateRole.Resources.All(r=>r.Disposals==1),"post-dispatch metadata payload upgrade replaces exact observed handle and cleans once");
var root = Path.Combine(Path.GetTempPath(), "Epic-Session-Args-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string exe = Path.Combine(root, "FortniteBootstrapper.exe"); File.WriteAllBytes(exe, []);
    string meta = Path.Combine(root, "fixture.item");
    string json = System.Text.Json.JsonSerializer.Serialize(new { AppName="Fortnite", CatalogNamespace="fn",
        CatalogItemId=fn.CatalogItemId, InstallLocation=root, LaunchExecutable="FortniteBootstrapper.exe",
        bIsApplication=true, AppCategories=new[]{"games"}, bIsIncompleteInstall=false, bRequiresAuth=true,
        LaunchCommand="-fixture-must-not-be-replayed", DisplayName="Fortnite" });
    File.WriteAllText(meta, json);
    var plan=EpicLaunchCatalog.Parse(meta,json,"fn",fn.CatalogItemId,"Fortnite",exe,File.Exists);
    Check(plan is not null && plan.LaunchUri == fn.LaunchUri && !plan.LaunchUri.Contains("fixture-must"), "actual metadata URI planner preserves official Fortnite chain");
}
finally { Directory.Delete(root,true); }
Console.WriteLine($"{checks} actual Epic URI/session checks PASS; protocol/native calls=0; no launcher/game started.");

sealed class Fake(EpicInstalledLaunch plan) : IEpicProtocolRuntime, IEpicProtocolBindingRuntime
{
    public long Tick { get; private set; }
    public bool SnapshotComplete => !IncompleteObservation;
    public bool IncompleteObservation, BindingThrows, LatePayloadRole;
    public int BindingCalls, DispatchesAtBinding;
    public bool BeforeComplete;
    public List<EpicObservedCandidate> Candidates = new();
    public void AfterDispatch(long startedAt,bool complete)
    {
        BindingCalls++; DispatchesAtBinding=Dispatches.Count; BeforeComplete=complete;
        if(BindingThrows)throw new IOException("fixture optional binding failure");
    }
    public void ObserveCandidates(IReadOnlyList<EpicObservedCandidate> candidates) => Candidates.AddRange(candidates);
    public long FileTime => 134389561211987069;
    public bool ExistingPayload, NoProcesses, DispatchFails, UnknownPayloadAtExit, OnlyOldProcesses, PayloadObserved, ObservationFails;
    public List<string> Dispatches { get; } = new();
    public List<string> Logs { get; } = new();
    public List<Observed> Resources { get; } = new();
    public void Dispatch(string uri) { Dispatches.Add(uri); if (DispatchFails) throw new IOException("fixture URI failure"); }
    public void Delay(int ms) => Tick += ms;
    public void Log(string text) => Logs.Add(text);
    public IReadOnlyList<IEpicObservedProcess> Snapshot(EpicInstalledLaunch launch)
    {
        if (ObservationFails) throw new IOException("fixture read unavailable");
        if (Dispatches.Count == 0)
        {
            if (!ExistingPayload) return Array.Empty<IEpicObservedProcess>();
            return new[] { Make(true,true) };
        }
        if (NoProcesses) return Array.Empty<IEpicObservedProcess>();
        if (OnlyOldProcesses) return new[] { Make(false,true) };
        if (Tick < 250) return new[] { Make(false,false) };
        if (Tick < 1000) { PayloadObserved = true; return new[] { Make(true,false) }; }
        return Array.Empty<IEpicObservedProcess>();
    }
    private Observed Make(bool payload,bool old)
    {
        var process=new Observed(this,payload,old,plan);Resources.Add(process);return process;
    }
    public sealed class Observed(Fake owner,bool payload,bool old,EpicInstalledLaunch plan) : IEpicObservedProcess
    {
        public int Disposals;
        public uint Pid => payload ? 99u : 77u;
        public long Birth => owner.FileTime + (old ? -1 : payload ? 2 : 1);
        public string Image => payload ? Path.Combine(plan.InstallDirectory,"FortniteGame","Binaries","Win64","FortniteClient-Win64-Shipping.exe") : plan.GameExecutable;
        private readonly bool role=payload&&(!owner.LatePayloadRole||owner.Tick>=500);
        public bool IsPayload => role;
        public long PresentationWindow => payload && State == EpicObservedState.Alive ? 123 : 0;
        public EpicObservedState State => payload
            ? owner.Tick < 1000 ? EpicObservedState.Alive : owner.UnknownPayloadAtExit && owner.Tick < 2000 ? EpicObservedState.Unknown : EpicObservedState.Exited
            : owner.Tick < 250 || old ? EpicObservedState.Alive : EpicObservedState.Exited;
        public void Dispose() { if (++Disposals != 1) throw new Exception("Double-disposed fixture handle"); }
    }
}
