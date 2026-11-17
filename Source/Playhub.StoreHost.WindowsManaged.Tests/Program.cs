// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
// Actual provider/queue/receipt + fake HTTP/OS observation; no live account/install.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Playhub.StoreLibrary;

static partial class Checks
{
    const string Product="9AAA00000001",Family="Publisher.Game_abcdefghijklm";
    static int checks;
    static void Require(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static async Task Wait(Func<Task<bool>> done){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));while(!await done())await Task.Delay(5,timeout.Token);}
    static async Task Main(string[] args){if(args.Contains("full-deployment")){await FullDeploymentOnly();Console.WriteLine($"PASS {checks} full native deployment checks; inert runtime only");return;}Diagnostics();LanguageOptions();await ExplicitFailure();await Grace(0);await Grace(null);await ObservationFault();await ExistingPackage();await RegisteredDespiteExit();await RestartUnknown();await SanitizeForeignDiagnostic();await NativeDestinations();await NativeCancel();await NativeQueue();await NativeQueueRestart();await NativeQueueRegisteredRestart();await DirectRefreshCompletion();await NativeHistorySelection();Console.WriteLine($"PASS {checks} Windows-managed outcome checks (actual provider/queue/receipt, inert HTTP/package/runtime boundaries only)");}
    static void Diagnostics()
    {
        var d=new WindowsStoreManagedRuntime.Diagnostics();d.Add("https://auth.invalid/?secret=DO-NOT-PERSIST\nErro",true);d.Add("r: 0x803f8001\n",true);
        d.Add("Installing42%",false,true);var outcome=d.Snapshot(1603);
        Require(outcome is{ExitCode:1603,HResult:"0x803F8001",ReportedProgress:true},"bounded split stderr preserves only canonical HRESULT and actual progress");
        Require(!JsonSerializer.Serialize(outcome).Contains("DO-NOT-PERSIST"),"raw output/auth URL never returned");
        var clean=new WindowsStoreManagedRuntime.Diagnostics();clean.Add(new string('x',100000)+"\nError 0x00000000\nTOKEN 0x8BADF00D",true);
        Require(clean.Snapshot(0).HResult is null,"unlabeled hex/success is not an error diagnostic");
    }
    static async Task ExplicitFailure()
    {
        await using var f=new Fixture();var runtime=new Runtime{Exit=1603,Diagnostic=new(1603,"0x803F8001",false)};await f.Start(runtime);
        await Wait(async()=>(await f.Job()).State==StoreJobState.Failed);var job=await f.Job();
        Require(job.ErrorCode=="windows_store_install_failed"&&runtime.Starts==1,"nonzero exit is explicit bounded failure without replay");
        Require(!(await f.Engine.GetSnapshotAsync()).Games.Single().IsInstalled,"CLI failure never fabricates installed state");
        using var disk=JsonDocument.Parse(File.ReadAllText(f.Queue));var receipt=disk.RootElement.GetProperty("WindowsManagedInstalls")[0];
        Require(receipt.GetProperty("CliExitCode").GetInt32()==1603&&receipt.GetProperty("CliHResult").GetString()=="0x803F8001","sanitized numeric/HRESULT outcome survives queue persistence");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var observations=(Dictionary<Guid,Task>)typeof(StoreQueueEngine).GetField("windowsObservations",flags)!.GetValue(f.Engine)!;
        await Task.WhenAll(observations.Values).WaitAsync(TimeSpan.FromSeconds(3));
        var stops=(Dictionary<Guid,CancellationTokenSource>)typeof(StoreQueueEngine).GetField("windowsObservationStops",flags)!.GetValue(f.Engine)!;
        Require(stops.Count==0,"completed OS observation releases its exact CTS");
        Require(observations.Count==1,"completed observation task entry remains, preventing accidental replay");
    }
    static async Task Grace(int? exit)
    {
        await using var f=new Fixture();var runtime=new Runtime{Exit=exit};await f.Start(runtime);await Wait(async()=>(await f.Job()).State==StoreJobState.ExternalHandoff);
        Require((await f.Job()).ErrorCode is null,"registration receives initial grace");
        int reads=runtime.Reads;f.Now=f.Now.AddSeconds(29);runtime.Next.Release();await Wait(()=>Task.FromResult(runtime.Reads>reads));
        Require((await f.Job()).ErrorCode is null,"29 seconds does not fabricate a failure");
        f.Now=f.Now.AddSeconds(2);runtime.Next.Release();await Wait(async()=>(await f.Job()).ErrorCode=="windows_store_install_unconfirmed");
        var waiting=await f.Job();Require(waiting.State==StoreJobState.ExternalHandoff&&!waiting.CanPause&&!waiting.CanCancel&&waiting.ProgressPercent is null,"unknown state is visible, with no fake progress or control");
        runtime.Next.Release();await Task.Delay(30);Require((await f.Job()).Revision==waiting.Revision,"continued observation does not flash/re-publish the same warning every poll");
        Directory.CreateDirectory(f.PackagePath);runtime.Package=new("Foreign.Game_abcdefghijklm","foreign",f.PackagePath,null,null,true);runtime.Next.Release();await Task.Delay(30);
        Require((await f.Job()).State!=StoreJobState.Completed,"foreign PFN cannot resolve an unconfirmed installation");
        runtime.Package=new(Family,"registered",f.PackagePath,null,null,false);runtime.Next.Release();await Task.Delay(30);
        Require((await f.Job()).State!=StoreJobState.Completed,"unhealthy exact family cannot complete");
        runtime.Package=runtime.Package with{StatusOk=true};runtime.Next.Release();await Wait(async()=>(await f.Job()).State==StoreJobState.Completed);
        Require((await f.Job()).ErrorCode is null&&runtime.Starts==1,"late exact healthy package completes continued observation without replay");
    }
    static async Task ObservationFault()
    {
        await using var f=new Fixture();var runtime=new Runtime{Fault=true};await f.Start(runtime);await Wait(async()=>(await f.Job()).State==StoreJobState.ExternalHandoff);
        f.Now=f.Now.AddSeconds(31);runtime.Next.Release();await Wait(async()=>(await f.Job()).ErrorCode=="windows_store_install_unconfirmed");
        Require((await f.Job()).State!=StoreJobState.Failed,"unknown observation exception is not a confirmed Store failure");
        Require(!File.ReadAllText(f.Queue).Contains("DO-NOT-PERSIST"),"observation exception text never persists");
    }
    static async Task ExistingPackage()
    {
        await using var f=new Fixture();Directory.CreateDirectory(f.PackagePath);var runtime=new Runtime{Exit=1603,Package=new(Family,"registered",f.PackagePath,null,null,true)};await f.Start(runtime);
        await Wait(async()=>(await f.Job()).State==StoreJobState.Completed);Require(runtime.Starts==0,"exact OS registration wins before launching any command");
    }
    static async Task RegisteredDespiteExit()
    {
        await using var f=new Fixture();Directory.CreateDirectory(f.PackagePath);var runtime=new Runtime{Exit=1603};
        runtime.OnStart=()=>runtime.Package=new(Family,"registered",f.PackagePath,null,null,true);await f.Start(runtime);
        await Wait(async()=>(await f.Job()).State==StoreJobState.Completed);Require(runtime.Starts==1&&(await f.Job()).ErrorCode is null,"authoritative exact OS registration is checked before a returned nonzero exit");
    }
    static async Task RestartUnknown()
    {
        var first=new Fixture{Keep=true};var runtime=new Runtime{Exit=null};await first.Start(runtime);await Wait(async()=>(await first.Job()).State==StoreJobState.ExternalHandoff);
        string folder=first.Folder;string? vault=first.Vault;var later=first.Now.AddSeconds(40);await first.DisposeAsync();
        // Missing new outcome fields is an old v1 receipt, not a fabricated failure.
        var document=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"queue.json")))!;
        foreach(var receipt in document["WindowsManagedInstalls"]!.AsArray())foreach(string name in new[]{"CliCompletedAt","CliExitCode","CliHResult","CliReportedProgress"})receipt!.AsObject().Remove(name);
        File.WriteAllText(Path.Combine(folder,"queue.json"),document.ToJsonString());
        await using var restored=new Fixture(folder,vault){Now=later};var observer=new Runtime();restored.Bind(observer);await restored.Engine.InitializeAsync();
        await Wait(async()=>(await restored.Job()).ErrorCode=="windows_store_install_unconfirmed");Require(observer.Starts==0,"legacy restored unknown receipt is observed, never relaunched");
        Require((await restored.Job()).State==StoreJobState.ExternalHandoff,"missing historical outcome remains unknown");
    }
    static async Task SanitizeForeignDiagnostic()
    {
        await using var f=new Fixture();var runtime=new Runtime{Exit=1,Diagnostic=new(99,"https://auth.invalid/?DO-NOT-PERSIST",false)};await f.Start(runtime);await Wait(async()=>(await f.Job()).State==StoreJobState.Failed);
        string raw=File.ReadAllText(f.Queue);Require(!raw.Contains("DO-NOT-PERSIST"),"untrusted runtime diagnostic cannot enter queue JSON");using var disk=JsonDocument.Parse(raw);var row=disk.RootElement.GetProperty("WindowsManagedInstalls")[0];
        Require(row.GetProperty("CliExitCode").GetInt32()==1&&row.GetProperty("CliHResult").ValueKind==JsonValueKind.Null,"returned exit is authoritative; invalid diagnostic discarded");
    }
    sealed class Runtime:IWindowsStoreManagedRuntime,IWindowsStoreManagedOutcomeSource
    {
        internal int Starts,Reads;internal int? Exit;internal bool Fault;internal WindowsXboxPackage? Package;internal WindowsStoreManagedOutcome? Diagnostic;
        internal Action? OnStart;
        internal readonly SemaphoreSlim Next=new(0);public bool Available=>true;
        public Task<WindowsXboxPackage?> FindPackageAsync(string family,CancellationToken token){Interlocked.Increment(ref Reads);return Task.FromResult(Package);}
        public Task<int?> RunInstallAsync(string id,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token){Interlocked.Increment(ref Starts);OnStart?.Invoke();return Fault?Task.FromException<int?>(new IOException("DO-NOT-PERSIST")):Task.FromResult(Exit);}
        public WindowsStoreManagedOutcome? TakeOutcome(string id){var value=Diagnostic;Diagnostic=null;return value;}
    }
    sealed class Fixture:IAsyncDisposable
    {
        internal DateTimeOffset Now=new(2026,11,15,12,0,0,TimeSpan.Zero);internal string? Vault;internal readonly string Folder;internal string Queue=>Path.Combine(Folder,"queue.json");internal string PackagePath=>Path.Combine(Folder,"package");internal bool Keep;
        internal readonly StoreXboxAccountProvider Provider;internal readonly StoreQueueEngine Engine;readonly HttpClient http;internal string[] Products=[Product];
        internal Fixture(string? folder=null,string? vault=null,IStoreProvider? other=null,IStoreInstallFileSystem? files=null){Folder=folder??Path.Combine(Path.GetTempPath(),"Playhub-Xbox-Outcome-Inert-"+Guid.NewGuid().ToString("N"));Vault=vault;http=new(new Http(this));Provider=new(http,()=>Vault,value=>Vault=value,()=>Now,(_,_)=>throw new Exception("no external opener"));Provider.PackageLookup=(_,_)=>Task.FromResult<WindowsXboxPackage?>(null);Provider.PackageBatchLookup=(_,_)=>Task.FromResult<IReadOnlyDictionary<string,WindowsXboxPackage>>(new Dictionary<string,WindowsXboxPackage>());Engine=new(other is null?[Provider]:[Provider,other],files??new Files(),Queue,()=>Now);Provider.Bind(Engine);}
        internal void Bind(Runtime runtime){Provider.UseWindowsStoreManagedInstalls=true;Engine.WindowsManagedRuntime=runtime;Engine.WindowsManagedDelay=(_,token)=>runtime.Next.WaitAsync(token);}
        internal async Task Start(Runtime runtime){Bind(runtime);await Engine.InitializeAsync();var session=await Provider.BeginXboxSignInAsync(1);var connected=await Provider.PollXboxSignInAsync(session.SessionId);Require(connected.State==StoreXboxSignInState.Connected,"fake protocol connects exact fixture account");var account=(await Engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox);Require((await Engine.RefreshAsync(StoreKind.Xbox,account.Revision)).Accepted,"exact owned PC product loaded");var owned=await Provider.GetXboxOwnedLibraryAsync();Require((await Provider.OpenXboxOwnedInstallPageAsync(new(Product,owned.Revision,Guid.NewGuid()))).Accepted,"managed request accepted");}
        internal async Task<StoreDownloadJob> Job()=>(await Engine.GetSnapshotAsync()).Jobs.Single();
        public async ValueTask DisposeAsync(){await Provider.DisposeAsync();await Engine.DisposeAsync();http.Dispose();string full=Path.GetFullPath(Folder),parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));if(!string.Equals(Path.GetDirectoryName(full),parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("Playhub-Xbox-Outcome-Inert-",StringComparison.Ordinal))throw new IOException("fixture cleanup path changed");if(!Keep&&Directory.Exists(full))Directory.Delete(full,true);}
    }
    sealed class Files:IStoreInstallFileSystem{public IReadOnlyList<StoreInstallLocation> Locations()=>[];public string CanonicalDestination(string value)=>value;public long FreeBytes(string value)=>0;public bool CanClaim(string value,Guid? id)=>false;public bool Claim(string value,Guid id)=>false;}
    sealed class Http(Fixture fixture):HttpMessageHandler
    {
        static object ProductBody(string id)
        {
            object package=new{MaxInstallSizeInBytes=1024,PlatformDependencies=new[]{new{PlatformName="Windows.Universal"}}};
            object sku=new{Properties=new{Packages=new[]{package}}};
            return new{ProductId=id,ProductKind="Game",Properties=new{PackageFamilyName=QueueFamily(id)},LocalizedProperties=new[]{new{ProductTitle="Inert game"}},DisplaySkuAvailabilities=new[]{new{Sku=sku}}};
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string uri=request.RequestUri!.AbsoluteUri;
            object body=uri.EndsWith("/devicecode")?new{device_code="inert-device",user_code="INERT-TEST",verification_uri="https://www.microsoft.com/link",expires_in=600,interval=5}
                :uri.EndsWith("/token")?new{access_token="inert-access",refresh_token="inert-refresh",token_type="Bearer",scope="XboxLive.signin offline_access",expires_in=3600}
                :uri.Contains("user.auth.xboxlive.com")?new{Token="inert-user"}
                :uri.Contains("xsts.auth.xboxlive.com")?new{Token="inert-xsts",NotAfter=fixture.Now.AddHours(1),DisplayClaims=new{xui=new[]{new{uhs="123",xid="999",gtg="Fixture"}}}}
                :uri.Contains("collections.mp.microsoft.com")?new{items=fixture.Products.Select(id=>new{productId=id,productKind="Game",status="Active",endDate="9999-12-31T00:00:00Z",isTrial=false,licensingRequired=false})}
                :uri.Contains("displaycatalog.mp.microsoft.com")?new{Products=fixture.Products.Select(ProductBody)}
                :throw new Exception("fake HTTP refuses every unrelated endpoint");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")});
        }
    }
}
