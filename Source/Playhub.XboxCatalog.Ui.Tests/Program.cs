// SPDX-License-Identifier: MIT. Actual view controller, inert service only.
using Playhub.StoreLibrary;
static class Checks
{
    static int count;
    static void Check(bool value,string reason){count++;if(!value)throw new Exception(reason);}
    static StoreXboxCatalogSnapshot Snapshot(long revision=1) => new(revision,"revision-"+revision,
        "xbox-web-gamepass-pc-sigl-v3:609d944c-d395-4c0a-9ea4-e9f39b52c1ad","IT","it-IT","pc","pc",
        DateTimeOffset.UtcNow.AddMinutes(-15),DateTimeOffset.UtcNow.AddMinutes(-5),true,
        [new("9AAAAAAAAAAA","Alpha",null,true,"desktop-confirmed"),
         new("9BBBBBBBBBBB","Beta",null,true,"not-confirmed"),
         new("9CCCCCCCCCCC","Console",null,true,"not-desktop"),
         new("9DDDDDDDDDDD","Removed",null,false,"desktop-confirmed")]);
    static async Task Main()
    {
        var fake=new Fake();using var owner=new XboxGamePassController(fake);
        Check(owner.Snapshot is null&&!owner.Busy,"initial state is unknown, not owned empty account");
        await owner.ReadAsync();Check(owner.Snapshot==fake.Value&&fake.Reads==1&&fake.Refreshes==0,"activation starts from read-only cache");
        Check(owner.VisibleGames(null).Select(g=>g.Title).SequenceEqual(new[]{"Alpha","Beta"}),"exclude removed and explicitly non-PC products, retain unknown for official confirmation");
        Check(owner.VisibleGames("LPH").Single().ProductId=="9AAAAAAAAAAA","search actual title case insensitively");
        var held=owner.Snapshot;fake.Throw=true;await owner.RefreshAsync();
        Check(owner.Snapshot==held&&owner.ErrorKey=="GamePass.RefreshFailed"&&!owner.Busy,"offline refresh retains previous complete cache");fake.Throw=false;
        fake.Value=Snapshot(2) with{ErrorCode="upstream unavailable"};await owner.RefreshAsync();
        Check(owner.Snapshot!.Revision==2&&owner.ErrorKey=="GamePass.RefreshFailed","host retained-cache warning remains explicit");
        fake.Value=Snapshot(1);await owner.ReadAsync();Check(owner.Snapshot!.Revision==2,"older revision cannot overwrite current view");
        fake.Value=Snapshot(3) with{IsStale=false,ErrorCode=null};await owner.RefreshIfStaleAsync();
        Check(owner.Snapshot!.Revision==3&&owner.ErrorKey is null,"successful later refresh clears warning");
        Check(await owner.OpenAsync("9AAAAAAAAAAA",3)&&fake.Opened.Count==1&&owner.NoticeKey=="GamePass.OpenRequested","only confirmed command reports page requested");
        Check(owner.Snapshot!.Games.All(g=>g.InstalledState=="not-checked"&&g.EntitlementState=="not-checked"),"successful handoff never rewrites installed or entitlement");
        Check(fake.Opened[0].ProductId=="9AAAAAAAAAAA"&&fake.Opened[0].ExpectedRevision==3&&fake.Opened[0].RequestId!=Guid.Empty,"exact identity, revision and one-shot request passed");
        foreach(var id in new[]{"9CCCCCCCCCCC","9DDDDDDDDDDD","foreign","9AAAAAAAAAAA&x=1"})
            Check(!await owner.OpenAsync(id,3)&&fake.Opened.Count==1,"noncurrent/foreign product cannot hand off");
        Check(!await owner.OpenAsync("9AAAAAAAAAAA",2)&&fake.Opened.Count==1,"confirmation of an older catalogue does not open a different current revision");
        fake.Mode="unknown";Check(!await owner.OpenAsync("9AAAAAAAAAAA",3)&&owner.ErrorKey=="GamePass.OpenUnknown"&&owner.NoticeKey is null,"uncertain outcome never reports success");
        var calls=fake.Opened.Count;await Task.Delay(5);Check(fake.Opened.Count==calls,"no automatic retry of uncertain official opening");
        fake.Mode="foreign";Check(!await owner.OpenAsync("9AAAAAAAAAAA",3)&&owner.ErrorKey=="GamePass.OpenUnknown","foreign receipt cannot grant success");
        fake.Mode="installed";Check(!await owner.OpenAsync("9AAAAAAAAAAA",3)&&owner.ErrorKey=="GamePass.OpenUnknown","claimed installation is not accepted as page receipt");
        fake.Mode="refused";Check(!await owner.OpenAsync("9AAAAAAAAAAA",3)&&owner.ErrorKey=="GamePass.OpenFailed","explicit refusal is distinct from outcome unknown");
        foreach(var bad in new[]{Snapshot(-1),Snapshot(8) with{Plan="ultimate"},Snapshot(8) with{Market="unknown"},Snapshot(8) with{Platform="console"},Snapshot(8) with{Source="other"},Snapshot(8) with{Games=[new("bad","Title",null,true,"desktop-confirmed")]},Snapshot(8) with{Games=[new("9AAAAAAAAAAA","Title",null,true,"desktop-confirmed","owned")]},Snapshot(8) with{Games=[new("9AAAAAAAAAAA","Title",null,true,"desktop-confirmed",InstalledState:"installed")]},Snapshot(8) with{Games=[Snapshot().Games[0],Snapshot().Games[0]]}})
        {Check(!XboxGamePassController.Valid(bad),"malformed/claimed ownership DTO refused");fake.Value=bad;await owner.ReadAsync();Check(owner.Snapshot!.Revision==3,"malformed data preserves prior view");}
        var deferred=new Fake { Block=new(TaskCreationOptions.RunContinuationsAsynchronously) };using var pending=new XboxGamePassController(deferred);
        var old=pending.ReadAsync();await pending.ReadAsync();Check(deferred.Reads==1&&pending.Busy,"reads single-flight, no duplicate network or host calls");
        pending.CancelPending();Check(!pending.Busy&&pending.ErrorKey is null,"page exit cancels local wait without declaring error or canceling installs");
        deferred.Block.SetResult(Snapshot(10));await old;Check(pending.Snapshot is null,"late result after page exit cannot resurrect catalog state");
        deferred.Block=null;deferred.Value=Snapshot(11);await pending.ReadAsync();Check(pending.Snapshot?.Revision==11,"later page activation reads fresh state");
        deferred.Block=new(TaskCreationOptions.RunContinuationsAsynchronously);var final=pending.ReadAsync();pending.Dispose();deferred.Block.SetResult(Snapshot(12));await final;Check(pending.Snapshot?.Revision==11,"disposal rejects late results");
        Check(fake.Opened.Count==calls+3,"read/refresh/search/cancel never launches official UI");
        foreach(var url in new[]{"https://store-images.s-microsoft.com/image/fixture?size=200","https://store-images.microsoft.com/fixture","https://images-eds-ssl.xboxlive.com/fixture"})
            Check(Playhub.Services.StoreLibraryArtworkCache.Allowed(StoreKind.Xbox,url,out _),"Microsoft image origin accepted with its CDN query");
        foreach(var url in new[]{"http://store-images.s-microsoft.com/image","https://store-images.s-microsoft.com.evil.test/image","https://user@store-images.s-microsoft.com/image","https://store-images.s-microsoft.com:444/image","https://store-images.s-microsoft.com/image#fragment","file:///C:/data.png","https://foreign.test/image"})
            Check(!Playhub.Services.StoreLibraryArtworkCache.Allowed(StoreKind.Xbox,url,out _),"foreign/scheme/port/userinfo image refused");
        Check(Playhub.Services.StoreLibraryArtworkCache.Allowed(StoreKind.Epic,"https://cdn1.epicgames.com/image.png",out _)
            &&Playhub.Services.StoreLibraryArtworkCache.Allowed(StoreKind.Epic,"https://cdn1.epicgames.com/image.png?resize=600x900",out _),"official Epic image query is retained");
        foreach(var store in new[]{StoreKind.Amazon,StoreKind.Gog})
        {
            string host=store==StoreKind.Amazon?"m.media-amazon.com":"images.gog.com";
            Check(Playhub.Services.StoreLibraryArtworkCache.Allowed(store,"https://"+host+"/image.jpg?size=600",out _),"official store image CDN with query accepted");
            Check(!Playhub.Services.StoreLibraryArtworkCache.Allowed(store,"https://"+host+".evil.test/image.jpg",out _),"foreign suffix never becomes an image CDN");
            Check(!Playhub.Services.StoreLibraryArtworkCache.Allowed(store,"https://user@"+host+"/image.jpg",out _),"embedded credentials never become an image CDN");
        }
        foreach(var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,"Assets/Localization"),"*.json"))
        {
            using var locale=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
            Check(locale.RootElement.EnumerateObject().Count(k=>k.Name.StartsWith("GamePass."))==15,"every shipped language has complete GamePass text");
            Check(locale.RootElement.GetProperty("StoreLibrary.BrowseGamePass").GetString() is {Length:>0}
                &&locale.RootElement.GetProperty("GamePass.Count").GetString()!.Contains("{0}")
                &&locale.RootElement.GetProperty("GamePass.Updated").GetString()!.Contains("{0}"),"localized action and formatting placeholders preserved");
        }
        Console.WriteLine($"PASS {count} actual Xbox catalogue controller checks; no network, window, account or handler");
    }
    sealed class Fake:IStoreXboxCatalogService
    {
        internal StoreXboxCatalogSnapshot Value=Snapshot();internal int Reads,Refreshes;internal bool Throw;internal string Mode="ok";
        internal TaskCompletionSource<StoreXboxCatalogSnapshot>? Block;internal List<StoreXboxHandoffRequest> Opened=[];
        public Task<StoreXboxCatalogSnapshot> GetXboxCatalogAsync(CancellationToken token=default)
        {Reads++;return Block?.Task??Task.FromResult(Value);}
        public Task<StoreXboxCatalogSnapshot> RefreshXboxCatalogAsync(bool force=false,CancellationToken token=default)
        {Refreshes++;if(Throw)throw new IOException("fake offline");return Task.FromResult(Value);}
        public Task<StoreXboxHandoffResult> OpenXboxInstallPageAsync(StoreXboxHandoffRequest request,CancellationToken token=default)
        {
            Opened.Add(request);return Task.FromResult(Mode switch
            {
                "unknown"=>new StoreXboxHandoffResult(false,"outcome-unknown","unknown",request.ProductId,request.RequestId),
                "foreign"=>new(true,"official-page-requested",null,"9BBBBBBBBBBB",request.RequestId),
                "installed"=>new(true,"official-page-requested",null,request.ProductId,request.RequestId,InstallState:"installed"),
                "refused"=>new(false,"not-opened","refused",request.ProductId,request.RequestId),
                _=>new(true,"official-page-requested",null,request.ProductId,request.RequestId)
            });
        }
    }
}
