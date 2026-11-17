using Playhub.StoreLibrary;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    private static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Main()
    {
        await BootstrapAndIntent(); await IndependentAndSingleFlight(); await VerificationAndUnavailable();
        await CancellationRevokesController(); await CancellationDuringBootstrap(); await DisposalRevokesEveryStore();
        await ParentCommandCannotBlockAccounts(); await OlderReadCannotConfirmAuthentication(); await ConcurrentClicks(); await BackgroundHydrationCannotBlockSignOut();
        Console.WriteLine($"PASS {checks} independent account/controller checks; fake service only, no authentication, browsers, logout or game files used");
    }
    private static async Task BootstrapAndIntent()
    {
        var service=new FakeService();int reads=0,actions=0;
        service.Read=ct=>++reads<=2?Task.FromException<StoreLibrarySnapshot>(new IOException("host starting")):Task.FromResult(service.Snapshot());
        using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);
        var epic=accounts.ForStore(StoreKind.Epic);
        Check(await epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>{actions++;service.Set(StoreKind.Epic,StoreConnectionState.Connected);return Task.CompletedTask;})==StoreAccountActionResult.Completed,"bootstrap retries real reads and verifies successful sign in");
        Check(reads>=4&&actions==1&&epic.Account?.State==StoreConnectionState.Connected,"no synthetic connection during transient bootstrap");
        Check(await epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>{actions++;return Task.CompletedTask;})==StoreAccountActionResult.AlreadyInState&&actions==1,"fresh connected read never flips SignIn into SignOut");
        service.Set(StoreKind.Epic,StoreConnectionState.Disconnected);
        Check(await epic.RunAsync(StoreAccountIntent.SignOut,(_,_)=>{actions++;return Task.CompletedTask;})==StoreAccountActionResult.AlreadyInState&&actions==1,"fresh disconnected read never flips SignOut into SignIn");
        service.Set(StoreKind.Epic,StoreConnectionState.Expired);
        Check(await epic.RunAsync(StoreAccountIntent.SignOut,async(c,_)=>{actions++;await c.DisconnectAsync(StoreKind.Epic);})==StoreAccountActionResult.Completed,"expired credentials may still be explicitly signed out");
        Check(service.Disconnects==1&&epic.Account?.State==StoreConnectionState.Disconnected,"sign out uses the same real service and authoritative state");
    }
    private static async Task IndependentAndSingleFlight()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);
        var entered=Signal();var release=Signal();int epicActions=0,oppositeActions=0;int notifications=0;accounts.Changed+=(_,_)=>notifications++;
        var epic=accounts.ForStore(StoreKind.Epic);
        var first=epic.RunAsync(StoreAccountIntent.SignIn,async(_,ct)=>{Interlocked.Increment(ref epicActions);entered.SetResult();await release.Task.WaitAsync(ct);service.Set(StoreKind.Epic,StoreConnectionState.Connected);});
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicate=epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>throw new Exception("duplicate must not run"));
        Check(await epic.RunAsync(StoreAccountIntent.SignOut,(_,_)=>{oppositeActions++;return Task.CompletedTask;})==StoreAccountActionResult.Unavailable,"opposite intent never inherits the active sign-in result");
        Check(epic.Busy&&!parent.Busy,"pending account authentication does not occupy the parent controller");
        foreach(var kind in new[]{StoreKind.Gog,StoreKind.Xbox,StoreKind.Amazon})
        {
            var account=accounts.ForStore(kind);
            Check(!ReferenceEquals(account.Controller,epic.Controller)&&!ReferenceEquals(account.Controller,parent),"one independent controller per account");
            Check(await account.RunAsync(StoreAccountIntent.SignIn,(_,_)=>{service.Set(kind,StoreConnectionState.Connected);return Task.CompletedTask;}).WaitAsync(TimeSpan.FromSeconds(2))==StoreAccountActionResult.Completed,"another store signs in while Epic browser remains pending");
        }
        Check(epic.Busy&&epicActions==1&&oppositeActions==0&&notifications>0,"single flight and aggregated change events");
        release.SetResult();Check(await first==StoreAccountActionResult.Completed&&await duplicate==StoreAccountActionResult.Completed&&epicActions==1,"duplicate same-direction calls share one real action");
        Check(!epic.Busy&&!epic.Loading,"account busy/load flags settle");
    }
    private static async Task VerificationAndUnavailable()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);var epic=accounts.ForStore(StoreKind.Epic);int actions=0;
        Check(await epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>Task.CompletedTask)==StoreAccountActionResult.Failed,"a completed callback is not proof of an authenticated account");
        foreach(var state in new[]{StoreConnectionState.Connecting,StoreConnectionState.Unavailable})
        {
            service.Set(StoreKind.Epic,state);
            Check(await epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>{actions++;return Task.CompletedTask;})==StoreAccountActionResult.Unavailable,"unavailable/connecting state cannot start another authentication");
            Check(await epic.RunAsync(StoreAccountIntent.SignOut,(_,_)=>{actions++;return Task.CompletedTask;})==StoreAccountActionResult.Unavailable,"unavailable/connecting is not an authoritative disconnected state");
        }
        Check(actions==0,"unavailable callbacks never execute");
        service.Set(StoreKind.Epic,StoreConnectionState.Connected);
        Check(await epic.RunAsync(StoreAccountIntent.SignOut,(_,_)=>{service.Set(StoreKind.Epic,StoreConnectionState.Expired);return Task.CompletedTask;})==StoreAccountActionResult.Failed,"expired is not successful sign out");
        service.Set(StoreKind.Epic,StoreConnectionState.Disconnected);
        Check(await epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>throw new IOException("provider error"))==StoreAccountActionResult.Failed&&!epic.Busy,"callback error releases only this account slot");
    }
    private static async Task CancellationRevokesController()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);var epic=accounts.ForStore(StoreKind.Epic);
        using var cancel=new CancellationTokenSource();var entered=Signal();var pending=new TaskCompletionSource<StoreConnectSession>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken providerToken=default;
        service.Connect=(kind,ct)=>{providerToken=ct;entered.SetResult();return pending.Task;};
        var old=epic.Controller;var operation=epic.RunAsync(StoreAccountIntent.SignIn,async(c,_)=>{var session=await c.BeginConnectAsync(StoreKind.Epic);if(session is not null)await c.CompleteConnectAsync(session,"inert-fake-code");},cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();
        Check(await operation.WaitAsync(TimeSpan.FromSeconds(2))==StoreAccountActionResult.Canceled,"UI cancellation completes even when provider ignores cancellation");
        Check(providerToken.IsCancellationRequested&&!ReferenceEquals(epic.Controller,old)&&!epic.Busy,"cancellation revokes the controller token and supplies a fresh retry controller");
        pending.SetResult(FakeService.Session(StoreKind.Epic));await Task.Delay(30);
        Check(service.Completes==0&&epic.Account is null,"late auth response cannot complete on the revoked controller");
        service.Connect=null;
        Check(await epic.RunAsync(StoreAccountIntent.SignIn,async(c,_)=>{var session=await c.BeginConnectAsync(StoreKind.Epic);Check(session is not null,"retry gets a new legitimate fake session");await c.CompleteConnectAsync(session!,"inert-fake-code");})==StoreAccountActionResult.Completed,"same account can retry immediately after cancellation");
    }
    private static async Task CancellationDuringBootstrap()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);var epic=accounts.ForStore(StoreKind.Epic);
        var readStarted=Signal();var delayed=new TaskCompletionSource<StoreLibrarySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken readToken=default;int actions=0;
        service.Read=ct=>{readToken=ct;readStarted.TrySetResult();return delayed.Task;};
        using var cancel=new CancellationTokenSource();var operation=epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>{actions++;return Task.CompletedTask;},cancel.Token);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();
        Check(await operation.WaitAsync(TimeSpan.FromSeconds(2))==StoreAccountActionResult.Canceled&&readToken.IsCancellationRequested&&actions==0,"cancel during bootstrap revokes the actual service read and never invokes authentication");
        service.Read=null;Check(await epic.EnsureLoadedAsync().WaitAsync(TimeSpan.FromSeconds(2)),"fresh controller can load while the superseded read remains pending");
        var fresh=epic.Controller;delayed.SetResult(service.Snapshot());await Task.Delay(30);
        Check(ReferenceEquals(epic.Controller,fresh)&&epic.Account?.State==StoreConnectionState.Disconnected,"late bootstrap cannot replace current account state");
    }
    private static async Task DisposalRevokesEveryStore()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);var accounts=new StoreAccountControllers(parent);int changes=0;accounts.Changed+=(_,_)=>changes++;
        var tokens=new List<CancellationToken>();var entered=Signal();var never=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int started=0;
        var operations=Enum.GetValues<StoreKind>().Select(kind=>accounts.ForStore(kind).RunAsync(StoreAccountIntent.SignIn,(_,ct)=>{tokens.Add(ct);if(++started==4)entered.TrySetResult();return never.Task;})).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));accounts.Dispose();int disposedChanges=changes;
        Check((await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(2))).All(result=>result==StoreAccountActionResult.Canceled),"disposing the coordinator revokes every pending account action");
        Check(tokens.All(token=>token.IsCancellationRequested)&&changes==disposedChanges,"no UI notifications or live account cancellation tokens after disposal");
        Check(await accounts.ForStore(StoreKind.Epic).RunAsync(StoreAccountIntent.SignIn,(_,_)=>throw new Exception())==StoreAccountActionResult.Canceled,"disposed coordinator cannot restart account work");
    }
    private static async Task ParentCommandCannotBlockAccounts()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);await parent.ReadAsync();
        var pending=new TaskCompletionSource<StoreConnectSession>(TaskCreationOptions.RunContinuationsAsynchronously);service.Connect=(_,_)=>pending.Task;
        var sourceAction=parent.BeginConnectAsync(StoreKind.Epic);Check(parent.Busy,"source controller has its own pending command");
        using var accounts=new StoreAccountControllers(parent);
        Check(await accounts.ForStore(StoreKind.Gog).RunAsync(StoreAccountIntent.SignIn,(_,_)=>{service.Set(StoreKind.Gog,StoreConnectionState.Connected);return Task.CompletedTask;}).WaitAsync(TimeSpan.FromSeconds(2))==StoreAccountActionResult.Completed,"a command on the original controller cannot gray out independent account actions");
        Check(parent.Busy,"independent action did not cancel the original command");pending.SetResult(FakeService.Session(StoreKind.Epic));await sourceAction;
    }
    private static async Task OlderReadCannotConfirmAuthentication()
    {
        var service=new FakeService();var stale=service.Snapshot();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);var epic=accounts.ForStore(StoreKind.Epic);
        using var cancel=new CancellationTokenSource();var verifying=Signal();int reads=0;
        service.Read=_=>{if(++reads==1)return Task.FromResult(stale);verifying.TrySetResult();return Task.FromResult(stale);};
        var operation=epic.RunAsync(StoreAccountIntent.SignIn,async(c,_)=>{var session=await c.BeginConnectAsync(StoreKind.Epic);await c.CompleteConnectAsync(session!,"inert-fake-code");},cancel.Token);
        await verifying.Task.WaitAsync(TimeSpan.FromSeconds(2));await Task.Delay(190);
        Check(!operation.IsCompleted&&epic.Busy,"an older valid read cannot confirm the retained successful command snapshot");
        cancel.Cancel();Check(await operation.WaitAsync(TimeSpan.FromSeconds(2))==StoreAccountActionResult.Canceled,"stale verification remains cancelable without inventing completion");
    }
    private static async Task ConcurrentClicks()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);var epic=accounts.ForStore(StoreKind.Epic);var release=Signal();var entered=Signal();int actions=0;
        var results=Enumerable.Range(0,20).Select(_=>Task.Run(()=>epic.RunAsync(StoreAccountIntent.SignIn,async(_,ct)=>{Interlocked.Increment(ref actions);entered.TrySetResult();await release.Task.WaitAsync(ct);service.Set(StoreKind.Epic,StoreConnectionState.Connected);}))).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));await Task.Delay(30);release.SetResult();
        Check((await Task.WhenAll(results).WaitAsync(TimeSpan.FromSeconds(2))).All(result=>result is StoreAccountActionResult.Completed or StoreAccountActionResult.AlreadyInState)&&actions==1,"concurrent clicks share a single operation rather than racing multiple account controllers");
    }
    private static async Task BackgroundHydrationCannotBlockSignOut()
    {
        var service=new FakeService();using var parent=new StoreLibraryController(service);using var accounts=new StoreAccountControllers(parent);var epic=accounts.ForStore(StoreKind.Epic);
        Check(await epic.RunAsync(StoreAccountIntent.SignIn,(_,_)=>{service.Set(StoreKind.Epic,StoreConnectionState.Connected);return Task.CompletedTask;})==StoreAccountActionResult.Completed,"sign-in completion precedes library hydration");
        using var hydration=parent.CreateIndependent();await hydration.ReadAsync();var pending=new TaskCompletionSource<StoreCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);var captured=service.Snapshot();service.Refresh=(_,_,_)=>pending.Task;
        var refreshing=hydration.RefreshAsync(StoreKind.Epic);Check(hydration.IsRefreshing(StoreKind.Epic)&&!epic.Busy,"background refresh occupies its own controller");
        Check(await epic.RunAsync(StoreAccountIntent.SignOut,async(c,_)=>{await c.DisconnectAsync(StoreKind.Epic);}).WaitAsync(TimeSpan.FromSeconds(2))==StoreAccountActionResult.Completed,"sign out completes while same-store library hydration remains pending");
        pending.SetResult(new(true,null,captured));await refreshing;
        Check(epic.Account?.State==StoreConnectionState.Disconnected,"a late connected library refresh cannot resurrect the independently signed-out account");
    }
}

