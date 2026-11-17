using Playhub.StoreLibrary;

var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
var caps = new StoreCapabilities(StoreInstallKind.Direct,true,true,true,true,true);
StoreAccount Account(StoreKind store,long revision=1,StoreConnectionState state=StoreConnectionState.Connected) => new(store,state,revision,null,caps);
var service = new Library { Snapshot = new(7,DateTimeOffset.UtcNow,[Account(StoreKind.Epic,8),Account(StoreKind.Gog,3),Account(StoreKind.Xbox,9,StoreConnectionState.Disconnected)],[],[]) };
await using (var refresh = new StoreStartupRefresh(service))
{
    var held = new TaskCompletionSource<StoreCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    service.Refresh = async (s,r,token) => { if(s==StoreKind.Epic){entered.TrySetResult();return await held.Task.WaitAsync(token);} return new(true,null,service.Snapshot); };
    refresh.ObserveSteamStart(new(10,100));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(service.Snapshot.Revision==7,"Startup keeps cached snapshot while network query is pending");
    for (var i=0;i<10;i++) refresh.ObserveSteamStart(new(10,100));
    Check(service.Calls.Count==1,"Repeated lease registration for one Steam process is coalesced");
    held.SetResult(new(true,null,service.Snapshot)); await refresh.WhenIdleAsync();
    Check(service.Calls.SequenceEqual(new[]{(StoreKind.Epic,8L),(StoreKind.Gog,3L)}),"Refreshes connected providers using exact account revisions only");
    Check(service.MaxConcurrent==1,"Startup account requests stay serialized");
    refresh.ObserveSteamStart(new(0,100));refresh.ObserveSteamStart(new(10,0));await refresh.WhenIdleAsync();
    Check(service.Calls.Count==2,"Unknown Steam identities never refresh a store");
    refresh.ObserveSteamStart(new(10,101));await refresh.WhenIdleAsync();
    Check(service.Calls.Count==4,"Reused PID with a new process birth counts as a new Steam start");
    Check(service.AuthCalls==0&&service.InstallCalls==0,"Startup refresh never logs in or starts an installation");
}
var failing = new Library { Snapshot=service.Snapshot };
failing.Refresh=(s,r,token)=>s==StoreKind.Epic?Task.FromException<StoreCommandResult>(new IOException("offline")):Task.FromResult(new StoreCommandResult(false,"account_changed",failing.Snapshot));
await using(var refresh=new StoreStartupRefresh(failing))
{
    refresh.ObserveSteamStart(new(11,200));await refresh.WhenIdleAsync();
    Check(failing.Calls.Count==2,"An unavailable provider does not prevent the next library refresh");
    Check(failing.Snapshot.Revision==7,"Failure and account replacement cannot erase cached libraries");
}
var timeout = new Library { Snapshot=service.Snapshot };
timeout.Refresh=async(s,r,token)=>{if(s==StoreKind.Epic)await Task.Delay(Timeout.Infinite,token);return new(true,null,timeout.Snapshot);};
await using(var refresh=new StoreStartupRefresh(timeout,refreshBudget:TimeSpan.FromMilliseconds(50)))
{refresh.ObserveSteamStart(new(12,300));await refresh.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));Check(timeout.Calls.Count==2,"A stalled store query has a bounded lifetime and the next account still refreshes");}
var cancellation=new Library{Snapshot=service.Snapshot};
var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
cancellation.Refresh=async(s,r,token)=>{started.TrySetResult();await Task.Delay(Timeout.Infinite,token);return new(true,null,cancellation.Snapshot);};
var disposable=new StoreStartupRefresh(cancellation);
disposable.ObserveSteamStart(new(13,400));await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
await disposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
disposable.ObserveSteamStart(new(14,500));
Check(cancellation.Calls.Count==1,"Host shutdown cancels the pending query and schedules no new work");
var clock=DateTimeOffset.UtcNow;var notices=new List<SteamProcessIdentity>();
var authority=new SteamDownloadAuthority(()=> (true,new SteamProcessIdentity(20,600)),p=>p==30?700:null,()=>clock,onRegistered:notices.Add);
var lease=authority.Register(30);
Check(notices.SequenceEqual(new[]{new SteamProcessIdentity(20,600)})&&lease.OwnerId!=Guid.Empty,"Only a successful authenticated Steam registration supplies the startup identity");
try{authority.Register(30);throw new Exception("Busy registration accepted");}catch(StoreOperationException){checks++;}
Check(notices.Count==1,"Rejected duplicate observer registration does not refresh again");
clock=clock.AddSeconds(16);authority.Register(30);Check(notices.Count==2,"New observer lease can reuse the same Steam identity for coalescing");
var brokenObserver=new SteamDownloadAuthority(()=>(true,new SteamProcessIdentity(20,600)),_=>700,onRegistered:_=>throw new IOException("independent observer failure"));
Check(brokenObserver.Register(30).OwnerId!=Guid.Empty,"Library refresh failure never breaks Steam download-observer registration");
Console.WriteLine($"PASS {checks} actual startup refresh checks; no windows, controller, store account, host process or game operations.");

sealed class Library:IStoreLibraryService
{
 public StoreLibrarySnapshot Snapshot=new(1,DateTimeOffset.UtcNow,[],[],[]);
 public List<(StoreKind,long)> Calls=[];
 public int AuthCalls,InstallCalls,MaxConcurrent;
 private int concurrent;
 public Func<StoreKind,long,CancellationToken,Task<StoreCommandResult>>? Refresh;
 public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken ct=default)=>Task.FromResult(Snapshot);
 public async Task<StoreCommandResult> RefreshAsync(StoreKind s,long r,CancellationToken ct=default){Calls.Add((s,r));int n=Interlocked.Increment(ref concurrent);MaxConcurrent=Math.Max(MaxConcurrent,n);try{return Refresh is null?new(true,null,Snapshot):await Refresh(s,r,ct);}finally{Interlocked.Decrement(ref concurrent);}}
 public Task<StoreConnectSession> BeginConnectAsync(StoreKind s,CancellationToken ct=default){AuthCalls++;throw new NotSupportedException();}
 public Task<StoreCommandResult> CompleteConnectAsync(Guid id,string code,CancellationToken ct=default){AuthCalls++;throw new NotSupportedException();}
 public Task<StoreCommandResult> DisconnectAsync(StoreKind s,long r,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey k,long r,string? p=null,CancellationToken ct=default){InstallCalls++;throw new NotSupportedException();}
 public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest r,CancellationToken ct=default){InstallCalls++;throw new NotSupportedException();}
 public Task<StoreCommandResult> ControlAsync(StoreJobCommand c,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey k,CancellationToken ct=default)=>throw new NotSupportedException();
}
