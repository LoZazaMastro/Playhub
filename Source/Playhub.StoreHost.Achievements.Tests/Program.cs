using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

static class Program
{
    static int count;
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);count++;}
    static JsonElement Json(string text)=>JsonDocument.Parse(text).RootElement.Clone();
    static readonly StoreCapabilities Caps=new(StoreInstallKind.Direct,true,true,true,true,true);
    static StoreLibraryGame Game(StoreKind store=StoreKind.Epic,long revision=3)=>new(new(store,"Fixture"),"Fixture game",revision,Caps,true,true,@"F:\Games\Fixture");
    static StoreAchievement Row(string name="One")=>new(name,name,"Description",null,null,false,true,1700000000,12.5);
    static async Task Main()
    {
        await CacheChecks();NormalizationChecks();await ProviderChecks();
        Console.WriteLine($"{count} achievement cache/provider/normalization checks PASS; fixture HTTP and temporary private credentials, no live service requests");
    }
    static async Task CacheChecks()
    {
        string directory=Path.Combine(Path.GetTempPath(),"Playhub-Achievements-Checks-"+Guid.NewGuid().ToString("N"));
        try
        {
            var clock=DateTimeOffset.UtcNow;var cache=new StoreAchievementCache(directory,()=>clock);var game=Game();int reads=0;bool current=true;
            Task<bool> Current(CancellationToken _)=>Task.FromResult(current);
            Task<IReadOnlyList<StoreAchievement>> Read(CancellationToken _){reads++;return Task.FromResult<IReadOnlyList<StoreAchievement>>([Row()]);}
            Check(await cache.ReadAsync("account-A",game,"it-IT",false,true,Read,Current,default)is null&&reads==0,"cached-only miss must not start HTTP");
            var first=await cache.ReadAsync("account-A",game,"it-IT",false,false,Read,Current,default);
            Check(first?.Achievements.Count==1&&reads==1,"initial fetch");
            cache=new(directory,()=>clock);clock=clock.AddHours(2);
            Check((await cache.ReadAsync("account-A",game,"it-IT",false,true,Read,Current,default))?.Achievements[0].Key=="One"&&reads==1,"restart and game-stop retain disk progress");
            Check(await cache.ReadAsync("account-B",game,"it-IT",false,true,Read,Current,default)is null,"different account cannot read progress");
            Check(await cache.ReadAsync("account-A",Game(revision:4),"it-IT",false,true,Read,Current,default)is null,"different account revision cannot read progress");
            Check(await cache.ReadAsync("account-A",game,"en-US",false,true,Read,Current,default)is null,"different locale cache miss");
            await cache.ReadAsync("account-A",game,"it-IT",true,false,Read,Current,default);Check(reads==2,"explicit refresh bypasses cache");
            var release=new TaskCompletionSource<IReadOnlyList<StoreAchievement>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IReadOnlyList<StoreAchievement>> Pending(CancellationToken _){reads++;return release.Task;}
            var one=cache.ReadAsync("account-A",game,"it-IT",true,false,Pending,Current,default);
            var two=cache.ReadAsync("account-A",game,"it-IT",true,false,Pending,Current,default);release.SetResult([Row("Two")]);
            await Task.WhenAll(one,two);Check(reads==3&&ReferenceEquals(one.Result,two.Result),"concurrent reads share one request");
            var changed=new TaskCompletionSource<IReadOnlyList<StoreAchievement>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var late=cache.ReadAsync("account-A",game,"it-IT",true,false,_=>changed.Task,Current,default);current=false;changed.SetResult([Row("Wrong")]);
            try{await late;throw new Exception("revoked identity accepted");}catch(StoreOperationException e){Check(e.Code=="account_changed","account exchange fences pending result");}
            current=true;Check((await cache.ReadAsync("account-A",game,"it-IT",false,true,Read,Current,default))?.Achievements[0].Key=="Two","failed refresh preserves last confirmed payload");
            clock=clock.AddDays(31);Check((await cache.ReadAsync("account-A",game,"it-IT",false,true,Read,Current,default))?.Achievements[0].Key=="Two","old confirmed progress remains immediately available while a separate refresh updates it");
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    static void NormalizationChecks()
    {
        var defs=Json("""[{"achievement":{"name":"a","unlockedDisplayName":"First","lockedDescription":"Locked","unlockedDescription":"Earned","unlockedIconLink":"https://cdn1.epicgames.com/a.png","lockedIconLink":"https://cdn1.epicgames.com/b.png","rarity":{"percent":12.5},"hidden":false}},{"achievement":{"name":"b","lockedDisplayName":"Second","hidden":true,"rarity":{"percent":110}}}]""");
        var progress=Json("""[{"playerAchievement":{"achievementName":"a","unlocked":true,"unlockDate":"2024-01-01T00:00:00Z"}}]""");
        var epic=StoreAchievementNormalization.Epic(defs,progress);
        Check(epic.Count==2&&epic[0].Unlocked&&epic[0].Description=="Earned"&&epic[0].Rarity==12.5,"Epic definitions merge actual account progress");
        Check(!epic[1].Unlocked&&epic[1].Hidden&&epic[1].Rarity is null,"missing Epic state is locked; invalid rarity omitted");
        var gog=StoreAchievementNormalization.Gog(Json("""{"achievement_key":"g","name":"GOG","date_unlocked":"2024-02-03T00:00:00Z","visible":false,"rarity":4.2,"image_url_unlocked":"https://images.gog-statics.com/abc.png"}"""));
        Check(gog is {Unlocked:true,Hidden:true,Rarity:4.2}&&gog.UnlockedAt>0,"GOG unlock date and visibility");
        Check(StoreAchievementNormalization.Gog(Json("""{"achievement_key":"locked","date_unlocked":null}""")) is {Unlocked:false,UnlockedAt:null},"GOG null date remains locked");
        foreach(string bad in new[]{"http://cdn1.epicgames.com/a","https://cdn1.epicgames.com.evil.test/a","https://token@cdn1.epicgames.com/a","https://cdn1.epicgames.com:444/a","file:///C:/secret"})
            Check(StoreAchievementNormalization.Image(bad,StoreKind.Epic)is null,"unsafe icon refused");
        Check(!StoreAchievementCache.Valid([Row(),Row()],StoreKind.Epic),"duplicate stable IDs refused");
    }
    static async Task ProviderChecks()
    {
        string state=Path.Combine(Path.GetTempPath(),"Playhub-Achievements-Providers-"+Guid.NewGuid().ToString("N"));
        string bundle=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Playhub.StoreHost/Runtime"));
        try
        {
            var epicHttp=new FixtureHttp();var epic=new StoreCliProvider(StoreKind.Epic,bundle,state,Environment.ProcessPath!,new HttpClient(epicHttp));epic.AchievementHttp=new(epicHttp);
            StoreCredentialVault.Write(Path.Combine(state,"epic/credentials.dpapi"),"""{"account_id":"fixture-account","access_token":"fixture-access","refresh_token":"fixture-refresh"}""");
            string native=Path.Combine(state,"epic/native");Directory.CreateDirectory(Path.Combine(native,"metadata"));
            File.WriteAllText(Path.Combine(native,"metadata/Fixture.json"),"""{"app_name":"Fixture","metadata":{"namespace":"fixture-sandbox"}}""");
            epicHttp.Enqueue("https://store.epicgames.com/graphql","""{"data":{"Achievement":{"productAchievementsRecordBySandbox":{"achievements":[{"achievement":{"name":"one","unlockedDisplayName":"One"}}]}}}}""");
            epicHttp.Enqueue("https://store.epicgames.com/graphql","""{"data":{"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":{"records":[{"playerAchievements":[{"playerAchievement":{"achievementName":"one","unlocked":true}}]}]}}}}""");
            var rows=await epic.ReadAchievementsAsync(Game(),"it-IT",default);
            Check(rows is [{Unlocked:true}],"Epic provider uses current OAuth account and sandbox");
            Check(epicHttp.Requests.Count==2&&epicHttp.Requests.All(r=>r.Auth=="Bearer fixture-access")&&epicHttp.Requests[0].Body.Contains("it-IT"),"Epic token and requested language reach only pinned endpoint");
            Check(!File.Exists(Path.Combine(native,"user.json")),"Epic native plaintext auth removed after query");
            epicHttp.Enqueue("https://store.epicgames.com/graphql","{}",HttpStatusCode.Found);
            try{await epic.ReadAchievementsAsync(Game(),"it-IT",default);throw new Exception("redirect accepted");}catch(StoreOperationException e){Check(e.Code=="achievements_unavailable","Epic redirects are refused");}

            var gogHttp=new FixtureHttp();var gog=new StoreCliProvider(StoreKind.Gog,bundle,state,Environment.ProcessPath!,new HttpClient(gogHttp));gog.AchievementHttp=new(gogHttp);
            StoreCredentialVault.Write(Path.Combine(state,"gog/credentials.dpapi"),JsonSerializer.Serialize(new Dictionary<string,object>{["46899977096215655"]=new{user_id="12345",access_token="fixture-account-access",refresh_token="fixture-game-refresh",loginTime=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),expires_in=3600}}));
            byte[] compressed;using(var output=new MemoryStream()){using(var z=new ZLibStream(output,CompressionMode.Compress,true))z.Write(Encoding.UTF8.GetBytes("{\"clientId\":\"1234\",\"clientSecret\":\"fixture-public-secret\"}"));compressed=output.ToArray();}
            string hash=Convert.ToHexString(MD5.HashData(compressed)).ToLowerInvariant(),link=$"https://gog-cdn-fastly.gog.com/content-system/v2/meta/{hash[..2]}/{hash.Substring(2,2)}/{hash}";
            gogHttp.Enqueue("https://content-system.gog.com/products/Fixture/os/windows/builds?generation=2",JsonSerializer.Serialize(new{items=new[]{new{link}}}));gogHttp.EnqueueBytes(link,compressed);
            gogHttp.EnqueuePrefix("https://auth.gog.com/token?","""{"access_token":"fixture-game-access"}""");
            gogHttp.Enqueue("https://gameplay.gog.com/clients/1234/users/12345/achievements","""{"items":[{"achievement_key":"g","name":"GOG","date_unlocked":null}],"page_token":"0","total_count":1}""");
            var gogRows=await gog.ReadAchievementsAsync(Game(StoreKind.Gog),"it-IT",default);
            Check(gogRows is [{Key:"g",Unlocked:false}],"GOG manifest-token-paginated achievement flow");
            Check(gogHttp.Requests[^1].Auth=="Bearer fixture-game-access"&&gogHttp.Requests[^2].Auth is null,"GOG refresh material never leaks to gameplay/other endpoints");
            Check(gogHttp.Requests.Count==4,"GOG zero continuation token ends paging");
        }
        finally{if(Directory.Exists(state))Directory.Delete(state,true);}
    }
    sealed class FixtureHttp:HttpMessageHandler
    {
        internal readonly List<(string Url,string? Auth,string Body)> Requests=[];
        readonly Queue<(string Url,bool Prefix,byte[] Body,HttpStatusCode Code)> replies=[];
        internal void Enqueue(string url,string body,HttpStatusCode code=HttpStatusCode.OK)=>replies.Enqueue((url,false,Encoding.UTF8.GetBytes(body),code));
        internal void EnqueuePrefix(string url,string body)=>replies.Enqueue((url,true,Encoding.UTF8.GetBytes(body),HttpStatusCode.OK));
        internal void EnqueueBytes(string url,byte[] bytes)=>replies.Enqueue((url,false,bytes,HttpStatusCode.OK));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string url=request.RequestUri!.AbsoluteUri;var row=replies.Dequeue();
            if(row.Prefix?!url.StartsWith(row.Url,StringComparison.Ordinal):url!=row.Url)throw new Exception("Unexpected fixture endpoint");
            Requests.Add((url,request.Headers.Authorization?.ToString(),request.Content is null?"":await request.Content.ReadAsStringAsync(token)));
            return new(row.Code){Content=new ByteArrayContent(row.Body),RequestMessage=request};
        }
    }
}
