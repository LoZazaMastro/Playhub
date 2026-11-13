using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

internal readonly record struct OwnedProcessFacts(uint Pid, long Birth, string Image, string Sid, uint Session);
internal interface IOwnedWin32Process : IDisposable
{
    OwnedProcessFacts Identity { get; }
    bool Current { get; }
}
internal interface IOwnedSteamGeneration : IDisposable
{
    OwnedProcessFacts Identity { get; }
    string Root { get; }
    bool Current { get; }
}
internal interface IOwnedEpicProvenance : IDisposable
{
    bool Current { get; }
    bool RetainedCurrent { get; }
}
internal sealed record OwnedHandoff(uint ServerPid, Win32SteamSessionAssociation.Identity Publication);
internal sealed record OwnedLogWindow(string FileId, long Start, long End, byte[] Bytes)
{
    internal string Text => Encoding.UTF8.GetString(Bytes);
    internal string? Since(OwnedLogWindow previous) => FileId == previous.FileId && Start <= previous.End && End >= previous.End
        ? Encoding.UTF8.GetString(Bytes.AsSpan(checked((int)(previous.End - Start)))) : null;
}
internal interface IWin32OwnedRuntime
{
    long Tick { get; }
    DateTime LocalTime { get; }
    string WrapperImage { get; }
    IOwnedSteamGeneration? OpenSteam();
    IOwnedWin32Process? Open(uint pid);
    Task<OwnedLogWindow?> ReadLogAsync(string root, CancellationToken cancellation);
    Task<OwnedHandoff?> ReadHandoffAsync(uint wrapperPid, long birth, CancellationToken cancellation);
    // Captured from an actual present game surface, not an RPC supplied HWND.
    (nint Window, uint Pid)? Foreground();
    bool SteamUiForeground(IOwnedSteamGeneration generation) => false;
    IOwnedEpicProvenance? RetainEpic(Playhub.Shared.EpicLaunchProvenance provenance, uint wrapperPid, long wrapperBirth) => null;
}

internal sealed record OwnedWin32Route(uint CanonicalShortcutAppId, string OriginalGameId, string? ObservedSteamGameId,
    uint GamePid, string GameBirth, uint WrapperPid, string WrapperBirth, uint SteamPid, string SteamBirth,
    bool ObservedFresh, bool RoutingOnly = true, uint? UiObservedApp = null, string LaunchProvenance = "steam-child");

