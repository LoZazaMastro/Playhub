// SPDX-License-Identifier: MIT. Original inert Xbox public catalogue/handoff regression tests.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

static class Checks
{
    static int count;
    static void Check(bool value,string name){count++;if(!value)throw new Exception("FAIL "+name);}
    static async Task Main(string[] args)
    {
        await XboxInstalledChecks.Run(Check);
        var root=Path.Combine(Path.GetTempPath(),"Playhub-Xbox-Catalog-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        if(args.Contains("--installed-before"))
        {
            var legacyInstalled=await Pipe(root,null,true,"xbox-installed");
            Check(legacyInstalled.GetProperty("ok").GetBoolean(),"old provider registration cannot read installed Xbox snapshot");return;
        }
        var localInstalled=new StoreXboxInstalledLibrary("not-used",_=>Task.FromResult(new XboxInstalledRead([],false)),_=>Task.FromResult(false));
        var localReply=await Pipe(root,localInstalled,true,"xbox-installed");
        Check(localReply.GetProperty("ok").GetBoolean()&&localReply.GetProperty("data").GetProperty("scope").GetString()=="current-windows-user-installed","actual IPC dispatch exposes only truthful local registrations");
        var localDenied=await Pipe(root,localInstalled,false,"xbox-installed");
        Check(!localDenied.GetProperty("ok").GetBoolean()&&localDenied.GetProperty("errorCode").GetString()=="client_not_authorized","local registrations retain actual pinned-client IPC admission");
        if(args.Contains("--before"))
        {
            var legacy=await Pipe(root,null,true);
            Check(legacy.GetProperty("ok").GetBoolean(),"parent dispatcher has no registered Xbox public catalogue route");
            return;
        }
        var now=DateTimeOffset.Parse("2026-11-14T12:00:00Z");var network=new Network();var opened=new List<string>();var cache=Path.Combine(root,"catalogue.json");
        Task<bool> Open(Uri uri,CancellationToken token){opened.Add(uri.AbsoluteUri);return Task.FromResult(true);}
        var service=new StoreXboxCatalog(cache,"IT","it-IT",new HttpClient(network),()=>now,Open);
        var initial=await service.GetXboxCatalogAsync();Check(initial.Games.Count==0&&initial.IsStale&&initial.ValidatedAt==null,"empty cache is unknown not owned empty account");Check(network.Calls==0,"cached read does not access network");
        var first=await service.RefreshXboxCatalogAsync();Check(first.Revision==1&&!first.IsStale&&first.Games.Count==2&&first.ErrorCode==null,"complete public catalogue hydrated and cached");
        Check(first.Games.All(g=>g.EntitlementState=="not-checked"&&g.InstalledState=="not-checked"),"catalogue membership never grants entitlement or installed state");Check(first.Platform=="pc"&&first.Plan=="pc","explicit PC feed scope");
        Check(first.Games[0].PcAvailability=="desktop-confirmed"&&first.Games[1].PcAvailability=="not-desktop","product platform evidence is separate");
        Check(first.Games[0].ArtworkUrl!.StartsWith("https://store-images.s-microsoft.com/"),"official image normalized");Check(first.Games[1].ArtworkUrl==null,"foreign artwork refused");Check(first.ExpiresAt==now.AddMinutes(5),"minimum metadata cache lifetime honoured");
        var calls=network.Calls;Check((await service.GetXboxCatalogAsync()).ValidatedAt==first.ValidatedAt&&network.Calls==calls,"cached read never renews observation time");
        await service.RefreshXboxCatalogAsync();Check(network.Calls==calls,"fresh nonforced refresh is cache-only");
        var restored=new StoreXboxCatalog(cache,"IT","it-IT",new HttpClient(network),()=>now,Open);Check((await restored.GetXboxCatalogAsync()).SourceRevision==first.SourceRevision&&network.Calls==calls,"cold cache available without HTTP or login");
        now=now.AddMinutes(6);Check((await restored.GetXboxCatalogAsync()).IsStale,"expired snapshot marked stale without deleting entries");
        network.Partial=true;var partial=await service.RefreshXboxCatalogAsync(true);Check(partial.SourceRevision==first.SourceRevision&&partial.Games.Count==2&&partial.IsStale&&partial.ErrorCode!=null,"partial metadata preserves entire last known good cache");network.Partial=false;
        network.FailFeed=true;var failed=await service.RefreshXboxCatalogAsync(true);Check(failed.Revision==first.Revision&&failed.Games.Count==2,"network failure never turns library empty");network.FailFeed=false;
        network.BadId=true;var malformed=await service.RefreshXboxCatalogAsync(true);Check(malformed.SourceRevision==first.SourceRevision&&malformed.ErrorCode=="catalogue_schema_invalid","invalid product identity rejected");network.BadId=false;
        network.Duplicate=true;var duplicate=await service.RefreshXboxCatalogAsync(true);Check(duplicate.SourceRevision==first.SourceRevision&&duplicate.ErrorCode!=null,"duplicate feed identities rejected");network.Duplicate=false;
        network.Empty=true;var empty=await service.RefreshXboxCatalogAsync(true);Check(empty.Games.Count==2&&empty.ErrorCode!=null,"unexpected empty feed cannot delete collection");network.Empty=false;
        network.NoStore=true;var transient=await service.RefreshXboxCatalogAsync(true);
        Check(transient.ErrorCode is null&&transient.Games.Count==2&&transient.SourceWarning=="transient_response_not_cached","no-store response remains displayable, not a cache-forbidden failure");
        Check(transient.IsStale&&transient.ExpiresAt==transient.ValidatedAt,"no-store response expires immediately");
        var transientDisk=File.ReadAllText(cache);
        Check(!transientDisk.Contains("Title ",StringComparison.Ordinal)&&!transientDisk.Contains("store-images",StringComparison.Ordinal)&&!transientDisk.Contains(transient.SourceRevision,StringComparison.Ordinal),"no-store body/hash/image metadata never serialized");
        Check((await service.GetXboxCatalogAsync()).Games.Count==2,"current view can read transient response without claiming a saved catalogue");
        var transientCalls=network.Calls;await service.RefreshXboxCatalogAsync(false);
        Check(network.Calls>transientCalls&&!network.SawEtag,"every no-store refresh fetches unconditionally, never uses validator or cache freshness");
        var transientRestart=new StoreXboxCatalog(cache,"IT","it-IT",new HttpClient(network),()=>now,Open);
        var afterRestart=await transientRestart.GetXboxCatalogAsync();
        Check(afterRestart.Games.Count==0&&afterRestart.ValidatedAt is null&&afterRestart.SourceRevision=="","cold start cannot restore no-store metadata");
        Check(afterRestart.Revision==transient.Revision,"local monotone revision survives without response metadata");
        network.NoStore=false;
        await service.RefreshXboxCatalogAsync(true);network.NotModified=true;var metadataBefore=network.Metadata;var fresh=await service.RefreshXboxCatalogAsync(true);Check(fresh.Revision==first.Revision&&!fresh.IsStale&&fresh.ValidatedAt==now,"conditional membership cache succeeds");Check(network.SawEtag&&network.Metadata==metadataBefore+1,"SIGL304 still rehydrates actual metadata, no fake fresh product proof");network.NotModified=false;
        network.Ids=["9AAAAAAAAAAA"];var changed=await service.RefreshXboxCatalogAsync(true);Check(changed.Revision==2&&changed.Games.Single(g=>g.ProductId=="9BBBBBBBBBBB").IsCurrentMember==false,"membership removal retains historical metadata");
        Check(changed.Games.Single(g=>g.ProductId=="9AAAAAAAAAAA").IsCurrentMember,"current member remains independent of purchase evidence");
        var bad=new StoreXboxHandoffRequest("9AAAAAAAAAAA",1,Guid.NewGuid());Check(!(await service.OpenXboxInstallPageAsync(bad)).Accepted&&opened.Count==0,"stale revision cannot hand off");
        Check(!(await service.OpenXboxInstallPageAsync(new("9BBBBBBBBBBB",2,Guid.NewGuid()))).Accepted&&opened.Count==0,"removed membership cannot masquerade as current install card");
        foreach(var id in new[]{"9AAAAAAAAAAA&evil=1","../escape","https://evil.test","9aaaaaaaaaaa","", "9AAAAAAAAAAAA"})Check(!(await service.OpenXboxInstallPageAsync(new(id,2,Guid.NewGuid()))).Accepted&&opened.Count==0,"foreign/URI product ID refused");
        var request=new StoreXboxHandoffRequest("9AAAAAAAAAAA",2,Guid.NewGuid());var handoff=await service.OpenXboxInstallPageAsync(request);Check(handoff.Accepted&&handoff.State=="official-page-requested"&&opened.Count==1,"explicit user request opens official product page only");
        Check(opened.Single()=="ms-windows-store://pdp/?ProductId=9AAAAAAAAAAA","fixed official scheme and exact product, no folder or shell command");
        Check(handoff.InstallState=="not-observed"&&handoff.EntitlementState=="not-checked"&&handoff.LocationChoice=="official-client","handoff does not claim download/location/license success");
        Check(await service.OpenXboxInstallPageAsync(request)==handoff&&opened.Count==1,"request replay idempotent no second page");
        Check(!(await service.OpenXboxInstallPageAsync(request with {ProductId="9BBBBBBBBBBB"})).Accepted&&opened.Count==1,"request ID cannot be reused for other product");
        var replayed=new StoreXboxCatalog(cache,"IT","it-IT",new HttpClient(network),()=>now,Open);Check(await replayed.OpenXboxInstallPageAsync(request)==handoff&&opened.Count==1,"restart retains handoff receipt without duplicate UI");
        using var canceled=new CancellationTokenSource();canceled.Cancel();Check(!(await service.OpenXboxInstallPageAsync(new("9AAAAAAAAAAA",2,Guid.NewGuid()),canceled.Token)).Accepted&&opened.Count==1,"canceled user handoff never opens page");
        var foreignLocale=new StoreXboxCatalog(cache,"US","en-US",new HttpClient(network),()=>now,Open);Check((await foreignLocale.GetXboxCatalogAsync()).Games.Count==0&&opened.Count==1,"foreign market cache not merged silently");
        File.WriteAllText(Path.Combine(root,"broken.json"),"broken");var corrupt=new StoreXboxCatalog(Path.Combine(root,"broken.json"),"IT","it-IT",new HttpClient(network),()=>now,Open);Check((await corrupt.GetXboxCatalogAsync()).ErrorCode=="catalogue_cache_unreadable","corrupt cache explicit unknown no rewrite");Check(File.ReadAllText(Path.Combine(root,"broken.json"))=="broken","constructor preserves corrupt source evidence");
        var other=(await service.TryHandleAsync("snapshot",JsonSerializer.SerializeToElement(new{}),default));Check(!other.Handled,"extension does not override existing queue/auth methods");
        var cached=await service.TryHandleAsync("xbox-catalog",JsonSerializer.SerializeToElement(new{}),default);Check(cached.Handled&&cached.Data is StoreXboxCatalogSnapshot,"actual IPC extension exposes typed read snapshot");
        var noClient=new StoreXboxCatalog(Path.Combine(root,"no-client.json"),"IT","it-IT",new HttpClient(network),()=>now,(_,_)=>throw new InvalidOperationException("no handler"));var clientState=await noClient.RefreshXboxCatalogAsync(true);var unavailable=await noClient.OpenXboxInstallPageAsync(new("9AAAAAAAAAAA",clientState.Revision,Guid.NewGuid()));Check(!unavailable.Accepted&&unavailable.InstallState=="not-observed","missing official handler does not report install");
        foreach(var cancelAfterEffect in new[]{false,true})
        {
            var effectCount=0;
            var uncertain=new StoreXboxCatalog(Path.Combine(root,"uncertain-"+cancelAfterEffect+".json"),"IT","it-IT",new HttpClient(network),()=>now,(_,_)=>{effectCount++;if(cancelAfterEffect)throw new OperationCanceledException();throw new IOException("after invocation");});
            var ready=await uncertain.RefreshXboxCatalogAsync(true);var intent=new StoreXboxHandoffRequest("9AAAAAAAAAAA",ready.Revision,Guid.NewGuid());
            var result=await uncertain.OpenXboxInstallPageAsync(intent);
            Check(!result.Accepted&&result.State=="outcome-unknown"&&result.InstallState=="not-observed","effect then exception cannot be reported as not-opened");
            Check(await uncertain.OpenXboxInstallPageAsync(intent)==result&&effectCount==1,"uncertain outcome is idempotent, never relaunches official page");
        }
        foreach(var metadataOnly in new[]{false,true})
        {
            var noStoreNetwork=new Network { NoStore=!metadataOnly, MetadataNoStore=metadataOnly };
            var noStorePath=Path.Combine(root,"no-store-"+metadataOnly+".json");var noStoreOpens=0;
            Task<bool> NoStoreOpen(Uri uri,CancellationToken token)
            {
                var beforeEffect=File.ReadAllText(noStorePath);using var disk=JsonDocument.Parse(beforeEffect);
                Check(disk.RootElement.GetProperty("Requests").EnumerateObject().Count()==1,"one-shot intent is durably journaled BEFORE official-client boundary");
                Check(disk.RootElement.GetProperty("Snapshot").GetProperty("Games").GetArrayLength()==0&&!beforeEffect.Contains("Title ",StringComparison.Ordinal),"pre-effect journal save never stores no-store metadata");
                noStoreOpens++;return Task.FromResult(true);
            }
            var noStoreService=new StoreXboxCatalog(noStorePath,"IT","it-IT",new HttpClient(noStoreNetwork),()=>now,NoStoreOpen);
            var state=await noStoreService.RefreshXboxCatalogAsync();
            Check(state.Games.Count==2&&state.ErrorCode is null&&state.SourceWarning=="transient_response_not_cached","either no-store endpoint makes the whole response transient");
            var intent=new StoreXboxHandoffRequest("9AAAAAAAAAAA",state.Revision,Guid.NewGuid());var receipt=await noStoreService.OpenXboxInstallPageAsync(intent);
            Check(receipt.Accepted&&noStoreOpens==1,"explicit official page still works over transient current inventory");
            var persisted=File.ReadAllText(noStorePath);
            Check(persisted.Contains(intent.RequestId.ToString(),StringComparison.OrdinalIgnoreCase)&&!persisted.Contains("Title ",StringComparison.Ordinal)&&!persisted.Contains("store-images",StringComparison.Ordinal),"only one-shot user intent/receipt persists, not no-store metadata");
            using(var disk=JsonDocument.Parse(persisted))Check(disk.RootElement.GetProperty("Snapshot").GetProperty("Games").GetArrayLength()==0&&disk.RootElement.GetProperty("Etag").ValueKind==JsonValueKind.Null,"journal save cannot accidentally serialize transient catalogue or validator");
            var restart=new StoreXboxCatalog(noStorePath,"IT","it-IT",new HttpClient(noStoreNetwork),()=>now,NoStoreOpen);
            Check(await restart.OpenXboxInstallPageAsync(intent)==receipt&&noStoreOpens==1,"no-store journal replay remains idempotent after restart");
            Check((await restart.GetXboxCatalogAsync()).Games.Count==0,"receipt never restores no-store catalogue after restart");
            var again=await restart.RefreshXboxCatalogAsync();Check(again.Revision>state.Revision&&again.Games.Count==2,"fresh post-restart request receives a new local revision");
        }
        var transitionNetwork=new Network { MetadataNoStore=true };
        var transitionPath=Path.Combine(root,"transition-no-store.json");
        var transitionService=new StoreXboxCatalog(transitionPath,"IT","it-IT",new HttpClient(transitionNetwork),()=>now,(_,_)=>throw new Exception("No UI"));
        var transientPair=await transitionService.RefreshXboxCatalogAsync();
        Check(transientPair.Games.Count==2&&transientPair.SourceWarning=="transient_response_not_cached","actual transient pair observed before policy transition");
        transitionNetwork.NotModified=true;transitionNetwork.MetadataNoStore=false;
        var invalid304=await transitionService.RefreshXboxCatalogAsync();
        Check(invalid304.ErrorCode=="catalogue_schema_invalid"&&transitionNetwork.Metadata==1&&!transitionNetwork.SawEtag,"unconditional304 cannot revalidate no-store membership");
        transitionNetwork.NotModified=false;transitionNetwork.Ids=["9AAAAAAAAAAA"];
        var allowedAgain=await transitionService.RefreshXboxCatalogAsync();
        using(var disk=JsonDocument.Parse(File.ReadAllText(transitionPath)))
            Check(disk.RootElement.GetProperty("Snapshot").GetProperty("Games").EnumerateArray().All(g=>g.GetProperty("ProductId").GetString()!="9BBBBBBBBBBB"),"transient B must never migrate into later cacheable historical metadata");
        Check(allowedAgain.ErrorCode is null&&allowedAgain.Games.Count==1&&allowedAgain.SourceWarning!="transient_response_not_cached","new permitted metadata only is cacheable after transient policy");
        transitionNetwork.Ids=["9AAAAAAAAAAA","9BBBBBBBBBBB"];
        await transitionService.RefreshXboxCatalogAsync(true);
        using(var disk=JsonDocument.Parse(File.ReadAllText(transitionPath)))
            Check(disk.RootElement.GetProperty("Snapshot").GetProperty("Games").GetArrayLength()==2,"a product independently re-fetched under permitted policy may be persisted");
        var blockedNetwork=new Network{Block=new(TaskCreationOptions.RunContinuationsAsynchronously)};var once=new StoreXboxCatalog(Path.Combine(root,"once.json"),"IT","it-IT",new HttpClient(blockedNetwork),()=>now,Open);
        var one=once.RefreshXboxCatalogAsync(true);await blockedNetwork.Started.Task;var two=once.RefreshXboxCatalogAsync(true);var three=once.RefreshXboxCatalogAsync(true);blockedNetwork.Block.SetResult();await Task.WhenAll(one,two,three);Check(blockedNetwork.Feeds==1&&blockedNetwork.Metadata==1,"parallel callers share one complete refresh");
        var refreshCount=0;await using(var startup=new StoreStartupRefresh(new EmptyLibrary(),publicCatalogRefresh:async token=>{Interlocked.Increment(ref refreshCount);await service.RefreshXboxCatalogAsync(false,token);}))
        {
            startup.ObserveSteamStart(new(10,100));startup.ObserveSteamStart(new(10,100));await startup.WhenIdleAsync();Check(refreshCount==1,"public catalogue refresh happens once on real bound Steam start even without account");
            startup.ObserveSteamStart(new(10,101));await startup.WhenIdleAsync();Check(refreshCount==2,"new Steam birth receives distinct startup refresh");startup.ObserveSteamStart(new(0,101));await startup.WhenIdleAsync();Check(refreshCount==2,"unknown process never schedules background network");
        }
        Check(opened.Count==1,"startup/public refresh never invokes official UI");
        var baseline=await Pipe(root,null,true);Check(!baseline.GetProperty("ok").GetBoolean()&&baseline.GetProperty("errorCode").GetString()=="method_unavailable","actual unchanged host dispatcher without extension refuses Xbox method");
        var wired=await Pipe(root,service,true);Check(wired.GetProperty("ok").GetBoolean()&&wired.GetProperty("data").GetProperty("plan").GetString()=="pc","actual pinned host dispatcher exposes registered public snapshot");
        var refused=await Pipe(root,service,false);Check(!refused.GetProperty("ok").GetBoolean()&&refused.GetProperty("errorCode").GetString()=="client_not_authorized","new Xbox methods do not bypass pinned client auth");
        Console.WriteLine($"PASS {count} actual Xbox cache/metadata/handoff checks; no network, account, OS handler or UI execution");
    }
    static async Task<JsonElement> Pipe(string root,IStoreProtocolExtension? extension,bool authorized,string method="xbox-catalog")
    {
        await using var engine=new StoreQueueEngine([],new StoreInstallFileSystem(),Path.Combine(root,Guid.NewGuid()+".json"));
        var name="Playhub-Xbox-Test-"+Guid.NewGuid().ToString("N");using var stop=new CancellationTokenSource();
        var server=new StorePipeServer(engine,_=>authorized,extension==null?null:[extension],pipeName:name);var serving=server.RunAsync(stop.Token);
        try
        {
            await using var pipe=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new{protocol=1,id=Guid.NewGuid(),method,args=new{}}));
            var line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));using var doc=JsonDocument.Parse(line!);return doc.RootElement.Clone();
        }
        finally{stop.Cancel();try{await serving;}catch(OperationCanceledException){}}
    }
    sealed class EmptyLibrary:IStoreLibraryService
    {
        public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new StoreLibrarySnapshot(1,DateTimeOffset.UtcNow,[],[],[]));
        public Task<StoreConnectSession> BeginConnectAsync(StoreKind store,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreCommandResult> CompleteConnectAsync(Guid sessionId,string authorizationCode,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreCommandResult> DisconnectAsync(StoreKind store,long revision,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreCommandResult> RefreshAsync(StoreKind store,long revision,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey key,long revision,string? destination=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest request,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreCommandResult> ControlAsync(StoreJobCommand command,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    sealed class Network:HttpMessageHandler
    {
        internal int Calls,Feeds,Metadata;internal bool Partial,FailFeed,BadId,Duplicate,Empty,NoStore,MetadataNoStore,NotModified,SawEtag;internal string[] Ids=["9AAAAAAAAAAA","9BBBBBBBBBBB"];
        internal TaskCompletionSource? Block;internal TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;
            if(request.RequestUri!.Host=="catalog.gamepass.com")
            {
                Feeds++;Check(request.RequestUri.Query.Contains("platformContext=pc")&&request.RequestUri.Query.Contains("subscriptionContext=cfq7ttc0kgq8"),"request is explicit public PC plan, not account token");Check(request.Headers.Authorization==null,"no user auth or client spoofing");Started.TrySetResult();if(Block!=null)await Block.Task.WaitAsync(token);
                SawEtag=request.Headers.Contains("If-None-Match");
                if(FailFeed)return new(HttpStatusCode.ServiceUnavailable);
                var rows=new List<object>{new{siglId="609d944c-d395-4c0a-9ea4-e9f39b52c1ad",title="PC collection"}};
                if(!Empty)foreach(var id in Ids)rows.Add(new{id=BadId?"invalid&uri":id});if(Duplicate)rows.Add(new{id=Ids[0]});
                var response=Json(rows);if(NotModified)response.StatusCode=HttpStatusCode.NotModified;response.Headers.ETag=new EntityTagHeaderValue("\"one\"");response.Headers.CacheControl=new(){MaxAge=TimeSpan.FromMinutes(10),NoStore=NoStore};return response;
            }
            if(request.RequestUri.Host=="displaycatalog.mp.microsoft.com")
            {
                Metadata++;var ids=Uri.UnescapeDataString(request.RequestUri.Query.Split('&').Single(v=>v.StartsWith("?bigIds=")).Split('=')[1]).Split(',');if(Partial)ids=ids.Take(Math.Max(0,ids.Length-1)).ToArray();
                var products=ids.Select(id=>new{ProductId=id,ProductKind="Game",LocalizedProperties=new[]{new{ProductTitle="Title "+id,Images=new[]{new{ImagePurpose="Poster",Uri=id=="9AAAAAAAAAAA"?"//store-images.s-microsoft.com/image/fixture":"https://foreign.invalid/image"}}}},DisplaySkuAvailabilities=new[]{new{Availabilities=new[]{new{Conditions=new{ClientConditions=new{AllowedPlatforms=new[]{new{PlatformName=id=="9AAAAAAAAAAA"?"Windows.Desktop":"Windows.Xbox"}}}}}}}}});
                var response=Json(new{Products=products});response.Headers.CacheControl=new(){MaxAge=TimeSpan.FromMinutes(5),NoStore=MetadataNoStore};return response;
            }
            throw new Exception("unexpected fake HTTP host");
        }
        static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    }
}
