using Playhub.StandaloneHost.Migration;
using System.Security.Cryptography;

int count = 0;
var checksRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../fixtures"));
Directory.CreateDirectory(checksRoot);
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); count++;
}
void Throws(Action action, string name)
{
    try { action(); } catch (Exception e) when (e is IOException or ArgumentException or InvalidDataException) { Check(true, name); return; }
    throw new Exception("FAIL expected rejection: " + name);
}
async Task Rejects(Func<Task> action, string name)
{
    try { await action(); } catch (InvalidOperationException) { Check(true, name); return; }
    throw new Exception("FAIL expected rejection: " + name);
}
Fixture New() => new(checksRoot);

using (var f = New())
{
    var plan = f.Plan(); var runtime = new FakeRuntime(); var coordinator = new MigrationCoordinator(f.Storage);
    var before = Directory.GetFileSystemEntries(f.Root).Length;
    var result = await coordinator.ExecuteAsync(plan, runtime);
    Check(result.Status == "dry-run" && runtime.Calls.Count == 0, "default dry-run never invokes runtime");
    Check(before == Directory.GetFileSystemEntries(f.Root).Length, "default dry-run creates no output");
    Throws(() => f.Storage.Plan([new("escape", "../outside")], "candidate"), "parent traversal rejected");
    Throws(() => f.Storage.Plan([new("absolute", f.Root)], "candidate"), "absolute source rejected");
    Throws(() => f.Storage.Plan([new("one", "input"), new("two", "input/settings")], "candidate"), "overlapping source roots rejected");
    Throws(() => f.Storage.VerifyCurrent(plan with { CandidateInstanceId = "changed" }), "plan identity cannot be changed after planning");
    File.AppendAllText(f.Settings, " ");
    Throws(() => f.Storage.CreateSnapshot(plan), "source change after plan rejects snapshot");
    Check(runtime.Calls.Count == 0, "stale plan cannot retire an owner");
}
using (var f = New())
{
    var plan = f.Plan(); var snapshot = f.Storage.CreateSnapshot(plan);
    f.Storage.VerifySnapshot(snapshot);
    var backup = Path.Combine(f.Root, snapshot.RelativeDirectory, "files/input/settings/quick-settings-2.3.json");
    Check(File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(f.Settings)), "snapshot preserves bytes including unknown preference keys");
    File.AppendAllText(backup, "tamper");
    Throws(() => f.Storage.VerifySnapshot(snapshot), "tampered backup rejected by manifest");
    Throws(() => f.Storage.VerifySnapshot(snapshot with { RelativeDirectory = "../outside" }), "forged backup path rejected");
}
using (var f = New())
{
    var runtime = new FakeRuntime(); var coordinator = new MigrationCoordinator(f.Storage); var plan = f.Plan();
    var original = File.ReadAllBytes(f.Settings);
    var result = await coordinator.ExecuteAsync(plan, runtime, apply: true);
    Check(result.Status == "active" && runtime.Active, "complete handover requires active final attestation");
    Check(runtime.Calls.SequenceEqual(["probe", "retire", "activate", "probe"]), "frontend/backend readiness precede retire and activation follows unload");
    Check(File.ReadAllBytes(f.Settings).SequenceEqual(original), "successful handover never rewrites source preferences");
    Check(result.Snapshot is not null && Directory.GetFiles(Path.Combine(f.Root, result.JournalDirectory!)).Length == 5, "verified backup and journal retained");
    var calls = runtime.Calls.Count;
    await Rejects(() => coordinator.ExecuteAsync(plan, runtime, apply: true), "completed plan cannot replay in coordinator");
    Check(runtime.Calls.Count == calls, "replay does not stop active candidate");
}
foreach (var missing in new[] { "frontend", "backend", "identity", "already-writing" })
{
    using var f = New();
    var runtime = new FakeRuntime { InitialProblem = missing };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && !runtime.Calls.Contains("retire") && !runtime.Calls.Contains("activate"), "readiness fails closed: " + missing);
}
using (var f = New())
{
    var runtime = new FakeRuntime { IncompleteRetirement = true };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && !runtime.Calls.Contains("activate"), "unconfirmed unload never activates new writer");
    Check(runtime.Calls.TakeLast(2).SequenceEqual(["stop", "restore"]), "rollback confirms candidate stop before restoring Decky");
}
using (var f = New())
{
    var runtime = new FakeRuntime { ThrowAfterRetire = true };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && runtime.Calls.Contains("restore"), "lost retirement reply triggers confirmed compensation");
}
using (var f = New())
{
    var changed = "{\"unknown\":\"new user choice during handover\",\"volume\":23}";
    var runtime = new FakeRuntime { OnActivate = () => { File.WriteAllText(f.Settings, changed); throw new IOException("activation failed"); } };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && File.ReadAllText(f.Settings) == changed, "rollback preserves preferences saved after snapshot");
    Check(result.Snapshot is not null, "rollback retains original snapshot for explicit recovery");
}
using (var f = New())
{
    var runtime = new FakeRuntime { FailFinalProbe = true, StopConfirmed = false };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "blocked" && !runtime.Calls.Contains("restore"), "unknown candidate stop cannot start a second writer");
}
using (var f = New())
{
    var runtime = new FakeRuntime { FailFinalProbe = true, RestoreConfirmed = false };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "blocked", "unconfirmed previous owner restoration is not success");
}
using (var f = New())
{
    var runtime = new FakeRuntime { HangInitialProbe = true };
    var result = await new MigrationCoordinator(f.Storage, TimeSpan.FromMilliseconds(50)).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && !runtime.Calls.Contains("retire"), "bounded readiness timeout leaves previous owner intact");
}
using (var f = New())
{
    var runtime = new FakeRuntime { OnInitialProbe = () => File.AppendAllText(f.Settings, " ") };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && !runtime.Calls.Contains("retire"), "data drift during readiness aborts before retirement");
}
using (var f = New())
{
    var coordinator = new MigrationCoordinator(f.Storage); var plan = f.Plan();
    var runtime = new FakeRuntime { OnInitialProbe = () => Rejects(
        () => coordinator.ExecuteAsync(plan, new FakeRuntime(), apply: true),
        "concurrent handover in one coordinator is rejected").GetAwaiter().GetResult() };
    var result = await coordinator.ExecuteAsync(plan, runtime, apply: true);
    Check(result.Status == "active", "rejected concurrent attempt does not stop first transaction");
}
using (var f = New())
{
    var runtime = new FakeRuntime { OnActivate = () =>
    {
        var journal = Directory.GetDirectories(Path.Combine(f.Root, ".migration-journal")).Single();
        File.WriteAllText(Path.Combine(journal, "005.json"), "fixture simulates failed append");
    } };
    var result = await new MigrationCoordinator(f.Storage).ExecuteAsync(f.Plan(), runtime, apply: true);
    Check(result.Status == "rolled-back" && runtime.Calls.TakeLast(2).SequenceEqual(["stop", "restore"]),
        "post-activation journal failure still compensates in safe order");
}
Console.WriteLine($"PASS: {count} isolated migration checks. No live adapter, Steam, Decky or autostart operations.");