internal sealed class FakeService : IStoreLibraryService
{
    private readonly object gate=new();private long revision=1;
    private readonly Dictionary<StoreKind,StoreAccount> accounts=Enum.GetValues<StoreKind>().ToDictionary(kind=>kind,kind=>new StoreAccount(kind,StoreConnectionState.Disconnected,1,null,new(StoreInstallKind.Direct,true,true,false,false,false)));
    internal Func<CancellationToken,Task<StoreLibrarySnapshot>>? Read;
    internal Func<StoreKind,CancellationToken,Task<StoreConnectSession>>? Connect;
    internal Func<StoreKind,long,CancellationToken,Task<StoreCommandResult>>? Refresh;
    internal int Disconnects,Completes;
    internal void Set(StoreKind kind,StoreConnectionState state){lock(gate){var prior=accounts[kind];accounts[kind]=prior with{State=state,Revision=++revision};}}
    internal StoreLibrarySnapshot Snapshot(){lock(gate)return new(revision,DateTimeOffset.UtcNow,accounts.Values.ToArray(),[],[]);}
    internal static StoreConnectSession Session(StoreKind kind)=>new(Guid.NewGuid(),kind,new Uri("https://www.epicgames.com/id/login"),DateTimeOffset.UtcNow.AddMinutes(5),true);
    public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken ct=default)=>Read?.Invoke(ct)??Task.FromResult(Snapshot());
    public Task<StoreConnectSession> BeginConnectAsync(StoreKind kind,CancellationToken ct=default)=>Connect?.Invoke(kind,ct)??Task.FromResult(Session(kind));
    public Task<StoreCommandResult> CompleteConnectAsync(Guid session,string code,CancellationToken ct=default){Completes++;Set(StoreKind.Epic,StoreConnectionState.Connected);return Task.FromResult(new StoreCommandResult(true,null,Snapshot()));}
    public Task<StoreCommandResult> DisconnectAsync(StoreKind kind,long expected,CancellationToken ct=default){Disconnects++;Set(kind,StoreConnectionState.Disconnected);return Task.FromResult(new StoreCommandResult(true,null,Snapshot()));}
    public Task<StoreCommandResult> RefreshAsync(StoreKind kind,long expected,CancellationToken ct=default)=>Refresh?.Invoke(kind,expected,ct)??throw new NotSupportedException();
    public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey key,long expected,string? destination=null,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<StoreCommandResult> ControlAsync(StoreJobCommand command,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key,CancellationToken ct=default)=>throw new NotSupportedException();
}