// Each request borrows the same retained launch. A cache keeps the ORIGINAL
// transaction across bounded-log rollover, not a PID lookup or 480 mapping.
internal sealed partial class Win32OwnedAssociationCatalog(IWin32OwnedRuntime runtime) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly List<Entry> entries = new();
    private IOwnedSteamGeneration? steam;
    private volatile bool disposed;
    private const int MaxCandidates = 32, MaxEntries = 16, ProofMs = 1500;
    internal sealed class Lease : IDisposable
    {
        private Entry? entry;
        private readonly long epoch;
        internal Lease(Entry value) { entry = value; epoch = value.Epoch; value.AddRef(); }
        internal Win32SteamSessionAssociation.Identity Identity => entry?.Identity ?? throw new ObjectDisposedException(nameof(Lease));
        internal bool Current { get { var e = entry; return e is not null && e.Epoch == epoch && e.Current && e.Epoch == epoch && ReferenceEquals(entry, e); } }
        internal OwnedWin32Route? Route { get { var e = entry; var route = Current ? e?.ReadRoute() : null; return Current ? route : null; } }
        public void Dispose() { Interlocked.Exchange(ref entry, null)?.Release(); }
    }
    internal sealed class Entry(IOwnedWin32Process wrapper, IOwnedWin32Process game, IOwnedSteamGeneration generation,
        Win32SteamSessionAssociation.Identity identity, IWin32OwnedRuntime runtime, OwnedLogWindow log, ulong? observed, IOwnedEpicProvenance? epic = null)
    {
        internal Win32SteamSessionAssociation.Identity Identity { get; } = identity;
        private readonly OwnedProcessFacts w = wrapper.Identity, g = game.Identity, s = generation.Identity;
        private int references = 1;
        private volatile bool retired;
        private long deadline, epoch;
        private OwnedLogWindow last = log;
        private ulong? currentId = observed;
        private bool fresh = observed is not null;
        private DateTime? removedAt;
        internal void AddRef() => Interlocked.Increment(ref references);
        internal void Release() { if (Interlocked.Decrement(ref references) == 0) { epic?.Dispose(); game.Dispose(); wrapper.Dispose(); } }
        internal long Epoch => Interlocked.Read(ref epoch);
        private void InvalidateProof() { Interlocked.Exchange(ref deadline, 0); Interlocked.Increment(ref epoch); }
        internal void Retire() { retired = true; InvalidateProof(); }
        internal bool RetainedCurrent => !retired && wrapper.Identity == w && game.Identity == g
            && wrapper.Current && game.Current && generation.Current && generation.Identity == s && (epic?.RetainedCurrent ?? true) && !retired;
        internal bool Current
        {
            get
            {
                long stamp = Interlocked.Read(ref epoch), until = Interlocked.Read(ref deadline);
                return runtime.Tick < until && RetainedCurrent && (currentId is not null || epic is not null)
                    && runtime.Tick < until && Interlocked.Read(ref deadline) >= until && !retired && Interlocked.Read(ref epoch) == stamp;
            }
        }
        internal async Task<bool> Refresh(OwnedLogWindow log, CancellationToken cancellation)
        {
            long stamp = Interlocked.Read(ref epoch);
            if (!RetainedCurrent) return false;
            OwnedHandoff? handoff;
            try { handoff = await runtime.ReadHandoffAsync(w.Pid, w.Birth, cancellation).ConfigureAwait(false); }
            catch { InvalidateProof(); throw; }
            if (handoff is null) { InvalidateProof(); return false; }
            if (handoff.ServerPid != w.Pid || handoff.Publication != Identity || !RetainedCurrent || epic?.Current == false || retired
                || stamp != Interlocked.Read(ref epoch)) { Retire(); return false; }
            string? delta = log.Since(last);
            if (delta is not null)
            {
                if (delta.Contains("Client version:", StringComparison.Ordinal) || Removed(delta, w.Pid)) { Retire(); return false; }
                foreach (var evt in Events(delta, g.Pid))
                {
                    if (!Known(evt.Id) || evt.Time > runtime.LocalTime) { Retire(); return false; }
                    if (evt.Added)
                    {
                        if (epic is not null && currentId is null && removedAt is null)
                        {
                            // A later real Steam event may be observed; provenance never manufactures one.
                            if (evt.Time < DateTime.FromFileTimeUtc(g.Birth).ToLocalTime().AddSeconds(-2)) { Retire(); return false; }
                            currentId = evt.Id;
                        }
                        else
                        {
                            if (currentId is not null || removedAt is not DateTime at || evt.Time < at || evt.Time - at > TimeSpan.FromSeconds(2)) { Retire(); return false; }
                            currentId = evt.Id; removedAt = null;
                        }
                    }
                    else
                    {
                        if (currentId != evt.Id) { Retire(); return false; }
                        InvalidateProof(); stamp = Epoch; currentId = null; removedAt = evt.Time;
                    }
                }
                fresh = currentId is not null;
            }
            else
            {
                // Missing history never creates a new identity. The original
                // retained handles + a FRESH private handoff keep this launch.
                fresh = false;
            }
            last = log;
            if (!RetainedCurrent || stamp != Interlocked.Read(ref epoch)) return false;
            Interlocked.Exchange(ref deadline, checked(runtime.Tick + ProofMs));
            return Current;
        }
        internal bool ConfirmCapture() { if (!RetainedCurrent) return false; Interlocked.Exchange(ref deadline, checked(runtime.Tick + ProofMs)); return Current; }
        internal OwnedWin32Route? ReadRoute()
        {
            long stamp = Interlocked.Read(ref epoch);
            if (!Current) return null;
            var result = new OwnedWin32Route((uint)(Identity.GameId >> 32), Dec(Identity.GameId), currentId is ulong id ? Dec(id) : null,
                g.Pid, Dec(g.Birth), w.Pid, Dec(w.Birth), s.Pid, Dec(s.Birth), fresh, LaunchProvenance: epic is null ? "steam-child" : "epic-protocol");
            return Current && Interlocked.Read(ref epoch) == stamp ? result : null;
        }
    }
    internal async Task<Lease?> FindAsync(uint app, CancellationToken cancellation)
    {
        if (app < 0x80000000) return null;
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellation); bound.CancelAfter(900);
        await gate.WaitAsync(bound.Token).ConfigureAwait(false);
        try { return await FindCore(app, bound.Token).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or OverflowException or System.Security.SecurityException) { return null; }
        finally { if (disposed) Clear(); gate.Release(); }
    }
    private async Task<Lease?> FindCore(uint app, CancellationToken cancellation)
    {
        if (disposed) return null;
        if (steam?.Current != true)
        {
            Clear(); steam = runtime.OpenSteam();
            if (steam?.Current != true) return null;
        }
        foreach (var dead in entries.Where(e => !e.RetainedCurrent).ToArray()) { entries.Remove(dead); dead.Retire(); dead.Release(); }
        var log = await runtime.ReadLogAsync(steam.Root, cancellation).ConfigureAwait(false);
        if (log is null || log.Bytes.Length > 262144 || !steam.Current || disposed) return null;
        var valid = new List<Entry>();
        foreach (var e in entries.Where(e => (uint)(e.Identity.GameId >> 32) == app))
            if (await e.Refresh(log, cancellation).ConfigureAwait(false)) valid.Add(e);
        var tracked = Win32SteamSessionAssociation.Parse(log.Text, app);
        if (tracked.Count > MaxCandidates) return null;
        var added = new List<Entry>();
        try
        {
            foreach (var t in tracked)
            {
                cancellation.ThrowIfCancellationRequested();
                if (entries.Any(e => e.Identity.WrapperPid == t.Pid)) continue;
                var candidate = await Capture(t, log, cancellation).ConfigureAwait(false);
                if (candidate is not null) { added.Add(candidate); valid.Add(candidate); }
            }
            if (valid.Count > 1)
            {
                foreach (var e in valid) { e.Retire(); if (entries.Remove(e)) e.Release(); }
                return null;
            }
            if (disposed || steam.Current != true || valid.Count != 1 || entries.Count + added.Count > MaxEntries) return null;
            foreach (var e in added) entries.Add(e);
            added.Clear();
            return valid[0].Current ? new Lease(valid[0]) : null;
        }
        finally { foreach (var e in added) { e.Retire(); e.Release(); } }
    }
    private async Task<Entry?> Capture(Win32SteamSessionAssociation.Tracked tracked, OwnedLogWindow log, CancellationToken cancellation)
    {
        IOwnedWin32Process? wrapper = null, game = null;
        IOwnedEpicProvenance? epic = null;
        try
        {
            wrapper = runtime.Open(tracked.Pid);
            if (steam is null || wrapper is null || !wrapper.Current || !SameOwner(wrapper.Identity, steam.Identity)
                || !wrapper.Identity.Image.Equals(runtime.WrapperImage, StringComparison.OrdinalIgnoreCase)) return null;
            var w = wrapper.Identity;
            var handoff = await runtime.ReadHandoffAsync(w.Pid, w.Birth, cancellation).ConfigureAwait(false);
            if (handoff?.Publication is not { } identity || handoff.ServerPid != w.Pid) return null;
            game = runtime.Open(identity.GamePid);
            if (game is null || !game.Current || !SameOwner(game.Identity, w) || game.Identity.Pid == w.Pid
                || !Win32SteamSessionAssociation.Matches(identity, tracked, handoff.ServerPid, w.Birth,
                    game.Identity.Pid, game.Identity.Birth, game.Identity.Image)) return null;
            var id = InitialIdentity(log.Text, identity.GamePid, identity.GameId, identity.GameBirth, runtime.LocalTime);
            if (identity.Epic is { } proof)
            {
                if (proof.WrapperPid != w.Pid || proof.WrapperBirth != w.Birth || proof.GamePid != identity.GamePid
                    || proof.GameBirth != identity.GameBirth || !proof.GameExecutable.Equals(identity.Executable, StringComparison.OrdinalIgnoreCase)) return null;
                epic = runtime.RetainEpic(proof, w.Pid, w.Birth);
                if (epic?.Current != true) return null;
            }
            if ((id is null && epic is null) || !wrapper.Current || !game.Current || !steam.Current || disposed) return null;
            var e = new Entry(wrapper, game, steam, identity, runtime, log, id, epic);
            wrapper = game = null; epic = null;
            if (e.ConfirmCapture()) return e;
            e.Retire(); e.Release(); return null;
        }
        finally { epic?.Dispose(); game?.Dispose(); wrapper?.Dispose(); }
    }
    internal Task<OwnedWin32Route?> DiscoverAsync(uint observedApp, CancellationToken cancellation)
        => DiscoverUiAsync(observedApp, null, cancellation);
    internal async Task<OwnedWin32Route?> DiscoverUiAsync(uint? uiObservedApp, uint? candidateApp, CancellationToken cancellation)
    {
        // observedApp is a consistency hint, NEVER authority or a 480 selector.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation); bounded.CancelAfter(900);
        cancellation = bounded.Token;
        if (disposed || candidateApp is < 0x80000000 || ((uiObservedApp is null or 0) && candidateApp is null) || runtime.Foreground() is not { } fg) return null;
        using var game = runtime.Open(fg.Pid);
        if (game is null || !game.Current) return null;
        OwnedWin32Route? found = null;
        // Existing captured sessions survive log rollover and plugin reload.
        uint[] cached;
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try { cached = entries.Where(e => e.Identity.GamePid == fg.Pid && e.Identity.GameBirth == game.Identity.Birth)
            .Select(e => (uint)(e.Identity.GameId >> 32)).Distinct().Take(2).ToArray(); }
        finally { gate.Release(); }
        var root = steam?.Root;
        if (root is null) { using var initialSteam = runtime.OpenSteam(); root = initialSteam?.Root; }
        if (root is null) return null;
        var log = await runtime.ReadLogAsync(root, cancellation).ConfigureAwait(false);
        if (log is null || log.Bytes.Length > 262144) return null;
        var apps = cached.Concat(Originals(log.Text, fg.Pid, game.Identity.Birth))
            .Concat(candidateApp is uint hinted ? new[] { hinted } : Array.Empty<uint>()).Distinct().Take(3).ToArray();
        if (apps.Length == 0)
        {
            // Only tracked wrappers are candidates; FindAsync authenticates each private handoff and the exact foreground game.
            apps = TrackedWrapperApps(log.Text).Distinct().Take(MaxCandidates + 1).ToArray();
            if (apps.Length > MaxCandidates) return null;
        }
        if (candidateApp is uint expected) apps = apps.Where(a => a == expected).ToArray();
        if (apps.Length == 0 || (candidateApp is not null && apps.Length > 2)) return null;
        foreach (uint app in apps)
        {
            using var lease = await FindAsync(app, cancellation).ConfigureAwait(false);
            var route = lease?.Route;
            if (route is null || route.GamePid != fg.Pid || route.GameBirth != Dec(game.Identity.Birth)) continue;
            if (ulong.TryParse(route.ObservedSteamGameId, out ulong observed))
            {
                if (uiObservedApp is uint ui && ui != 0 && (observed <= uint.MaxValue ? observed : observed >> 32) != ui) continue;
            }
            else if (lease!.Identity.Epic is null || (uiObservedApp is uint raw && raw != 0 && raw != route.CanonicalShortcutAppId)) continue;
            if (found is not null) return null;
            found = route;
        }
        return game.Current && runtime.Foreground() == fg && found is not null ? found with { UiObservedApp = uiObservedApp } : null;
    }
    private static IEnumerable<uint> TrackedWrapperApps(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            var row = Row.Match(line);
            if (row.Success && Add.IsMatch(row.Groups["rest"].Value) && ulong.TryParse(row.Groups["id"].Value, out ulong id) && Original(id))
                yield return (uint)(id >> 32);
        }
    }
    private void Clear() { foreach (var e in entries) { e.Retire(); e.Release(); } entries.Clear(); steam?.Dispose(); steam = null; }
    public void Dispose() { disposed = true; if (!gate.Wait(0)) return; try { Clear(); } finally { gate.Release(); } }
    private static bool SameOwner(OwnedProcessFacts a, OwnedProcessFacts b) => a.Sid == b.Sid && a.Session == b.Session && a.Birth >= b.Birth;
    private static string Dec(ulong x) => x.ToString(CultureInfo.InvariantCulture);
    private static string Dec(long x) => x.ToString(CultureInfo.InvariantCulture);
    private static readonly Regex Row = new(@"^\[(?<time>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\] (?:AppID|Game) (?<id>\d+) (?<rest>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Add = new(@"^adding PID (?<pid>\d+) as a tracked process", RegexOptions.CultureInvariant);
    private static readonly Regex Remove = new(@"(?:^|; )no longer tracking PID (?<pid>\d+)", RegexOptions.CultureInvariant);
    private readonly record struct Event(ulong Id, bool Added, DateTime Time);
    private static IEnumerable<Event> Events(string text, uint pid)
    {
        foreach (string line in text.Split('\n'))
        {
            var r = Row.Match(line.TrimEnd('\r')); if (!r.Success) continue;
            var a = Add.Match(r.Groups["rest"].Value); var d = Remove.Match(r.Groups["rest"].Value); var p = a.Success ? a : d;
            if (p.Success && uint.TryParse(p.Groups["pid"].Value, out uint actual) && actual == pid
                && ulong.TryParse(r.Groups["id"].Value, out ulong id) && id > 0
                && DateTime.TryParseExact(r.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                yield return new(id, a.Success, time);
        }
    }
    private static bool Removed(string text, uint pid) => Events(text, pid).Any(e => !e.Added);
    private static bool Known(ulong id) => id > 0 && (id <= uint.MaxValue || Original(id));
    private static bool Original(ulong id) => (uint)id == 0x02000000 && id >> 32 >= 0x80000000 && id >> 32 <= uint.MaxValue;
    private static IEnumerable<uint> Originals(string text, uint pid, long birth) => Events(text, pid)
        .Where(e => e.Added && Original(e.Id) && Math.Abs((e.Time - DateTime.FromFileTimeUtc(birth).ToLocalTime()).TotalSeconds) <= 2)
        .Select(e => (uint)(e.Id >> 32));
    private static ulong? InitialIdentity(string text, uint pid, ulong original, long birth, DateTime observed)
    {
        if (!Original(original)) return null;
        var earliest = DateTime.FromFileTimeUtc(birth).ToLocalTime().AddSeconds(-2);
        bool begun = false; ulong? active = null; DateTime? removed = null, last = null;
        foreach (var e in Events(text, pid))
        {
            if (e.Time > observed) return null;
            if (!begun) {
                if (e.Time < earliest) continue;
                if (!e.Added || !Known(e.Id)) return null;
                // Capture has already authenticated the tracked wrapper and
                // its private handoff to this exact retained game. Steam can
                // first track that game under its own application identity.
                // Preserve the real event separately from the shortcut ID.
                begun = true; active = e.Id; last = e.Time; continue;
            }
            if (!Known(e.Id) || e.Time < last) return null; last = e.Time;
            if (!e.Added) { if (active != e.Id) return null; active = null; removed = e.Time; }
            else { if (active is not null || removed is null || e.Time - removed > TimeSpan.FromSeconds(2)) return null; active = e.Id; }
        }
        return begun ? active : null;
    }
}
