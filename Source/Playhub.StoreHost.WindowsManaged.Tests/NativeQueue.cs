using System.Collections.Concurrent;
using Playhub.StoreLibrary;
static partial class Checks
{
    const string Second="9AAA00000002", Third="9AAA00000003";
    static string QueueFamily(string id)=>id==Product?Family:$"Publisher.Game{id[^1]}_abcdefghijklm";
    static async Task PrepareQueue(Fixture f, QueueRuntime runtime)
    {
        f.Products=[Product,Second,Third];f.Provider.UseWindowsStoreManagedInstalls=true;
        f.Engine.WindowsManagedRuntime=runtime;f.Engine.WindowsManagedDelay=(_,ct)=>Task.Delay(5,ct);
        await f.Engine.InitializeAsync();var session=await f.Provider.BeginXboxSignInAsync(1);await f.Provider.PollXboxSignInAsync(session.SessionId);
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        Require((await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision)).Accepted,"queue owned catalog loaded");
    }
    static async Task EnqueueNative(Fixture f,string id)
    {var owned=await f.Provider.GetXboxOwnedLibraryAsync();Require((await f.Provider.OpenXboxOwnedInstallPageAsync(new(id,owned.Revision,Guid.NewGuid(),Volume))).Accepted,"native request accepted durably");}
    static async Task NativeQueue()
    {
        var direct=new QueueDirectProvider();await using var f=new Fixture(other:direct,files:new QueueFiles());var runtime=new QueueRuntime();await PrepareQueue(f,runtime);
        await EnqueueNative(f,Product);await Wait(()=>Task.FromResult(runtime.Starts.Count==1));await EnqueueNative(f,Second);await EnqueueNative(f,Third);
        var jobs=(await f.Engine.GetSnapshotAsync()).Jobs;
        Require(jobs.Count==3&&jobs.Count(j=>j.State==StoreJobState.Queued)==2&&runtime.Starts.Count==1,"one native worker and two genuine queued requests");
        var second=jobs.Single(j=>j.Key.Id==Second);Require(second.CanPause&&second.CanCancel,"queued native request has pause and cancel");
        Require((await f.Engine.ControlAsync(new(second.JobId,second.Revision,StoreJobAction.Pause,Guid.NewGuid()))).Accepted,"pause queued native without touching OS worker");
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Gog);await f.Engine.RefreshAsync(StoreKind.Gog,account.Revision);
        await f.Engine.ObserveSteamAsync(new(Guid.NewGuid(),1,f.Now,false),true);
        // The real prepare/enqueue producer accepts another store while Xbox owns its native slot.
        string path=Path.Combine(f.Folder,"direct");var preparation=await f.Engine.PrepareInstallAsync(new(StoreKind.Gog,"direct"),account.Revision,path);
        Require((await f.Engine.EnqueueAsync(new(preparation.Key,account.Revision,path,Guid.NewGuid(),preparation.SelectionNonce!.Value,preparation.Revision))).Accepted,"mixed direct request accepted during native installation");
        await Wait(()=>Task.FromResult(direct.Starts==1));Require((await f.Engine.GetSnapshotAsync()).Jobs.Count==4,"mixed stores retained in one persistent queue");
        runtime.Finish(Product);await Wait(()=>Task.FromResult(runtime.Starts.Contains(Third)));
        Require(!runtime.Starts.Contains(Second)&&runtime.Peak==1,"paused request skipped and native slot drains before next start");
        second=(await f.Engine.GetSnapshotAsync()).Jobs.Single(j=>j.Key.Id==Second);
        Require((await f.Engine.ControlAsync(new(second.JobId,second.Revision,StoreJobAction.Resume,Guid.NewGuid()))).Accepted,"queued native resumes");
        Require(!runtime.Starts.Contains(Second),"resume does not overlap active native worker");runtime.Finish(Third);await Wait(()=>Task.FromResult(runtime.Starts.Contains(Second)));
        Require(runtime.Peak==1,"all native queue execution stays serial");
        second=(await f.Engine.GetSnapshotAsync()).Jobs.Single(j=>j.Key.Id==Second);
        await (Task)typeof(StoreQueueEngine).GetMethod("UpdateWindowsProgressAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(f.Engine,[second.JobId,new WindowsStoreManagedProgress("downloading",20,20,100,1)])!;
        Require((await f.Engine.ControlAsync(new(second.JobId,second.Revision,StoreJobAction.Cancel,Guid.NewGuid()))).ErrorCode=="job_changed","legacy native control reproduces progress-revision race");
        runtime.FailCancel=true;var rejected=await f.Engine.ControlAsync(new(second.JobId,second.Revision,StoreJobAction.Cancel,Guid.NewGuid(),second.Key,second.AccountRevision,second.InstallPath));
        Require(!rejected.Accepted&&rejected.ErrorCode=="windows_store_cancel_failed"&&rejected.Snapshot.Jobs.Any(j=>j.JobId==second.JobId&&j.CanCancel),"native cancel refusal remains visible and retryable instead of pretending success");
        runtime.FailCancel=false;second=(await f.Engine.GetSnapshotAsync()).Jobs.Single(j=>j.Key.Id==Second);
        Require(second.CanCancel&&(await f.Engine.ControlAsync(new(second.JobId,second.Revision,StoreJobAction.Cancel,Guid.NewGuid(),second.Key,second.AccountRevision,second.InstallPath))).Accepted,"desktop control cancels actual active native operation");
        Require((await f.Engine.GetSnapshotAsync()).Jobs.All(j=>j.JobId!=second.JobId),"desktop native cancel removes its row immediately");await Wait(()=>Task.FromResult(runtime.Cancels==1));
    }
    static async Task NativeQueueRestart()
    {
        var first=new Fixture{Keep=true};var runtime=new QueueRuntime();await PrepareQueue(first,runtime);await EnqueueNative(first,Product);await Wait(()=>Task.FromResult(runtime.Starts.Count==1));await EnqueueNative(first,Second);await EnqueueNative(first,Third);
        var third=(await first.Engine.GetSnapshotAsync()).Jobs.Single(j=>j.Key.Id==Third);
        Require((await first.Engine.CancelCleanupAsync(new(third.JobId,third.Revision,Guid.NewGuid(),false))).Accepted,"cancel pending native hides immediately");
        string folder=first.Folder;string? vault=first.Vault;await first.DisposeAsync();
        await using var restored=new Fixture(folder,vault);var next=new QueueRuntime{LookupBlock=new(TaskCreationOptions.RunContinuationsAsynchronously)};restored.Provider.UseWindowsStoreManagedInstalls=true;restored.Engine.WindowsManagedRuntime=next;restored.Engine.WindowsManagedDelay=(_,ct)=>Task.Delay(5,ct);await restored.Engine.InitializeAsync();
        await next.LookupEntered.Task;await Task.Delay(25);Require(next.Starts.Count==0,"restored native reservation blocks queue before slow package lookup attaches");next.LookupBlock.TrySetResult();
        await Wait(()=>Task.FromResult(next.Observed.Contains(Product)));Require(!next.Starts.Contains(Product),"restart observes active native operation without replaying install");
        next.Finish(Product);await Wait(()=>Task.FromResult(next.Starts.Contains(Second)));
        Require(!next.Starts.Contains(Third)&&(await restored.Engine.GetSnapshotAsync()).Jobs.All(j=>j.Key.Id!=Third),"pending queue resumes after recovery while canceled request stays absent");
        Require(next.LastVolume==Volume,"persisted exact selected volume survives restart");next.Finish(Second);
    }
    static async Task NativeQueueRegisteredRestart()
    {
        var first=new Fixture{Keep=true};var runtime=new QueueRuntime{RootPath=Path.GetPathRoot(first.Folder)!};await PrepareQueue(first,runtime);
        await EnqueueNative(first,Product);await Wait(()=>Task.FromResult(runtime.Starts.Count==1));await EnqueueNative(first,Second);
        string folder=first.Folder;string? vault=first.Vault;Directory.CreateDirectory(first.PackagePath);await first.DisposeAsync();
        await using var restored=new Fixture(folder,vault);var next=new QueueRuntime{RootPath=runtime.RootPath,Package=new(Family,"registered",restored.PackagePath,null,null,true)};
        restored.Provider.UseWindowsStoreManagedInstalls=true;restored.Engine.WindowsManagedRuntime=next;restored.Engine.WindowsManagedDelay=(_,ct)=>Task.Delay(5,ct);await restored.Engine.InitializeAsync();
        await Wait(()=>Task.FromResult(next.Observed.Contains(Product)));next.Finish(Product);
        await Wait(()=>Task.FromResult(next.Starts.Contains(Second)));await Wait(async()=>(await restored.Engine.GetSnapshotAsync()).Jobs.Single(j=>j.Key.Id==Product).State==StoreJobState.Completed);
        Require(!next.Starts.Contains(Product)&&next.Observed.Contains(Product),"restored registered package requires native final deployment proof without replaying install");
        Require((await restored.Engine.GetSnapshotAsync()).Jobs.Single(j=>j.Key.Id==Product).State==StoreJobState.Completed,"healthy exact restored registration completes after full deployment proof");next.Finish(Second);
    }
    static async Task NativeHistorySelection()
    {
        await using var f=new Fixture();var runtime=new Runtime{Exit=1603};await f.Start(runtime);await Wait(async()=>(await f.Job()).State==StoreJobState.Failed);
        var reviewed=await f.Engine.GetSnapshotAsync();var job=reviewed.Jobs.Single();var account=reviewed.Accounts.Single(a=>a.Store==StoreKind.Xbox);await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require((await f.Engine.DismissWindowsInstallRequestAsync(new(reviewed.Revision,Guid.NewGuid(),job.JobId))).ErrorCode=="queue_changed","legacy Windows history dismissal reproduces unrelated global revision refusal");
        var result=await f.Engine.DismissWindowsInstallRequestAsync(new(reviewed.Revision,Guid.NewGuid(),job.JobId,job.Revision,job.Key,job.AccountRevision));
        Require(result.Accepted&&result.Snapshot.Jobs.Count==0&&runtime.Starts==1,"scoped Windows history dismissal survives unrelated refresh without starting or canceling native work");
    }
    static async Task FullDeploymentOnly()
    {
        var first=new Fixture{Keep=true};var runtime=new QueueRuntime{RootPath=Path.GetPathRoot(first.Folder)!};await PrepareQueue(first,runtime);await EnqueueNative(first,Product);await Wait(()=>Task.FromResult(runtime.Starts.Count==1));
        Directory.CreateDirectory(first.PackagePath);runtime.Package=new(Family,"registered",first.PackagePath,null,null,true);await Task.Delay(30);
        var before=(await first.Engine.GetSnapshotAsync()).Jobs.Single();Require(before.State!=StoreJobState.Completed&&before.DownloadedBytes==10&&before.TotalBytes==100,"early healthy package registration at 10 percent is never full installation");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var rows=(List<StoreDownloadJob>)typeof(StoreQueueEngine).GetField("jobs",flags)!.GetValue(first.Engine)!;rows[0]=before with{State=StoreJobState.Completed,ProgressPercent=100};
        typeof(StoreQueueEngine).GetMethod("Changed",flags)!.Invoke(first.Engine,null);string folder=first.Folder;string? vault=first.Vault;await first.DisposeAsync();
        await using var restored=new Fixture(folder,vault);var next=new QueueRuntime{RootPath=runtime.RootPath,Package=runtime.Package};restored.Provider.UseWindowsStoreManagedInstalls=true;restored.Engine.WindowsManagedRuntime=next;restored.Engine.WindowsManagedDelay=(_,ct)=>Task.Delay(5,ct);await restored.Engine.InitializeAsync();
        await Wait(()=>Task.FromResult(next.Observed.Contains(Product)));var active=await restored.Engine.GetSnapshotAsync();
        Require(next.Starts.Count==0,"legacy false completion reattaches exact native operation without replaying install");
        Require(active.Jobs.Single().State!=StoreJobState.Completed&&active.Jobs.Single().DownloadedBytes==10&&active.Jobs.Single().TotalBytes==100,"restart restores real partial progress instead of 100 percent");
        Require(!active.Games.Single(g=>g.Key.Id==Product).IsInstalled,"registered partial package cannot make inventory Installed");
        next.Finish(Product);await Wait(async()=>(await restored.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Completed);
        var finished=(await restored.Engine.GetSnapshotAsync()).Jobs.Single();Require(finished.DownloadedBytes==100&&finished.TotalBytes==100,"full native final-byte telemetry and successful CLI are required before completion");
    }
    sealed class QueueRuntime:IWindowsStoreManagedRuntime,IWindowsStoreInstallDestinationRuntime
    {
        internal readonly ConcurrentBag<string> Starts=[],Observed=[];readonly ConcurrentDictionary<string,TaskCompletionSource<int?>> completions=[];
        int active;internal int Peak,Cancels;internal bool FailCancel;internal string? LastVolume;internal string RootPath=@"F:\";internal WindowsXboxPackage? Package;internal TaskCompletionSource? LookupBlock;internal readonly TaskCompletionSource LookupEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Available=>true;
        public async Task<WindowsXboxPackage?> FindPackageAsync(string family,CancellationToken token){if(family==Family&&LookupBlock is not null){LookupEntered.TrySetResult();await LookupBlock.Task.WaitAsync(token);}return Package?.PackageFamilyName==family?Package:null;}
        public Task<int?> RunInstallAsync(string id,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token)=>throw new Exception("native queue uses InstallControl");
        public Task<IReadOnlyList<WindowsStoreInstallVolume>> GetInstallVolumesAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<WindowsStoreInstallVolume>>([new(Volume,RootPath,10000,true)]);
        public async Task<int?> RunOnVolumeAsync(string id,string family,string? volume,bool launch,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token)
        {Require(family==QueueFamily(id)&&volume==Volume,"worker receives reviewed exact identity and volume");LastVolume=volume;var completion=completions.GetOrAdd(id,_=>new(TaskCreationOptions.RunContinuationsAsynchronously));int count=Interlocked.Increment(ref active);Peak=Math.Max(Peak,count);if(launch)Starts.Add(id);else Observed.Add(id);try{await progress(new("downloading",10,10,100,1));var exit=await completion.Task.WaitAsync(token);if(exit==0)await progress(new("installing",100,100,100,0));return exit;}finally{Interlocked.Decrement(ref active);}}
        internal void Finish(string id)=>completions[id].TrySetResult(0);
        public Task CancelInstallAsync(string id,CancellationToken token){if(FailCancel)throw new IOException("fixture refused cancellation");Interlocked.Increment(ref Cancels);completions.GetOrAdd(id,_=>new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(5);return Task.CompletedTask;}
    }
    sealed class QueueFiles:IStoreInstallFileSystem
    {public IReadOnlyList<StoreInstallLocation> Locations()=>[];public string CanonicalDestination(string path)=>Path.GetFullPath(path);public long FreeBytes(string path)=>100000;public bool CanClaim(string path,Guid? id)=>true;public bool Claim(string path,Guid id)=>true;}
    static async Task DirectRefreshCompletion()
    {
        foreach(var store in new[]{StoreKind.Epic,StoreKind.Gog,StoreKind.Amazon})
        {
            var provider=new QueueDirectProvider(store);await using var f=new Fixture(other:provider,files:new QueueFiles());await f.Engine.InitializeAsync();
            var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==store);await f.Engine.RefreshAsync(store,account.Revision);await f.Engine.ObserveSteamAsync(new(Guid.NewGuid(),1,f.Now,false),true);
            string path=Path.Combine(f.Folder,"direct");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"fixture.exe"),"inert fixture only");
            var preparation=await f.Engine.PrepareInstallAsync(new(store,"direct"),account.Revision,path);
            Require((await f.Engine.EnqueueAsync(new(preparation.Key,account.Revision,path,Guid.NewGuid(),preparation.SelectionNonce!.Value,preparation.Revision))).Accepted,"actual direct producer accepted for "+store);
            await Wait(()=>Task.FromResult(provider.Starts==1));provider.ListEnabled=false;
            Require((await f.Engine.RefreshAsync(store,account.Revision)).Accepted&&(await f.Engine.GetSnapshotAsync()).Games.Any(g=>g.Key==preparation.Key),"provider refresh retains same-account pending direct record for "+store);
            ((Dictionary<StoreGameKey,StoreLibraryGame>)typeof(StoreQueueEngine).GetField("games",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Engine)!).Remove(preparation.Key);
            provider.Current!.Complete();await Wait(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Completed);
            var game=(await f.Engine.GetSnapshotAsync()).Games.Single();Require(game.Key==preparation.Key&&game.IsInstalled&&game.IsLaunchable&&game.InstallPath==path,"verified direct completion recovers old missing game record for "+store);
        }
    }
    sealed class QueueDirectProvider(StoreKind kind=StoreKind.Gog):IStoreProvider
    {
        internal int Starts;internal bool ListEnabled=true;internal QueueTransfer? Current;public StoreKind Store=>kind;public StoreCapabilities Capabilities=>new(StoreInstallKind.Direct,true,true,true,true,true);public Uri AuthorizationUri=>new("https://fixture.invalid/");
        public Task<ProviderAccount?> GetAccountAsync(CancellationToken token)=>Task.FromResult<ProviderAccount?>(new("fixture-gog","Fixture"));public Task<ProviderAccount> ConnectAsync(string code,CancellationToken token)=>throw new NotSupportedException();public Task DisconnectAsync(CancellationToken token)=>Task.CompletedTask;
        public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<ProviderGame>>(ListEnabled?[new("direct","Inert direct",true,10)]:[]);public Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken token)=>Task.FromResult(new ProviderInstallSize(10,10));
        public Task<ProviderLaunch?> ResolveLaunchAsync(string id,string path,CancellationToken token)=>Task.FromResult(File.Exists(Path.Combine(path,"fixture.exe"))?new ProviderLaunch(Path.Combine(path,"fixture.exe"),path,[]):null);public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id,string path,CancellationToken token)=>throw new NotSupportedException();
        public Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken token){Interlocked.Increment(ref Starts);progress(new(1,10,1,"downloading"));Current=new();return Task.FromResult<IStoreTransfer>(Current);}
    }
    sealed class QueueTransfer:IStoreTransfer
    {readonly TaskCompletionSource<TransferCompletion> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);public Task<TransferCompletion> Completion=>completion.Task;internal void Complete()=>completion.TrySetResult(new(true,false,true));public Task RequestCheckpointAsync(CancellationToken token){completion.TrySetResult(new(false,true,false,Stopped:true));return Task.CompletedTask;}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
}
