// SPDX-License-Identifier: MIT. Synthetic current-account/OS/catalogue fixtures only; no native install, activation, removal or network.
using System.Net;
using System.Text.Json;
using Playhub.StoreLibrary;
static class Checks
{
    const string Product="9AAA00000001",Family="Fixture.Game_abcdefghijklm";
    static int count;static void Check(bool ok,string name){count++;if(!ok)throw new Exception(name);}
    static async Task Main()
    {
        await Happy();await Refusals();await AccountChanges();await WrongNativeProduct();await Restart();await Metadata();await PartnerMetadata();await Concurrent();await ForeignAccount();await PurchasedPriority();await InvalidJournal();await Transient();await ColdTransientProtocol();await PendingRefreshAndRepair();
        Console.WriteLine($"{count} Game Pass checks PASS (actual provider/queue/journal/launch; inert HTTP/OS only)");
    }
    sealed class HttpStub:HttpMessageHandler
    {
        internal readonly List<string> Requests=[];internal object? ProductBody;internal object ProviderBody=new{Products=new Dictionary<string,object>()};
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string uri=request.RequestUri!.AbsoluteUri;Requests.Add(request.Method+" "+request.RequestUri.Host+request.RequestUri.AbsolutePath);
            object? auth=uri.EndsWith("/token")?new{access_token="inert-access",refresh_token="inert-refresh",token_type="Bearer",scope="XboxLive.signin offline_access",expires_in=3600}
                :uri.Contains("user.auth.xboxlive.com")?new{Token="inert-user"}
                :uri.Contains("xsts.auth.xboxlive.com")?new{Token="inert-xsts",NotAfter=DateTimeOffset.Parse("2026-11-15T13:00:00Z"),DisplayClaims=new{xui=new[]{new{uhs="123",xid="999",gtg="Fixture"}}}}
                :uri.Contains("collections.mp.microsoft.com")?new{items=Array.Empty<object>()}:null;
            if(auth is not null)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(JsonSerializer.Serialize(auth))});
            bool provider=request.RequestUri!.Host=="catalog.gamepass.com";
            Check(provider?request.Method==HttpMethod.Post&&request.RequestUri.Query.Contains("hydration=PCInline"):request.Method==HttpMethod.Get&&request.RequestUri.Host=="displaycatalog.mp.microsoft.com","only expected public metadata endpoints");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(provider?ProviderBody:ProductBody)),RequestMessage=request});
        }
    }
    sealed class Runtime:IWindowsStoreManagedRuntime
    {
        internal bool available=true;public bool Available=>available;internal int runs;internal WindowsXboxPackage? package;internal Func<bool>? journal;internal bool wrong;internal bool LookupFault;internal string Folder="";internal TaskCompletionSource? Hold;internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WindowsXboxPackage?> FindPackageAsync(string family,CancellationToken token)=>LookupFault?Task.FromException<WindowsXboxPackage?>(new IOException("inert optional lookup failure")):Task.FromResult(package);
        public async Task<int?> RunInstallAsync(string id,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token)
        {Check(journal?.Invoke()==true,"explicit request provenance durable before managed CLI");runs++;Started.TrySetResult();if(Hold is not null)await Hold.Task.WaitAsync(token);package=new(Family,"Fixture.Game_1.0.0.0_x64__abcdefghijklm",Folder,Family+"!App",Path.Combine(Folder,"Game.exe"),true,"Game.exe",wrong?"9AAA00000002":Product);return 0;}
    }
    sealed class Fixture:IAsyncDisposable
    {
        internal readonly string folder;internal readonly Runtime runtime=new();internal readonly HttpStub handler=new();internal readonly HttpClient http;
        internal StoreXboxAccountProvider provider;internal StoreQueueEngine engine;internal string? credential;internal DateTimeOffset time=DateTimeOffset.Parse("2026-11-15T12:00:00Z");internal int metadataCalls;
        internal string Journal=>Path.Combine(folder,"gp.json");internal StoreGamePassInstallRequest request=new(Product,9,2,Guid.NewGuid());
        internal StoreXboxCatalogSnapshot catalog;internal StoreXboxOwnedGame metadata=new(Product,"Inert GP game",true,false,"catalogue",PackageFamilyName:Family,RequiredBytes:20);
        internal Fixture()
        {
            folder=Path.Combine(Path.GetTempPath(),"Playhub-GamePass-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"Game.exe"),"inert payload");File.WriteAllText(Path.Combine(folder,"Session.exe"),"inert wrapper");
            credential=JsonSerializer.Serialize(new{ClientId=StoreXboxAccountAuth.ClientId,RefreshToken="inert-test",Xuid="999",Gamertag="Fixture"});http=new(handler);
            catalog=new(9,"fixture","fixture","US","en-US","pc","pc",time,time.AddHours(1),false,[new(Product,"Inert GP game",null,true,"desktop-confirmed")]);
            provider=CreateProvider();engine=CreateEngine();runtime.Folder=folder;runtime.journal=()=>File.Exists(Journal);
        }
        StoreXboxAccountProvider CreateProvider()
        {
            var value=new StoreXboxAccountProvider(http,()=>credential,text=>credential=text,()=>time){GamePassCacheOverride=Journal,UseWindowsStoreManagedInstalls=true,SessionExecutable=Path.Combine(folder,"Session.exe")};
            value.GamePassMetadataLookup=(_,_)=>{metadataCalls++;return Task.FromResult<StoreXboxOwnedGame?>(metadata);};value.PackageLookup=(_,_)=>Task.FromResult(runtime.package);value.PackageBatchLookup=(_,_)=>Task.FromResult<IReadOnlyDictionary<string,WindowsXboxPackage>>(runtime.package is null?new Dictionary<string,WindowsXboxPackage>():new Dictionary<string,WindowsXboxPackage>{{Family,runtime.package}});return value;
        }
        StoreQueueEngine CreateEngine(){var value=new StoreQueueEngine([provider],new Files(),Path.Combine(folder,"queue.json"),()=>time){WindowsManagedRuntime=runtime,WindowsManagedDelay=(_,token)=>Task.Delay(5,token)};provider.Bind(value);return value;}
        internal Task Initialize()=>engine.InitializeAsync();internal Task<StoreXboxHandoffResult> Install()=>provider.InstallGamePassAsync(request,_=>Task.FromResult(catalog),default);
        internal async Task<StoreLibrarySnapshot> Complete(){for(int i=0;i<200;i++){var snapshot=await engine.GetSnapshotAsync();if(snapshot.Jobs.Any(job=>job.State==StoreJobState.Completed))return snapshot;await Task.Delay(5);}throw new Exception("fake install never completed");}
        internal async Task Restart(){await engine.DisposeAsync();await provider.DisposeAsync();provider=CreateProvider();engine=CreateEngine();await engine.InitializeAsync();}
        public async ValueTask DisposeAsync(){await engine.DisposeAsync();await provider.DisposeAsync();http.Dispose();Directory.Delete(folder,true);}
    }
    sealed class Files:IStoreInstallFileSystem
    {public IReadOnlyList<StoreInstallLocation> Locations()=>[];public string CanonicalDestination(string path)=>throw new Exception("GP has no fake destination");public long FreeBytes(string path)=>0;public bool CanClaim(string path,Guid? job)=>false;public bool Claim(string path,Guid job)=>false;}
    static async Task Happy()
    {
        await using var f=new Fixture();await f.Initialize();Check((await f.provider.GetXboxOwnedLibraryAsync()).Games.Count==0,"no purchase ownership before request");
        var result=await f.Install();Check(result.Accepted&&result.State=="official-install-requested","managed request acknowledged");var snapshot=await f.Complete();var game=snapshot.Games.Single();
        Check(game.IsInstalled&&game.IsLaunchable&&game.LibraryKind=="gamePass","verified exact native installation retains GP provenance");Check(snapshot.Jobs.Single().LibraryKind=="gamePass"&&snapshot.Jobs.Single().ProgressPercent==100,"job classified only after verified completion");
        Check((await f.provider.GetXboxOwnedLibraryAsync()).Games.Count==0,"catalog request never fabricates purchased or conditional ownership");Check(await f.provider.ResolveLaunchAsync(Product,f.folder,default) is not null,"confirmed GP native package launches through existing registered path");
        result=await f.Install();Check(result.Accepted&&f.runtime.runs==1,"same request never starts CLI twice");f.request=f.request with{ProductId="9AAA00000002"};result=await f.Install();Check(!result.Accepted,"request identity cannot change");
    }
    static async Task Refusals()
    {
        foreach(string mode in new[]{"stale","removed","console","revision","account","runtime","metadata-pc","metadata-family","metadata-id","journal"})
        {
            await using var f=new Fixture();await f.Initialize();
            if(mode=="stale")f.catalog=f.catalog with{IsStale=true};if(mode=="removed")f.catalog=f.catalog with{Games=[]};if(mode=="console")f.catalog=f.catalog with{Games=[new(Product,"Console",null,true,"not-desktop")]};if(mode=="revision")f.request=f.request with{ExpectedRevision=10};if(mode=="account")f.request=f.request with{ExpectedAccountRevision=7};if(mode=="runtime")f.runtime.available=false;
            if(mode=="metadata-pc")f.metadata=f.metadata with{SupportsPc=false};if(mode=="metadata-family")f.metadata=f.metadata with{PackageFamilyName=null};if(mode=="metadata-id")f.metadata=f.metadata with{ProductId="9AAA00000002"};if(mode=="journal")f.provider.GamePassCacheOverride=f.folder;
            StoreXboxHandoffResult? result=null;try{result=await f.Install();}catch(IOException)when(mode=="journal"){}
            Check(result?.Accepted!=true,"refuse "+mode);Check(f.runtime.runs==0,"no CLI on "+mode);Check((await f.engine.GetSnapshotAsync()).Jobs.Count==0,"no queue job on "+mode);
        }
    }
    static async Task AccountChanges()
    {
        await using var f=new Fixture();await f.Initialize();f.provider.GamePassMetadataLookup=async(_,token)=>{await f.provider.DisconnectAsync(token);return f.metadata;};var result=await f.Install();Check(!result.Accepted&&f.runtime.runs==0&&!File.Exists(f.Journal),"account change during metadata cannot persist or invoke install");
    }
    static async Task WrongNativeProduct()
    {
        await using var f=new Fixture();await f.Initialize();f.runtime.wrong=true;Check((await f.Install()).Accepted,"OS may accept request without installation proof");await Task.Delay(50);var snapshot=await f.engine.GetSnapshotAsync();Check(!snapshot.Games.Single().IsInstalled&&snapshot.Jobs.Single().State!=StoreJobState.Completed,"same PFN with wrong native StoreId cannot complete GP");Check(await f.provider.ResolveLaunchAsync(Product,f.folder,default) is null,"wrong StoreId cannot launch GP record");
    }
    static async Task Restart()
    {
        await using var f=new Fixture();await f.Initialize();await f.Install();await f.Complete();await f.Restart();Check((await f.engine.GetSnapshotAsync()).Games.Single().IsInstalled,"bound provenance survives host restart");Check(await f.provider.ResolveLaunchAsync(Product,f.folder,default) is not null&&f.runtime.runs==1,"offline native launch after restart does not replay install");
    }
    static async Task Metadata()
    {
        var handler=new HttpStub();using var http=new HttpClient(handler);var collection=new StoreXboxAccountCollections(new StoreXboxAccountAuth(http,()=>DateTimeOffset.UtcNow),()=>DateTimeOffset.UtcNow);
        object ProductBody(string platform,string? family,string kind="Game")=>new{Products=new[]{new{ProductId=Product,ProductKind=kind,LocalizedProperties=new[]{new{ProductTitle="Inert public product"}},DisplaySkuAvailabilities=new[]{new{Sku=new{Properties=new{Packages=new[]{new{PackageFamilyName=family,PlatformDependencies=new[]{new{PlatformName=platform}}}}}}}}}}};
        handler.ProductBody=ProductBody("Windows.Desktop",Family);Check((await collection.GamePassMetadataAsync(Product,default))?.PackageFamilyName==Family,"fresh exact PC package family used for GP");
        handler.ProductBody=ProductBody("Windows.Xbox",Family);Check(await collection.GamePassMetadataAsync(Product,default) is null,"console package never forced PC");handler.ProductBody=ProductBody("Windows.Desktop",null);Check(await collection.GamePassMetadataAsync(Product,default) is null,"missing family never guessed");handler.ProductBody=ProductBody("Windows.Desktop",Family,"Application");Check(await collection.GamePassMetadataAsync(Product,default) is null,"non-game refused");handler.ProductBody=new{Products=Array.Empty<object>()};Check(await collection.GamePassMetadataAsync(Product,default) is null,"missing fresh product cannot reuse prior cached metadata");
    }
    static async Task PartnerMetadata()
    {
        foreach(var(provider,extension)in new[]{("battlenet","XPDM5VSMTKQLBJ"),("ubisoft","XPDP2QW12DFSFK"),("ea","XPFC0VB7MLFWLC"),("unknown","XPUNKNOWN12345")})
        {
            using var document=JsonDocument.Parse(JsonSerializer.Serialize(new{Products=new Dictionary<string,object>{{Product,new{StoreId=Product,GameCatalogExtensionId=extension,ProductTitle="Identical arbitrary title",PublisherName="Ignored publisher"}},{"9AAA00000002",new{StoreId="9AAA00000002",GameCatalogExtensionId=extension,ProductTitle="Different unrelated title",PublisherName="Different publisher"}},{"9AAA00000003",new{StoreId="9AAA00000003",GameCatalogExtensionId=(string?)null,ProductTitle="Battle.net Ubisoft EA named but native"}}}}));
            var classified=StoreXboxInstallProviders.Parse(document.RootElement,[Product,"9AAA00000002","9AAA00000003"]);
            Check(classified[Product].Provider==provider&&classified["9AAA00000002"].Provider==provider,"launcher relationship applies generically to multiple arbitrary titles:"+provider);
            Check(classified["9AAA00000003"].Provider=="microsoft"&&classified["9AAA00000003"].ExtensionId is null,"titles and publishers cannot turn native game into partner");
            await using var f=new Fixture();await f.Initialize();f.metadata=f.metadata with{InstallProvider=provider,InstallProviderId=extension};
            var refused=await f.Install();string code="xbox_partner_"+provider+"_install_unavailable";
            Check(!refused.Accepted&&refused.ErrorCode==code&&f.runtime.runs==0&&!File.Exists(f.Journal)&&(await f.engine.GetSnapshotAsync()).Jobs.Count==0,"fresh partner metadata refuses native MSIX before journal or queue:"+provider);
            f.catalog=f.catalog with{Games=[f.catalog.Games.Single() with{InstallProvider=provider,InstallProviderId=extension}]};int calls=f.metadataCalls;
            Check((await f.Install()).ErrorCode==code&&f.metadataCalls==calls,"classified catalog rejects partner before package lookup:"+provider);
            var owner=await f.provider.GetAccountAsync(default);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(StoreXboxAccountProvider).GetField("owned",flags)!.SetValue(f.provider,new StoreXboxOwnedSnapshot(3,f.time,false,[f.metadata with{AccessKind="owned"}]));
            typeof(StoreXboxAccountProvider).GetField("ownedIdentity",flags)!.SetValue(f.provider,owner!.AccountIdentity);
            var owned=await f.provider.OpenXboxOwnedInstallPageAsync(new(Product,3,Guid.NewGuid()),default);
            Check(!owned.Accepted&&owned.ErrorCode==code&&f.runtime.runs==0,"Xbox Store and Game Pass share exact partner policy:"+provider);
        }
        using var mismatch=JsonDocument.Parse("{\"Products\":{\"9AAA00000001\":{\"StoreId\":\"9AAA00000002\",\"GameCatalogExtensionId\":\"XPDM5VSMTKQLBJ\"}}}");
        try{StoreXboxInstallProviders.Parse(mismatch.RootElement,[Product]);throw new Exception("mismatched identity accepted");}
        catch(StoreOperationException error){Check(error.Code=="xbox_install_provider_identity_mismatch","hydration identity mismatch cannot classify another product");}
        using var badExtension=JsonDocument.Parse("{\"Products\":{\"9AAA00000001\":{\"StoreId\":\"9AAA00000001\",\"GameCatalogExtensionId\":\"publisher@example.invalid\"},\"9AAA00000002\":{\"StoreId\":\"9AAA00000002\",\"GameCatalogExtensionId\":\"XPDM5VSMTKQLBJ\"}}}");
        var isolated=StoreXboxInstallProviders.Parse(badExtension.RootElement,[Product,"9AAA00000002"]);
        Check(isolated["9AAA00000002"].Provider=="battlenet"&&isolated[Product].Provider=="unknown"&&isolated[Product].ExtensionId=="invalid-extension","malformed external field is isolated and never leaks raw publisher data into cache or frontend");
        Check(StoreXboxInstallProviders.Refusal(isolated[Product].Provider,isolated[Product].ExtensionId)=="xbox_partner_unknown_install_unavailable","malformed explicit extension can never trigger false native MSIX");
    }
    static async Task Concurrent()
    {await using var f=new Fixture();await f.Initialize();var results=await Task.WhenAll(f.Install(),f.Install());Check(results.All(row=>row.Accepted),"same concurrent request has idempotent ACK");await f.Complete();Check(f.runtime.runs==1&&(await f.engine.GetSnapshotAsync()).Jobs.Count==1,"single queue receipt and CLI for concurrent request");}
    static async Task ForeignAccount()
    {await using var f=new Fixture();await f.Initialize();await f.Install();await f.Complete();f.credential=JsonSerializer.Serialize(new{ClientId=StoreXboxAccountAuth.ClientId,RefreshToken="inert-other",Xuid="888",Gamertag="Other"});await f.Restart();Check(await f.provider.ResolveLaunchAsync(Product,f.folder,default) is null,"foreign Microsoft account cannot inherit GP launch provenance");Check(!f.provider.CurrentGamePassFamily(Product,Family),"foreign account family refused");}
    static async Task PurchasedPriority()
    {await using var f=new Fixture();await f.Initialize();var owner=await f.provider.GetAccountAsync(default);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(StoreXboxAccountProvider).GetField("owned",flags)!.SetValue(f.provider,new StoreXboxOwnedSnapshot(3,f.time,false,[new(Product,"Purchased",true,false,"owned",PackageFamilyName:Family)]));
        typeof(StoreXboxAccountProvider).GetField("ownedIdentity",flags)!.SetValue(f.provider,owner!.AccountIdentity);await f.Install();var snapshot=await f.Complete();Check(snapshot.Games.Single().LibraryKind=="store"&&snapshot.Jobs.Single().LibraryKind=="store","purchased ownership has collection priority over explicit catalog request");}
    static async Task InvalidJournal()
    {await using var f=new Fixture();await f.Initialize();await f.Install();await f.Complete();File.WriteAllText(f.Journal,"{\"Version\":1,\"Requests\":[null]}");await f.Restart();Check(!f.provider.CanRequestGamePass,"malformed journal refuses future writes instead of erasing provenance");Check(await f.provider.ResolveLaunchAsync(Product,f.folder,default) is null,"malformed journal never fabricates GP launch ownership");}
    static async Task Transient()
    {foreach(bool stale in new[]{false,true}){await using var f=new Fixture();await f.Initialize();f.catalog=f.catalog with{SourceWarning="transient_response_not_cached",ValidatedAt=f.time.AddSeconds(stale?-60:0),ExpiresAt=f.time.AddSeconds(stale?-60:0),IsStale=true};var result=await f.Install();Check(result.Accepted!=stale,"only the fresh response of this bounded operation permits a transient catalog request");if(!stale)await f.Complete();else Check(f.runtime.runs==0,"historical no-store response cannot authorize native install");}}
    static async Task PendingRefreshAndRepair()
    {
        foreach(bool oldMissingRecord in new[]{false,true})
        {
            await using var f=new Fixture();f.runtime.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);await f.Initialize();Check((await f.Install()).Accepted,"deferred native Game Pass request accepted");await f.runtime.Started.Task;
            var refreshed=await f.engine.RefreshAsync(StoreKind.Xbox,2);Check(refreshed.Accepted,"actual owned refresh succeeds while Game Pass installation is pending: "+refreshed.ErrorCode+" requests="+string.Join(";",f.handler.Requests));
            var pending=await f.engine.GetSnapshotAsync();Check(pending.Games.Single().Key.Id==Product&&!pending.Games.Single().IsInstalled,"owned refresh retains exact account pending Game Pass staged record");
            if(oldMissingRecord)((Dictionary<StoreGameKey,StoreLibraryGame>)typeof(StoreQueueEngine).GetField("games",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(f.engine)!).Clear();
            f.runtime.Hold.TrySetResult();var completed=await f.Complete();Check(completed.Games.Single().IsInstalled&&completed.Games.Single().IsLaunchable&&completed.Games.Single().LibraryKind=="gamePass","verified native completion reconstructs missing metadata from exact current-account request provenance");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;((Dictionary<StoreGameKey,StoreLibraryGame>)typeof(StoreQueueEngine).GetField("games",flags)!.GetValue(f.engine)!).Clear();typeof(StoreQueueEngine).GetMethod("Changed",flags)!.Invoke(f.engine,null);
            long jobRevision=completed.Jobs.Single().Revision;
            if(!oldMissingRecord)f.runtime.LookupFault=true;
            await f.Restart();
            if(!oldMissingRecord){Check((await f.engine.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Xbox).State==StoreConnectionState.Connected,"optional orphan lookup failure cannot disable connected accounts or host startup");f.runtime.LookupFault=false;Check((await f.engine.RefreshAsync(StoreKind.Xbox,2)).Accepted,"later normal refresh retries optional orphan recovery");}
            var repaired=await f.engine.GetSnapshotAsync();
            Check(repaired.Games.Single().IsInstalled&&repaired.Games.Single().IsLaunchable&&repaired.Jobs.Single().Revision==jobRevision,"startup repairs an already completed orphan using healthy exact PFN without changing completion history revision");
            Check(f.runtime.runs==1,"completed orphan repair never replays native installation");
        }
    }
    static async Task ColdTransientProtocol()
    {
        await using var f=new Fixture();await f.Initialize();using var http=new HttpClient(new CatalogNetwork());
        string path=Path.Combine(f.folder,"catalog.json");var first=new StoreXboxCatalog(path,"US","en-US",http,()=>f.time);
        var display=await first.RefreshXboxCatalogAsync(true);Check(display.ErrorCode is null&&display.Games.Single().ProductId==Product,"actual no-store catalog review loaded");
        var cold=new StoreXboxCatalog(path,"US","en-US",http,()=>f.time);Check((await cold.GetXboxCatalogAsync()).Games.Count==0,"new host has no saved no-store metadata");
        var protocol=new StoreGamePassProtocol(cold,f.provider);var request=f.request with{ExpectedRevision=display.Revision};
        using var args=JsonDocument.Parse(JsonSerializer.Serialize(request,StoreLibraryService.JsonOptions));var result=await protocol.TryHandleAsync("xbox-gamepass-install",args.RootElement,default);
        Check(result.Handled&&result.Data is StoreXboxHandoffResult{Accepted:true},"explicit install revalidates old display revision against fresh current membership after host restart");
        await f.Complete();Check(!File.ReadAllText(path).Contains("Inert catalog title"),"request-scoped no-store catalog is never persisted");
    }
    sealed class CatalogNetwork:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string body=request.RequestUri!.AbsolutePath.Contains("sigls")?"[{\"siglId\":\"609d944c-d395-4c0a-9ea4-e9f39b52c1ad\"},{\"id\":\"9AAA00000001\"}]"
                :request.RequestUri.Host=="displaycatalog.mp.microsoft.com"?
                """{"Products":[{"ProductId":"9AAA00000001","ProductKind":"Game","LocalizedProperties":[{"ProductTitle":"Inert catalog title"}],"DisplaySkuAvailabilities":[{"Availabilities":[{"Conditions":{"ClientConditions":{"AllowedPlatforms":[{"PlatformName":"Windows.Desktop"}]}}}]}]}]}""":"{\"Products\":{}}";
            var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(body)};response.Headers.CacheControl=new(){NoStore=true};return Task.FromResult(response);
        }
    }

}