sealed class Fixture : IDisposable
{
    public string Root { get; }
    public string Settings => Path.Combine(Root, "input/settings/quick-settings-2.3.json");
    public MigrationStorage Storage { get; }
    private readonly string approvedParent;
    public Fixture(string parent)
    {
        approvedParent = Path.GetFullPath(parent); Root = Path.Combine(approvedParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "input/settings")); Directory.CreateDirectory(Path.Combine(Root, "input/plugin"));
        File.WriteAllText(Settings, "{\"volume\":45,\"unknownFutureKey\":{\"retain\":true}}\r\n");
        File.WriteAllText(Path.Combine(Root, "input/settings/qam-layout.json"), "{\"version\":3,\"order\":[\"steam:home\"]}");
        File.WriteAllText(Path.Combine(Root, "input/plugin/plugin.json"), "{\"name\":\"Playhub\",\"version\":\"fixture\"}");
        Storage = new(Root);
    }
    public MigrationPlan Plan() => Storage.Plan([new("settings", "input/settings"), new("plugin", "input/plugin")], "candidate");
    public void Dispose()
    {
        var target = Path.GetFullPath(Root);
        if (Path.GetDirectoryName(target) != approvedParent || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
            throw new InvalidOperationException("Fixture cleanup target is outside the approved test directory.");
        Directory.Delete(target, recursive: true);
    }
}
sealed class FakeRuntime : IHostHandover
{
    public List<string> Calls { get; } = [];
    public bool Active, IncompleteRetirement, ThrowAfterRetire, FailFinalProbe, HangInitialProbe;
    public bool StopConfirmed = true, RestoreConfirmed = true;
    public string? InitialProblem;
    public Action? OnActivate, OnInitialProbe;
    private int probes;
    public async Task<CandidateReadiness> ProbeCandidateAsync(HandoverContext context, CancellationToken cancellationToken)
    {
        Calls.Add("probe"); probes++;
        if (probes == 1)
        {
            OnInitialProbe?.Invoke();
            if (HangInitialProbe) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        return new(InitialProblem == "identity" ? "wrong-instance" : context.CandidateInstanceId,
            InitialProblem != "frontend" && !(probes > 1 && FailFinalProbe), InitialProblem != "backend", Active,
            Active || InitialProblem == "already-writing");
    }
    public Task<PreviousOwnerStopped> RetirePreviousOwnerAsync(HandoverContext context, CancellationToken cancellationToken)
    {
        Calls.Add("retire"); if (ThrowAfterRetire) throw new IOException("lost reply after retiring");
        return Task.FromResult(new PreviousOwnerStopped(true, !IncompleteRetirement, !IncompleteRetirement));
    }
    public Task ActivateCandidateAsync(HandoverContext context, CancellationToken cancellationToken)
    { Calls.Add("activate"); OnActivate?.Invoke(); Active = true; return Task.CompletedTask; }
    public Task<CandidateStopped> StopCandidateAsync(HandoverContext context, CancellationToken cancellationToken)
    { Calls.Add("stop"); if (StopConfirmed) Active = false; return Task.FromResult(new CandidateStopped(StopConfirmed, StopConfirmed, StopConfirmed)); }
    public Task<PreviousOwnerRestored> RestorePreviousOwnerAsync(HandoverContext context, CancellationToken cancellationToken)
    { Calls.Add("restore"); return Task.FromResult(new PreviousOwnerRestored(RestoreConfirmed, RestoreConfirmed, RestoreConfirmed)); }
}
