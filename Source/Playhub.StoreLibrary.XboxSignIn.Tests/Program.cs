using Playhub.StoreLibrary;

static class Checks
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static readonly StoreCapabilities caps = new(StoreInstallKind.ExternalHandoff, true, true, false, false, false);
    static StoreLibrarySnapshot Snapshot(bool connected = true) => new(7, DateTimeOffset.UtcNow,
        [new(StoreKind.Xbox, connected ? StoreConnectionState.Connected : StoreConnectionState.Disconnected, 4, null, caps)], [], []);
    static async Task Main()
    {
        Check(StoreXboxSignInController.ValidAuthorizationUri(new("https://www.microsoft.com/link?otc=ABCD-1234")), "official prefilled sign-in URL");
        foreach (var url in new[] { "http://www.microsoft.com/link?otc=ABCD", "https://evil.example/link?otc=ABCD", "https://microsoft.com.evil.example/link?otc=ABCD", "https://user@www.microsoft.com/link?otc=ABCD", "https://www.microsoft.com/link?otc=ABCD&otc=EFGH", "https://www.microsoft.com/link?otc=ABCD#code", "https://www.microsoft.com/link?code=ABCD", "https://www.microsoft.com/link?otc=ABCD&other=1", "https://www.microsoft.com/other?otc=ABCD", "https://www.microsoft.com:8443/link?otc=ABCD", "https://www.microsoft.com/link?otc=%3Cscript%3E" })
            Check(!StoreXboxSignInController.ValidAuthorizationUri(new(url)), "foreign or ambiguous authorization URL refused");

        var f = new Fixture(); var flow = f.Controller();
        var session = await flow.BeginAsync(4, default);
        Check(session is not null && f.Revision == 4, "exact account revision sent to Begin");
        Check(await flow.BeginAsync(4, default) is null && f.Begins == 1, "one active session only");
        f.Results.Enqueue(new(f.Session.SessionId, StoreXboxSignInState.Pending, 2, Snapshot(false)));
        f.Results.Enqueue(new(f.Session.SessionId, StoreXboxSignInState.Connected, 0, Snapshot()));
        var connected = await flow.PollAsync(session!, default);
        Check(connected?.State == StoreXboxSignInState.Connected && f.Polls == 2, "actual Pending then Connected; terminal zero scheduling accepted");
        Check(f.Delays.SequenceEqual(new[] { 1d, 2d }), "poll follows server intervals without parallel calls");
        await flow.CancelAsync(); Check(f.Cancels.Count == 0, "successful terminal does not cancel account");

        f = new(); flow = f.Controller(); session = await flow.BeginAsync(4, default);
        f.Results.Enqueue(new(Guid.NewGuid(), StoreXboxSignInState.Connected, 0, Snapshot()));
        Check(await flow.PollAsync(session!, default) is null, "foreign session result cannot connect");
        await flow.CancelAsync(); await flow.CancelAsync();
        Check(f.Cancels.SequenceEqual(new[] { f.Session.SessionId }), "cancel exact session once");

        f = new(); flow = f.Controller(); session = await flow.BeginAsync(4, default);
        f.Results.Enqueue(new(f.Session.SessionId, StoreXboxSignInState.Connected, 0, Snapshot(false)));
        Check(await flow.PollAsync(session!, default) is null, "Connected without canonical Xbox account refused"); await flow.CancelAsync();

        f = new(); flow = f.Controller(); session = await flow.BeginAsync(4, default);
        var pendingPoll = new TaskCompletionSource<StoreXboxSignInResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Poll = _ => pendingPoll.Task;
        var first = flow.PollAsync(session!, default);
        Check(await flow.PollAsync(session!, default) is null && f.Polls == 1, "duplicate UI wait cannot issue another poll");
        await flow.CancelAsync(); pendingPoll.SetResult(new(f.Session.SessionId, StoreXboxSignInState.Connected, 0, Snapshot()));
        Check(await first is null && f.Cancels.Count == 1, "late connected response after close cannot update UI");

        f = new(); flow = f.Controller();
        var pendingBegin = new TaskCompletionSource<StoreXboxSignInSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Begin = () => pendingBegin.Task; var begin = flow.BeginAsync(4, default);
        await flow.CancelAsync(); pendingBegin.SetResult(f.Session);
        Check(await begin is null && f.Cancels.SequenceEqual(new[] { f.Session.SessionId }), "closing during Begin cancels its late exact session");

        f = new(); f.StepSeconds = 1000; flow = f.Controller(); session = await flow.BeginAsync(4, default);
        Check(await flow.PollAsync(session!, default) is null && f.Polls == 0, "original authorization expiry checked after wait"); await flow.CancelAsync();

        foreach (var state in new[] { StoreXboxSignInState.Denied, StoreXboxSignInState.Expired, StoreXboxSignInState.Canceled, StoreXboxSignInState.Failed })
        {
            f = new(); flow = f.Controller(); session = await flow.BeginAsync(4, default);
            f.Results.Enqueue(new(f.Session.SessionId, state, 0, Snapshot(false)));
            Check((await flow.PollAsync(session!, default))?.State == state, "terminal result is not relabeled Connected");
            await flow.CancelAsync(); Check(f.Cancels.Count == 0, "terminal session cleanup does not create another account operation");
        }
        f = new(); flow = f.Controller(); Check(await flow.BeginAsync(-1, default) is null && f.Begins == 0, "invalid account revision never starts auth");
        f = new(); f.Session = f.Session with { AuthorizationUri = new("https://evil.example/link?otc=ABCD") }; flow = f.Controller();
        Check(await flow.BeginAsync(4, default) is null && f.Cancels.Count == 1, "invalid initial URL is never opened and exact remote session canceled");
        Console.WriteLine($"PASS {count} actual Xbox sign-in UI-controller checks; no browser, OAuth, account, download or process launched");
    }
    sealed class Fixture : IStoreXboxAccountService
    {
        internal DateTimeOffset Now = new(2026, 11, 14, 17, 0, 0, TimeSpan.Zero);
        internal StoreXboxSignInSession Session = new(Guid.NewGuid(), new("https://www.microsoft.com/link?otc=ABCD-1234"), new(2026, 11, 14, 17, 15, 0, TimeSpan.Zero), 1);
        internal int Begins, Polls; internal long Revision; internal double? StepSeconds;
        internal readonly List<Guid> Cancels = []; internal readonly List<double> Delays = [];
        internal readonly Queue<StoreXboxSignInResult> Results = [];
        internal Func<Guid, Task<StoreXboxSignInResult>>? Poll;
        internal Func<Task<StoreXboxSignInSession>>? Begin;
        internal StoreXboxSignInController Controller() => new(this, () => Now, (period, token) => { token.ThrowIfCancellationRequested(); Delays.Add(period.TotalSeconds); Now = Now.AddSeconds(StepSeconds ?? period.TotalSeconds); return Task.CompletedTask; });
        public Task<StoreXboxSignInSession> BeginXboxSignInAsync(long revision, CancellationToken ct = default) { Begins++; Revision = revision; return Begin?.Invoke() ?? Task.FromResult(Session); }
        public Task<StoreXboxSignInResult> PollXboxSignInAsync(Guid id, CancellationToken ct = default) { Polls++; Check(id == Session.SessionId, "poll exact held session"); return Poll?.Invoke(id) ?? Task.FromResult(Results.Dequeue()); }
        public Task<StoreXboxSignInResult> CancelXboxSignInAsync(Guid id, CancellationToken ct = default) { Cancels.Add(id); return Task.FromResult(new StoreXboxSignInResult(id, StoreXboxSignInState.Canceled, 0, Snapshot(false))); }
        public Task<StoreXboxOwnedSnapshot> GetXboxOwnedLibraryAsync(CancellationToken ct = default) => throw new Exception("No owned-library access in sign-in tests");
        public Task<StoreXboxHandoffResult> OpenXboxOwnedInstallPageAsync(StoreXboxHandoffRequest request, CancellationToken ct = default) => throw new Exception("No official app launch in tests");
    }
}
