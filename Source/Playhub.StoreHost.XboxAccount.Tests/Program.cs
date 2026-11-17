// SPDX-License-Identifier: MIT. Original inert integration checks; no real Microsoft requests or UI.
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

static partial class Checks
{
    static int assertions;
    static void Require(bool value,string name){assertions++;if(!value)throw new Exception(name);}
    const string Pc="9AAA00000001",ConsoleGame="9AAA00000002",Gold="9AAA00000003",Trial="9AAA00000004";
    static async Task Main()
    {
        await OptionalPackageSizes();
        DirectoryPickerStartsAtExistingParent();
        await Happy();await CancelLate();await Expired();await Denied();await IdentityMismatch();await PartialKeepsLastGood();
        await AmbiguousPages();await WrongMetadata();await NoTitleGuess();await OldSessionDoesNotCancelNew();await InvalidPage();await SlowDown();await SignOutDuringRead();
        await OptionalCollectionFlags();await DuplicateCollectionGrants();await MissingCatalogProduct();await WindowsStoreCliBoundary();
        await WindowsManagedInstall();await WindowsManagedRestart();await WindowsManagedAccountChange();
        await WindowsNativeUninstall();await WindowsNativeUninstallChanged();
        await RegisteredLocalGame();await BundlePackageFamily();await PartnerAuthorization();await LiveAchievementReader();
        Console.WriteLine($"{assertions} Xbox account assertions PASS (actual auth/provider/queue/collections; fake HTTP/vault/opener only)");
    }
    static async Task PartnerAuthorization()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);
        long revision=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox).Revision;
        foreach(string party in new[]{"https://login.battle.net/","https://accounts.ea.com/","https://ubiservices.ubi.com/"})
        {
            bool invoked=false;
            bool result=await f.Provider.WithPartnerAuthorizationAsync(revision,party,(ticket,token)=>{
                invoked=true;Require(ticket=="XBL3.0 x=321;PARTNER-SECRET","partner-scoped userhash belongs to its own XSTS ticket, not Xbox Live's hash");
                Require(!token.IsCancellationRequested,"callback bound to current account cancellation");return Task.FromResult(true);
            },default);
            Require(result&&invoked,"each verified local partner can receive private in-memory authorization");
        }
        int requests=f.Handler.UserCalls;
        try{await f.Provider.WithPartnerAuthorizationAsync(revision,"https://foreign.invalid/",(_,_)=>Task.FromResult(true),default);throw new Exception("foreign relying party accepted");}
        catch(StoreOperationException e){Require(e.Code=="xbox_partner_identity_invalid"&&requests==f.Handler.UserCalls,"foreign relying party refused before contacting authentication");}
        try{await f.Provider.WithPartnerAuthorizationAsync(revision+1,"https://login.battle.net/",(_,_)=>Task.FromResult(true),default);throw new Exception("stale account accepted");}
        catch(StoreOperationException e){Require(e.Code=="account_changed"&&requests==f.Handler.UserCalls,"stale account cannot authorize a partner");}
        f.Handler.PartnerWrongXuid=true;
        try{await f.Provider.WithPartnerAuthorizationAsync(revision,"https://login.battle.net/",(_,_)=>Task.FromResult(true),default);throw new Exception("foreign Xbox identity accepted");}
        catch(StoreOperationException e){Require(e.Code=="xbox_account_identity_mismatch","explicit different Xbox identity rejected even for an allowed partner");}
        Require(!File.ReadAllText(Path.Combine(f.folder,"queue.json")).Contains("PARTNER-SECRET"),"partner ticket never persisted in public queue");
    }
    static async Task Happy()
    {
        await using var f=new Fixture();await f.Initialize();
        var first=await f.Engine.GetSnapshotAsync();var rev=first.Accounts.Single(a=>a.Store==StoreKind.Xbox).Revision;
        Require(first.Accounts.Single(a=>a.Store==StoreKind.Xbox).Capabilities.CanConnect,"real Xbox provider registered");
        try{await f.Engine.BeginConnectAsync(StoreKind.Xbox);throw new Exception("legacy fake code accepted");}catch(StoreOperationException e){Require(e.Code=="typed_signin_required","legacy code route refused");}
        var s=await f.Provider.BeginXboxSignInAsync(rev);
        Require(s.AuthorizationUri.AbsoluteUri=="https://www.microsoft.com/link?otc=ABCD-EFGH","official prefilled normal sign-in");
        Require(s.ExpiresAt==f.Now.AddSeconds(600)&&s.PollAfterSeconds==5,"original Microsoft deadline");
        Require(!JsonSerializer.Serialize(s).Contains("DEVICE-SECRET"),"device credential not UI data");
        Require((await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox).State==StoreConnectionState.Connecting,"real reserved connecting");
        Require(!(await f.Engine.QuiesceAsync(new CancellationTokenSource(15).Token)).Equals(true),"active sign-in prevents false quiescence");
    }
    static async Task RegisteredLocalGame()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);
        const string localId="9AAA00000005",family="Fixture.Local_abcdefghijklm";
        Directory.CreateDirectory(f.folder);
        string exe=Path.Combine(f.folder,"Game.exe"),session=Path.Combine(f.folder,"Session.exe");
        File.WriteAllText(exe,"inert game");File.WriteAllText(session,"inert session");
        var package=new WindowsXboxPackage(family,"Fixture.Local_1.0.0.0_x64__abcdefghijklm",f.folder,family+"!App",exe,true,"Game.exe",localId,"Native local game");
        f.Provider.InstalledPackageLookup=_=>Task.FromResult<IReadOnlyList<WindowsXboxPackage>>([package]);
        f.Provider.PackageBatchLookup=(_,_)=>Task.FromResult<IReadOnlyDictionary<string,WindowsXboxPackage>>(new Dictionary<string,WindowsXboxPackage>{{family,package}});
        f.Provider.PackageLookup=(_,_)=>Task.FromResult<WindowsXboxPackage?>(package);f.Provider.SessionExecutable=session;
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        var refreshed=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        var game=refreshed.Snapshot.Games.Single(g=>g.Key.Id==localId);
        Require(refreshed.Accepted&&game.IsInstalled&&game.IsLaunchable,"registered PC game absent from Consumer Collections is visible and launchable by exact native identity");
        Require(game.Capabilities.InstallKind==StoreInstallKind.Unavailable,"local registration never fabricates a purchased install entitlement");
        Require((await f.Provider.GetXboxOwnedLibraryAsync()).Games.Single(g=>g.ProductId==localId).AccessKind=="installed","local installation provenance remains distinct from purchased ownership");
        Require(await f.Provider.ResolveLaunchAsync(localId,f.folder,default) is not null,"local launch plan revalidates registered StoreId and installation");
        f.Provider.PackageLookup=(_,_)=>Task.FromResult<WindowsXboxPackage?>(package with{StoreProductId=Pc});
        Require(await f.Provider.ResolveLaunchAsync(localId,f.folder,default) is null,"changed native StoreId cannot inherit a cached local mapping");
        f.Provider.InstalledPackageLookup=_=>Task.FromResult<IReadOnlyList<WindowsXboxPackage>>([]);
        f.Provider.PackageBatchLookup=(_,_)=>Task.FromResult<IReadOnlyDictionary<string,WindowsXboxPackage>>(new Dictionary<string,WindowsXboxPackage>());
        refreshed=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require(!refreshed.Snapshot.Games.Single(g=>g.Key.Id==localId).IsInstalled,"confirmed local removal clears stale installed status in the same refresh");
        refreshed=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require(!refreshed.Snapshot.Games.Any(g=>g.Key.Id==localId),"later refresh drops the removed local-only library row");
    }
    static async Task BundlePackageFamily()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.Bundle=true;
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        var result=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require(result.Accepted,"owned bundle with one exact PC child remains a valid library row");
        var owned=(await f.Provider.GetXboxOwnedLibraryAsync()).Games.Single(g=>g.ProductId==Pc);
        Require(owned.SupportsPc&&owned.PackageFamilyName==PcFamily,"owned bundle inherits its sole explicit PC child package identity");
        Require(result.Snapshot.Games.Count==1&&result.Snapshot.Games[0].Key.Id==Pc,"bundle package association never creates another purchased product");
        f.Handler.BundleExtra=true;
        result=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        owned=(await f.Provider.GetXboxOwnedLibraryAsync()).Games.Single(g=>g.ProductId==Pc);
        Require(result.Accepted&&owned.PackageFamilyName is null,"a multi-game bundle never inherits installation from its sole child with available PC family metadata");
        f.Handler.BundleExtra=false;f.Handler.GrantBundleChild=true;
        result=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        var rows=(await f.Provider.GetXboxOwnedLibraryAsync()).Games;
        Require(result.Accepted&&rows.Single(g=>g.ProductId==Pc).PackageFamilyName is null&&rows.Single(g=>g.ProductId=="9AAA00000005").PackageFamilyName==PcFamily,
            "a separately owned base game retains its native package identity without duplicating its bundle as installed");
    }
    static async Task OptionalCollectionFlags()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.OmitOptionalFlags=true;
        await Read(f);
        f.Handler.BadTrialType=true;var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        var failed=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require(!failed.Accepted&&failed.Snapshot.Games.Single().Key.Id==Pc,"present malformed optional flag preserves last complete owned library");
    }
    static async Task OptionalPackageSizes()
    {
        foreach(var sample in new (object? Wire,long? Expected)[]{(12345L,12345L),("67890",67890L),(null,null),(-1L,null),("unknown",null),(new{invalid=true},null),(2L*1024*1024*1024*1024,null)})
        {
            await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.InstallSize=sample.Wire;
            var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
            var result=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
            Require(result.Accepted,"optional package size "+(sample.Wire?.GetType().Name??"null")+" cannot hide the owned PC library");
            Require(result.Snapshot.Games.Single().DownloadBytes==sample.Expected,"only bounded positive exact package sizes reach the library");
        }
    }
    static void DirectoryPickerStartsAtExistingParent()
    {
        string folder=Path.Combine(Path.GetTempPath(),"Playhub-Directory-Picker-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string absent=Path.Combine(folder,"missing","game"),root=Path.GetPathRoot(folder)!;
        StoreInstallLocation[] roots=[new(root,"Fixture drive",0,true)];
        var result=StoreInstallDirectories.Read(absent,roots);
        Require(result.Path==folder&&result.ParentPath==Path.GetDirectoryName(folder),"missing suggested library opens its nearest existing parent");
        Require(!Directory.Exists(Path.Combine(folder,"missing")),"folder browsing never creates a suggested folder");
        foreach(bool permission in new[]{true,false})
        {
            int calls=0;
            try{StoreInstallDirectories.Read(folder,roots,_=>{calls++;throw permission?new UnauthorizedAccessException():new IOException();});throw new Exception("directory IO error was swallowed");}
            catch(StoreOperationException e){Require(e.Code=="directory_unavailable"&&calls==1,"permission and IO errors do not silently move the folder picker");}
        }
        string file=Path.Combine(folder,"file.txt");File.WriteAllText(file,"inert fixture");
        try{StoreInstallDirectories.Read(file,roots);throw new Exception("file was browsable as directory");}catch(StoreOperationException e){Require(e.Code=="directory_unavailable","file path cannot silently change the picker location");}
        File.Delete(file);Directory.Delete(folder);
    }
    static async Task DuplicateCollectionGrants()
    {
        foreach(bool conditionalFirst in new[]{false,true})
        {
            await using var f=new Fixture();await f.Initialize();await Connect(f);
            f.Handler.DuplicateGrant=true;f.Handler.ConditionalFirst=conditionalFirst;await Read(f);
            Require((await f.Provider.GetXboxOwnedLibraryAsync()).Games.Single(g=>g.ProductId==Pc).AccessKind=="owned","purchased grant wins over conditional duplicate in either page order");
        }
    }
    static async Task MissingCatalogProduct()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.OmitMetadata=ConsoleGame;
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        var first=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require(first.Accepted&&first.Snapshot.Games.Single().Key.Id==Pc,"one delisted public-catalog product cannot hide the whole owned PC library");
        var unknown=(await f.Provider.GetXboxOwnedLibraryAsync()).Games.Single(g=>g.ProductId==ConsoleGame);
        Require(!unknown.SupportsPc&&!unknown.SupportsXbox&&unknown.AccessKind=="owned","unknown catalog platform never gains fabricated PC installation");
        f.Handler.OmitMetadata=Pc;var later=await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision);
        Require(later.Accepted&&later.Snapshot.Games.Single().Key.Id==Pc,"previous exact PC metadata survives a later successful catalog omission");
    }
    static async Task WindowsStoreCliBoundary()
    {
        string folder=Path.Combine(Path.GetTempPath(),"Playhub-WindowsStore-Args-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string alias=Path.Combine(folder,"store.exe");File.WriteAllText(alias,"inert fixture; never executed");
        string body="Description\nPFN : Foreign.App_abcdefghijklm\nTechnical Information\nProduct ID : "+Pc+"\nPFN : Publisher.Game_abcdefghijklm\n";int queries=0;
        var cli=new StoreWindowsStoreCli(alias,(exe,args,env,token)=>
        {queries++;Require(exe==alias&&args.SequenceEqual(new[]{"show",Pc}),"CLI metadata passes exact ID without a shell");return Task.FromResult((0,body));});
        var product=await cli.ReadProductAsync(Pc,default);
        Require(product.ProductId==Pc&&product.PackageFamilyName=="Publisher.Game_abcdefghijklm","PFN comes from final technical section, never publisher description");
        var install=cli.CreateInstallStartInfo(Pc);
        Require(install.FileName==alias&&!install.UseShellExecute&&install.CreateNoWindow&&install.ArgumentList.SequenceEqual(new[]{"install",Pc}),"reviewable official installer command contains only exact product ID");
        Require(queries==1,"constructing an installation plan never starts a process or queries an account");
        body=body.Replace("Product ID : "+Pc,"Product ID : "+ConsoleGame);
        try{await cli.ReadProductAsync(Pc,default);throw new Exception("foreign CLI product accepted");}catch(StoreOperationException e){Require(e.Code=="windows_store_product_invalid","foreign metadata cannot be used as install readback identity");}
        try{cli.CreateInstallStartInfo(Pc+" --other");throw new Exception("invalid install argument accepted");}catch(StoreOperationException e){Require(e.Code=="xbox_product_invalid","extra arguments never reach Store CLI");}
        File.Delete(alias);Directory.Delete(folder);
    }
    static async Task Connect(Fixture f)
    {
        var s=await f.Provider.BeginXboxSignInAsync((await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox).Revision);
        var pending=await f.Provider.PollXboxSignInAsync(s.SessionId);Require(pending.State==StoreXboxSignInState.Pending,"actual authorization pending");
        int count=f.Handler.TokenCalls;await f.Provider.PollXboxSignInAsync(s.SessionId);Require(f.Handler.TokenCalls==count,"early poll does not hit OAuth");
        f.Now=f.Now.AddSeconds(6);var done=await f.Provider.PollXboxSignInAsync(s.SessionId);
        Require(done.State==StoreXboxSignInState.Connected&&done.PollAfterSeconds==0,"connected only real OAuth+two XSTS audiences");
        Require(done.Snapshot.Accounts.Single(a=>a.Store==StoreKind.Xbox).State==StoreConnectionState.Connected,"canonical account connected");
        Require(f.Vault is not null&&f.Vault.Contains("REFRESH-SECRET"),"credential commit uses vault seam");
        Require(!JsonSerializer.Serialize(done).Contains("SECRET"),"no tokens in UI/queue snapshot");
        Require((await f.Provider.PollXboxSignInAsync(s.SessionId)).State==StoreXboxSignInState.Connected,"terminal receipt repeatable");
        Require(f.Handler.UserCalls==1&&f.Handler.XstsCalls==2,"exact real Xbox Live and marketplace chain");
    }
    const string PcFamily="Publisher.Game_abcdefghijklm";
    static async Task WaitFor(Func<Task<bool>> done)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(!await done())await Task.Delay(5,timeout.Token);
    }
    static async Task<StoreXboxHandoffResult> ManagedStart(Fixture f,FakeWindows runtime)
    {
        f.Provider.UseWindowsStoreManagedInstalls=true;f.Engine.WindowsManagedRuntime=runtime;
        f.Engine.WindowsManagedDelay=(_,token)=>runtime.Next.WaitAsync(token);
        await f.Initialize();await Connect(f);
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        Require((await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision)).Accepted,"managed fixture owned refresh");
        var owned=await f.Provider.GetXboxOwnedLibraryAsync();
        return await f.Provider.OpenXboxOwnedInstallPageAsync(new(Pc,owned.Revision,Guid.NewGuid()));
    }
    static async Task WindowsManagedInstall()
    {
        await using var f=new Fixture();var runtime=new FakeWindows();
        var result=await ManagedStart(f,runtime);
        Require(result.Accepted&&result.State=="official-install-requested"&&f.Opened.Count==0,"managed command does not merely open a product page");
        await WaitFor(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.ExternalHandoff);
        var pending=(await f.Engine.GetSnapshotAsync()).Jobs.Single();
        Require(runtime.Starts==1&&pending.ProgressPercent==42&&pending.TotalBytes==null&&pending.DownloadedBytes==0,"real phase retained without invented transferred bytes");
        Require(!pending.CanPause&&!pending.CanResume&&!pending.CanCancel&&!pending.CanDownloadNow,"service-owned transfer promises no unsupported controls");
        Require(!(await f.Engine.GetSnapshotAsync()).Games.Single().IsInstalled,"CLI exit zero is not completion");
        Directory.CreateDirectory(Path.Combine(f.folder,"package"));
        runtime.Package=new("Foreign.App_abcdefghijklm","foreign",Path.Combine(f.folder,"package"),null,null,true);runtime.Next.Release();
        await WaitFor(()=>Task.FromResult(runtime.Reads>=3));
        Require((await f.Engine.GetSnapshotAsync()).Jobs.Single().State!=StoreJobState.Completed,"foreign package cannot complete exact product install");
        runtime.Package=new(PcFamily,"registered",Path.Combine(f.folder,"package"),null,null,true);runtime.Next.Release();
        await WaitFor(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Completed);
        var completed=await f.Engine.GetSnapshotAsync();
        Require(completed.Games.Single().IsInstalled&&completed.Games.Single().InstallPath==runtime.Package.InstallLocation,"completion publishes verified OS location");
        Require(completed.Jobs.Single().ProgressPercent==100&&runtime.Starts==1,"one installer request and authoritative package completion");
        var replay=await f.Engine.BeginWindowsManagedInstallAsync(f.Provider,Pc,completed.Games.Single().AccountRevision,PcFamily,Guid.NewGuid(),default);
        await WaitFor(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Last().State==StoreJobState.Completed);
        Require(replay.Accepted&&runtime.Starts==1,"preexisting verified package never starts a second installer");
        Require(WindowsStoreManagedRuntime.ParseProgress("Starting0% Installing90%") is {Phase:"installing",Percent:90},"real Store CLI format parses without line breaks");
        Require(WindowsStoreManagedRuntime.ParseProgress("Installing101%") is null&&WindowsStoreManagedRuntime.ParseProgress("Unknown90%") is null,"invalid or unknown output cannot fabricate progress");
    }
    static async Task WindowsManagedRestart()
    {
        var first=new Fixture(){KeepFolder=true};var runtime=new FakeWindows();await ManagedStart(first,runtime);
        await WaitFor(async()=>(await first.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.ExternalHandoff);
        string folder=first.folder;string? vault=first.Vault;await first.DisposeAsync();
        await using var restarted=new Fixture(folder,vault);var observer=new FakeWindows();
        restarted.Engine.WindowsManagedRuntime=observer;restarted.Engine.WindowsManagedDelay=(_,token)=>observer.Next.WaitAsync(token);
        await restarted.Initialize();
        try{await WaitFor(()=>Task.FromResult(observer.Reads>=2));}catch{throw new Exception("restart observer did not attach: reads="+observer.Reads+" snapshot="+JsonSerializer.Serialize(await restarted.Engine.GetSnapshotAsync()));}
        Require(observer.Starts==0,"host restart observes receipt without replaying install");
        Directory.CreateDirectory(Path.Combine(folder,"package"));observer.Package=new(PcFamily,"registered",Path.Combine(folder,"package"),null,null,true);observer.Next.Release();
        try{await WaitFor(async()=>(await restarted.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Completed);}catch{throw new Exception("restart exact package did not complete: reads="+observer.Reads+" snapshot="+JsonSerializer.Serialize(await restarted.Engine.GetSnapshotAsync()));}
        Require(observer.Starts==0,"restarted observation completes only OS registration");
    }
    static async Task WindowsManagedAccountChange()
    {
        await using var f=new Fixture();var runtime=new FakeWindows();await ManagedStart(f,runtime);
        await WaitFor(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.ExternalHandoff);
        var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        await f.Engine.DisconnectAsync(StoreKind.Xbox,account.Revision);
        Directory.CreateDirectory(Path.Combine(f.folder,"package"));runtime.Package=new(PcFamily,"registered",Path.Combine(f.folder,"package"),null,null,true);runtime.Next.Release();
        await WaitFor(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Completed);
        Require(!(await f.Engine.GetSnapshotAsync()).Games.Any(g=>g.IsInstalled),"late OS installation cannot populate a disconnected account");
    }
    sealed class FakeWindows:IWindowsStoreManagedRuntime
    {
        public bool Available=>true;internal int Starts,Reads;internal WindowsXboxPackage? Package;internal readonly SemaphoreSlim Next=new(0);
        public Task<WindowsXboxPackage?> FindPackageAsync(string family,CancellationToken token){Interlocked.Increment(ref Reads);return Task.FromResult(Package);}
        public async Task<int?> RunInstallAsync(string productId,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token)
        {Interlocked.Increment(ref Starts);await progress(new("installing",42));return 0;}
    }
    static async Task<(Fixture Fixture,Func<WindowsXboxPackage?> Package,Action<WindowsXboxPackage?> Set,int[] Removes)> NativeFixture()
    {
        var f=new Fixture();Directory.CreateDirectory(Path.Combine(f.folder,"package"));
        WindowsXboxPackage? package=new(PcFamily,"Publisher.Game_1.0.0.0_x64__abcdefghijklm",Path.Combine(f.folder,"package"),null,null,true);
        f.Provider.PackageLookup=(_,_)=>Task.FromResult<WindowsXboxPackage?>(package);
        f.Provider.PackageBatchLookup=(_,_)=>Task.FromResult<IReadOnlyDictionary<string,WindowsXboxPackage>>(package is null?new Dictionary<string,WindowsXboxPackage>():new Dictionary<string,WindowsXboxPackage>{{PcFamily,package}});
        int[] removes=[0];f.Provider.NativeRefuseRunning=_=>{};
        f.Provider.NativePackageRemove=(plan,_)=>{Require(plan.Family==PcFamily&&plan.Key.Id==Pc,"OS removal receives the exact confirmed product/PFN");removes[0]++;package=null;return Task.CompletedTask;};
        await f.Initialize();await Connect(f);var account=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        Require((await f.Engine.RefreshAsync(StoreKind.Xbox,account.Revision)).Accepted,"preexisting registered Xbox package refresh");
        Require((await f.Engine.GetSnapshotAsync()).Jobs.Count==0,"preexisting Xbox installation needs no fabricated download receipt");
        Require(await f.Provider.IsInstalledAsync(Pc,package!.InstallLocation,default),"registered package remains installed independently of an unavailable launch manifest");
        return(f,()=>package,p=>package=p,removes);
    }
    static async Task WindowsNativeUninstall()
    {
        var fixture=await NativeFixture();await using var f=fixture.Fixture;var key=new StoreGameKey(StoreKind.Xbox,Pc);
        string sentinel=Path.Combine(fixture.Package()!.InstallLocation,"unrelated-user-file.txt");File.WriteAllText(sentinel,"untouched");
        var status=await f.Engine.GetInstalledGameAsync(key);
        Require(status.CanUninstall&&status.IsInstalled,"preexisting same-account registered package can be removed without filesystem marker");
        typeof(StoreQueueEngine).GetMethod("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(f.Engine,null);
        var wrongSelectedPath=await f.Engine.PrepareUninstallForPathAsync(key,status.AccountRevision,status.LibraryRevision,Path.Combine(f.folder,"foreign"));
        Require(!wrongSelectedPath.CanUninstall&&wrongSelectedPath.ErrorCode=="installation_changed","native prepare preserves the explicit selected installation path despite unrelated revisions");
        var receipt=await f.Engine.PrepareUninstallAsync(key,status.AccountRevision,status.LibraryRevision);
        Require(receipt.CanUninstall&&receipt.Nonce.HasValue,"OS package uninstall has a real expiring nonce");
        Require(receipt.LibraryRevision>status.LibraryRevision,"native preparation revalidates the install and issues current observational revision after unrelated metadata updates");
        Require(!(await f.Engine.UninstallAsync(receipt with{InstallPath=Path.Combine(f.folder,"foreign")},Guid.NewGuid())).Accepted&&fixture.Removes[0]==0,"wire path mutation cannot alter the private package plan");
        typeof(StoreQueueEngine).GetMethod("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(f.Engine,null);
        Guid request=Guid.NewGuid();var removed=await f.Engine.UninstallAsync(receipt,request);
        Require(removed.Accepted&&!removed.Snapshot.Games.Single().IsInstalled&&removed.Snapshot.Games.Single().InstallPath==null,"OS absence alone publishes removal");
        Require(File.ReadAllText(sentinel)=="untouched"&&fixture.Removes[0]==1,"Playhub never deletes package/user files itself");
        Require((await f.Engine.UninstallAsync(receipt,request)).Accepted&&fixture.Removes[0]==1,"confirmed OS removal request is idempotent");
    }
    static async Task WindowsNativeUninstallChanged()
    {
        foreach(string change in new[]{"version","account","expiry","still-installed","running"})
        {
            var fixture=await NativeFixture();await using var f=fixture.Fixture;var key=new StoreGameKey(StoreKind.Xbox,Pc);
            var status=await f.Engine.GetInstalledGameAsync(key);var receipt=await f.Engine.PrepareUninstallAsync(key,status.AccountRevision,status.LibraryRevision);
            if(change=="version")fixture.Set(fixture.Package()! with{PackageFullName="Publisher.Game_2.0.0.0_x64__abcdefghijklm"});
            if(change=="account")await f.Engine.DisconnectAsync(StoreKind.Xbox,status.AccountRevision);
            if(change=="expiry")f.Now=f.Now.AddMinutes(3);
            if(change=="still-installed")f.Provider.NativePackageRemove=(_,_)=>Task.CompletedTask;
            if(change=="running")f.Provider.NativeRefuseRunning=_=>throw new StoreOperationException("game_running");
            var result=await f.Engine.UninstallAsync(receipt,Guid.NewGuid());
            Require(!result.Accepted&&fixture.Removes[0]==0,"OS uninstall refuses changed "+change+" without mutation");
            if(change=="still-installed")Require(result.ErrorCode=="uninstall_unconfirmed"&&result.Snapshot.Games.Single().IsInstalled,"successful command without OS absence is never removal success");
        }
    }
    static async Task Read(Fixture f)
    {
        var a=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        var refreshed=await f.Engine.RefreshAsync(StoreKind.Xbox,a.Revision);
        Require(refreshed.Accepted,"complete owned refresh accepted");
        var rows=refreshed.Snapshot.Games.Where(g=>g.Key.Store==StoreKind.Xbox).ToArray();
        Require(rows.Length==1&&rows[0].Key.Id==Pc,"only PC-owned canonical rows, no console/trial/pass");
        Require(rows[0].Capabilities.InstallKind==StoreInstallKind.ExternalHandoff&&!rows[0].IsInstalled&&!rows[0].IsLaunchable,"honest official handoff only");
        var owned=await f.Provider.GetXboxOwnedLibraryAsync();
        Require(owned.Games.Count==3&&owned.Games.Single(g=>g.ProductId==Gold).AccessKind=="conditional","conditional grant distinct from ownership");
        Require(owned.Games.Single(g=>g.ProductId==ConsoleGame).SupportsXbox&&!owned.Games.Single(g=>g.ProductId==ConsoleGame).SupportsPc,"console platform not fabricated");
        var request=new StoreXboxHandoffRequest(Pc,owned.Revision,Guid.NewGuid());
        Require((await f.Provider.OpenXboxOwnedInstallPageAsync(request)).Accepted,"verified owned explicit official page");
        await f.Provider.OpenXboxOwnedInstallPageAsync(request);Require(f.Opened.Count==1,"one-shot request does not reopen");
        Require(f.Opened[0].AbsoluteUri=="ms-windows-store://pdp/?ProductId="+Pc,"fixed Store page, not install result");
        Require(!(await f.Provider.OpenXboxOwnedInstallPageAsync(new(ConsoleGame,owned.Revision,Guid.NewGuid()))).Accepted,"console handoff refused");
        f.Now=f.Now.AddMinutes(6);Require(!(await f.Provider.OpenXboxOwnedInstallPageAsync(new(Pc,owned.Revision,Guid.NewGuid()))).Accepted,"stale owned handoff refused");
    }
    static async Task CancelLate()
    {
        await using var f=new Fixture();await f.Initialize();f.Handler.BlockToken=true;
        var s=await f.Provider.BeginXboxSignInAsync(1);var poll=f.Provider.PollXboxSignInAsync(s.SessionId);
        await f.Handler.TokenEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var canceled=await f.Provider.CancelXboxSignInAsync(s.SessionId);Require(canceled.State==StoreXboxSignInState.Canceled,"explicit cancellation receipt");
        f.Handler.ReleaseToken.TrySetResult();await poll;
        Require(f.Vault is null&&f.Handler.UserCalls==0,"late canceled token never creates credentials/XSTS");
        Require((await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox).State==StoreConnectionState.Disconnected,"canceled account not connected");
    }
    static async Task Expired()
    {
        await using var f=new Fixture();await f.Initialize();var s=await f.Provider.BeginXboxSignInAsync(1);f.Now=f.Now.AddSeconds(601);
        var r=await f.Provider.PollXboxSignInAsync(s.SessionId);Require(r.State==StoreXboxSignInState.Expired&&r.PollAfterSeconds==0,"original deadline expires");
        Require(f.Handler.TokenCalls==0&&f.Vault is null,"expiry makes no token request");
    }
    static async Task Denied()
    {
        await using var f=new Fixture();await f.Initialize();f.Handler.Error="access_denied";var s=await f.Provider.BeginXboxSignInAsync(1);
        Require((await f.Provider.PollXboxSignInAsync(s.SessionId)).State==StoreXboxSignInState.Denied,"actual declined response distinct");Require(f.Vault is null,"denied no credentials");
    }
    static async Task IdentityMismatch()
    {
        await using var f=new Fixture();await f.Initialize();f.Handler.Mismatch=true;f.Handler.TokenCalls=1;var s=await f.Provider.BeginXboxSignInAsync(1);
        Require((await f.Provider.PollXboxSignInAsync(s.SessionId)).State==StoreXboxSignInState.Failed,"foreign XSTS audience identity refused");Require(f.Vault is null,"foreign token not persisted");
    }
    static async Task PartialKeepsLastGood()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);await Read(f);f.Now=f.Now.AddMinutes(-6);
        var before=await f.Engine.GetSnapshotAsync();f.Handler.FailSecondPage=true;
        var r=await f.Engine.RefreshAsync(StoreKind.Xbox,before.Accounts.Single(a=>a.Store==StoreKind.Xbox).Revision);
        Require(!r.Accepted&&r.Snapshot.Games.Count==before.Games.Count,"partial page cannot erase last good library");
        Require((await f.Provider.GetXboxOwnedLibraryAsync()).IsStale,"partial library labeled stale");
    }
    static async Task AmbiguousPages()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.RepeatCursor=true;
        var a=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        Require(!(await f.Engine.RefreshAsync(StoreKind.Xbox,a.Revision)).Accepted,"repeated continuation cannot become complete");Require(f.Handler.CollectionCalls==2,"bounded continuation refusal");
    }
    static async Task WrongMetadata()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.WrongMetadata=true;
        var a=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        Require(!(await f.Engine.RefreshAsync(StoreKind.Xbox,a.Revision)).Accepted,"foreign product metadata cannot grant PC access");
    }
    static async Task NoTitleGuess()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.NoPc=true;
        var a=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);var r=await f.Engine.RefreshAsync(StoreKind.Xbox,a.Revision);
        Require(r.Accepted&&!r.Snapshot.Games.Any(g=>g.Key.Store==StoreKind.Xbox),"title saying PC without platform metadata is not admitted");
    }
    static async Task OldSessionDoesNotCancelNew()
    {
        await using var f=new Fixture();await f.Initialize();var s=await f.Provider.BeginXboxSignInAsync(1);await f.Provider.CancelXboxSignInAsync(s.SessionId);
        var a=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);var next=await f.Provider.BeginXboxSignInAsync(a.Revision);
        await f.Provider.CancelXboxSignInAsync(s.SessionId);Require((await f.Provider.PollXboxSignInAsync(next.SessionId)).State==StoreXboxSignInState.Pending,"old cancellation does not retire successor");
    }
    static async Task InvalidPage()
    {
        await using var f=new Fixture();await f.Initialize();f.Handler.BadPage=true;
        try{await f.Provider.BeginXboxSignInAsync(1);throw new Exception("foreign sign-in page accepted");}catch(StoreOperationException){Require(f.Vault is null,"foreign verification URL rejected");}
        Require((await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox).State==StoreConnectionState.Disconnected,"invalid grant reserves no account");
    }
    static async Task SlowDown()
    {
        await using var f=new Fixture();await f.Initialize();f.Handler.Error="slow_down";
        var s=await f.Provider.BeginXboxSignInAsync(1);var first=await f.Provider.PollXboxSignInAsync(s.SessionId);
        Require(first.State==StoreXboxSignInState.Pending&&first.PollAfterSeconds==10,"OAuth slow-down receipt changes next scheduling interval");
        f.Now=f.Now.AddSeconds(9);await f.Provider.PollXboxSignInAsync(s.SessionId);Require(f.Handler.TokenCalls==1,"slow-down actual server gate preserved");
    }
    static async Task SignOutDuringRead()
    {
        await using var f=new Fixture();await f.Initialize();await Connect(f);f.Handler.BlockCollections=true;
        var a=(await f.Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);
        var read=f.Engine.RefreshAsync(StoreKind.Xbox,a.Revision);await f.Handler.CollectionEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require((await f.Engine.DisconnectAsync(StoreKind.Xbox,a.Revision)).Accepted,"real sign-out while old library request is in flight");
        f.Handler.ReleaseCollection.TrySetResult();try{await read;}catch(OperationCanceledException){}
        var snapshot=await f.Engine.GetSnapshotAsync();Require(snapshot.Accounts.Single(a=>a.Store==StoreKind.Xbox).State==StoreConnectionState.Disconnected,"late library cannot resurrect account");
        Require(snapshot.Games.Count==0&&(await f.Provider.GetXboxOwnedLibraryAsync()).Games.Count==0&&f.Vault==null,"late owned metadata cannot resurrect games or tokens");
        Require(f.Handler.CollectionCalls==1,"sign-out cancels old pagination before next authenticated request");
    }
    sealed class Fixture:IAsyncDisposable
    {
        internal DateTimeOffset Now=new(2026,11,14,12,0,0,TimeSpan.Zero);internal string? Vault;internal readonly FakeHttp Handler;
        internal readonly StoreXboxAccountProvider Provider;internal readonly StoreQueueEngine Engine;internal readonly List<Uri> Opened=[];
        internal readonly string folder;readonly HttpClient http;internal bool KeepFolder;
        internal Fixture(string? existingFolder=null,string? vault=null){folder=existingFolder??Path.Combine(Path.GetTempPath(),"Playhub-Xbox-Owned-Inert-"+Guid.NewGuid().ToString("N"));Vault=vault;Handler=new(this);http=new(Handler);Provider=new(http,()=>Vault,v=>Vault=v,()=>Now,(uri,_)=>{Opened.Add(uri);return Task.FromResult(true);});Provider.PackageLookup=(_,_)=>Task.FromResult<WindowsXboxPackage?>(null);Provider.PackageBatchLookup=(_,_)=>Task.FromResult<IReadOnlyDictionary<string,WindowsXboxPackage>>(new Dictionary<string,WindowsXboxPackage>());Engine=new([Provider],new Files(),Path.Combine(folder,"queue.json"),()=>Now);Provider.Bind(Engine);}
        internal Task Initialize()=>Engine.InitializeAsync();
        public async ValueTask DisposeAsync()
        {
            Handler.ReleaseToken.TrySetResult();Handler.ReleaseCollection.TrySetResult();await Provider.DisposeAsync();await Engine.DisposeAsync();http.Dispose();
            string absolute=Path.GetFullPath(folder),temporary=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar;
            if(!absolute.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("Playhub-Xbox-Owned-Inert-",StringComparison.Ordinal))throw new Exception("fixture cleanup escaped its private temporary directory");
            if(!KeepFolder&&Directory.Exists(absolute))Directory.Delete(absolute,true);
        }
    }
    sealed class Files:IStoreInstallFileSystem
    {
        public IReadOnlyList<StoreInstallLocation> Locations()=>Array.Empty<StoreInstallLocation>();public string CanonicalDestination(string p)=>p;public long FreeBytes(string p)=>0;
        public bool CanClaim(string p,Guid? id)=>false;public bool Claim(string p,Guid id)=>false;
    }
    sealed class FakeHttp(Fixture fixture):HttpMessageHandler
    {
        internal int TokenCalls,UserCalls,XstsCalls,CollectionCalls;internal string? Error;
        internal object? InstallSize;
        internal string? OmitMetadata;
        internal bool BlockToken,Mismatch,FailSecondPage,RepeatCursor,WrongMetadata,NoPc,BadPage,BlockCollections,OmitOptionalFlags,BadTrialType,DuplicateGrant,ConditionalFirst,Bundle,BundleExtra,GrantBundleChild,PartnerWrongXuid;
        internal readonly TaskCompletionSource TokenEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),ReleaseToken=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CollectionEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),ReleaseCollection=new(TaskCreationOptions.RunContinuationsAsynchronously);
        static HttpResponseMessage Response(HttpRequestMessage request,object body,HttpStatusCode status=HttpStatusCode.OK)=>new(status){RequestMessage=request,Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string url=request.RequestUri!.AbsoluteUri;string payload=request.Content==null?"":await request.Content.ReadAsStringAsync(token);
            if(url.EndsWith("/devicecode"))return Response(request,new{device_code="DEVICE-SECRET",user_code="ABCD-EFGH",verification_uri=BadPage?"https://evil.test/link":"https://www.microsoft.com/link",expires_in=600,interval=5});
            if(url.EndsWith("/token"))
            {
                TokenCalls++;TokenEntered.TrySetResult();if(BlockToken)await ReleaseToken.Task; // late fake response deliberately ignores cancellation
                if(Error!=null)return Response(request,new{error=Error},HttpStatusCode.BadRequest);
                if(TokenCalls==1)return Response(request,new{error="authorization_pending"},HttpStatusCode.BadRequest);
                return Response(request,new{access_token="ACCESS-SECRET",refresh_token="REFRESH-SECRET",token_type="Bearer",scope="XboxLive.signin offline_access",expires_in=3600});
            }
            if(url.Contains("user.auth.xboxlive.com")){UserCalls++;return Response(request,new{Token="USER-SECRET"});}
            if(url.Contains("xsts.auth.xboxlive.com"))
            {
                XstsCalls++;bool store=payload.Contains("mp.microsoft.com");bool partner=payload.Contains("login.battle.net")||payload.Contains("accounts.ea.com")||payload.Contains("ubiservices.ubi.com");return Response(request,new{Token=partner?"PARTNER-SECRET":store?"MARKET-SECRET":"LIVE-SECRET",NotAfter=fixture.Now.AddHours(1),DisplayClaims=new{xui=new[]{new{uhs=partner?"321":Mismatch&&store?"456":"123",xid=partner&&PartnerWrongXuid?"777":"999",gtg="Fixture"}}}});
            }
            if(url.Contains("collections.mp.microsoft.com"))
            {
                CollectionCalls++;Require(request.Headers.GetValues("Authorization").Single()=="XBL3.0 x=123;MARKET-SECRET","correct marketplace authorization");
                if(BlockCollections){CollectionEntered.TrySetResult();await ReleaseCollection.Task;}
                using var doc=JsonDocument.Parse(payload);Require(doc.RootElement.GetProperty("beneficiaries")[0].GetProperty("identityValue").GetString()=="999","exact authenticated beneficiary");
                bool next=doc.RootElement.TryGetProperty("continuationToken",out _);
                if(next&&FailSecondPage)return Response(request,new{error="inert_failure"},HttpStatusCode.ServiceUnavailable);
                var later=new[]{Entitlement(Gold,conditional:true),Entitlement(Trial,trial:true),Entitlement("CFQ7TTC0KGQ8",kind:"Pass")}.ToList();
                if(DuplicateGrant)later.Add(Entitlement(Pc,conditional:!ConditionalFirst));
                var first=new[]{Entitlement(Pc,conditional:DuplicateGrant&&ConditionalFirst),Entitlement(ConsoleGame)}.ToList();
                if(GrantBundleChild)first.Add(Entitlement("9AAA00000005"));
                return next?Response(request,new{items=later.ToArray(),continuationToken=RepeatCursor?"page2":null})
                    :Response(request,new{items=first.ToArray(),continuationToken="page2"});
            }
            if(url.Contains("displaycatalog.mp.microsoft.com"))
            {
                string ids=Uri.UnescapeDataString(request.RequestUri.Query.Split("bigIds=")[1].Split('&')[0]);
                return Response(request,new{Products=ids.Split(',').Where(id=>id!=OmitMetadata).Select(id=>Product(WrongMetadata?"9AAA99999999":id)).ToArray()});
            }
            throw new Exception("Unexpected HTTP endpoint: fixture refuses non-protocol requests");
        }
        object Entitlement(string id,bool conditional=false,bool trial=false,string kind="Game")
        {
            var item=new Dictionary<string,object?>{{"productId",id},{"productKind",kind},{"status","Active"},{"endDate","9999-12-31T00:00:00Z"}};
            if(!OmitOptionalFlags||conditional||trial){item["isTrial"]=trial;item["licensingRequired"]=conditional;}
            if(BadTrialType)item["isTrial"]="false";
            return item;
        }
        object Product(string id)
        {
            if(Bundle&&id==Pc)return new{ProductId=id,Properties=new{PackageFamilyName=(string?)null},LocalizedProperties=new[]{new{ProductTitle="Owned bundle"}},
                DisplaySkuAvailabilities=new[]{new{Sku=new{Properties=new{Packages=Array.Empty<object>(),BundledSkus=BundleExtra?new[]{new{BigId="9AAA00000005"},new{BigId=ConsoleGame}}:new[]{new{BigId="9AAA00000005"}}}}}}};
            return new{ProductId=id,ProductKind="Game",Properties=new{PackageFamilyName=id==Pc||Bundle&&id=="9AAA00000005"?PcFamily:null},LocalizedProperties=new[]{new{ProductTitle=id==Pc?"A PC title (PC)":"Other title",Images=new[]{new{ImagePurpose="Poster",Uri="https://store-images.s-microsoft.com/image/fixture"}}}},
                DisplaySkuAvailabilities=new[]{new{Sku=new{Properties=new{Packages=new[]{new{MaxInstallSizeInBytes=InstallSize,PlatformDependencies=NoPc?Array.Empty<object>():new object[]{new{PlatformName=id==ConsoleGame?"Windows.Xbox":"Windows.Universal"}}}}}}}}};
        }
    }
}
