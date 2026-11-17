// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

// Original public-catalogue cache. This is not an account or entitlement provider.
// Endpoint facts: Xbox's own xgpcatPopulate-2025.js, allgamespc/pc context.
public sealed class StoreXboxCatalog : IStoreXboxCatalogService, IStoreProtocolExtension
{
    private const string Sigl = "609d944c-d395-4c0a-9ea4-e9f39b52c1ad";
    private const string Source = "xbox-web-gamepass-pc-sigl-v3:" + Sigl;
    private const int MaximumIds = 3000, MaximumResponse = 8 * 1024 * 1024;
    private readonly object sync = new();
    private readonly HttpClient http;
    private readonly string path, market, language;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<Uri,CancellationToken,Task<bool>> open;
    private readonly Dictionary<Guid, RequestRecord> requests = [];
    private StoreXboxCatalogSnapshot snapshot;
    private string? etag;
    private bool metadataMayPersist = true;
    private StoreXboxCatalogSnapshot Empty(long revision) => new(revision,"",Source,market,language,"pc","pc",null,null,true,[],"catalogue_not_loaded");
    private Task<StoreXboxCatalogSnapshot>? refreshing;
    private sealed record RequestRecord(string ProductId,long Revision,StoreXboxHandoffResult Result);
    private sealed record Disk(int Version,string Source,string Market,string Language,string? Etag,
        StoreXboxCatalogSnapshot Snapshot,Dictionary<Guid,RequestRecord> Requests);

    public StoreXboxCatalog(string cachePath,string market,string language,HttpClient? client=null,
        Func<DateTimeOffset>? utcNow=null,Func<Uri,CancellationToken,Task<bool>>? officialPage=null)
    {
        if(!Regex.IsMatch(market,"^[A-Z]{2}$")||!Regex.IsMatch(language,"^[a-z]{2}-[A-Z]{2}$"))throw new ArgumentException("catalogue_locale_invalid");
        this.market=market;this.language=language;path=Path.GetFullPath(cachePath);
        http=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(20)};
        clock=utcNow??(()=>DateTimeOffset.UtcNow);open=officialPage??OpenOfficial;
        snapshot=Empty(0);
        Load();
    }
    public Task<StoreXboxCatalogSnapshot> GetXboxCatalogAsync(CancellationToken cancellationToken=default)
    { cancellationToken.ThrowIfCancellationRequested();lock(sync)return Task.FromResult(Current()); }
    private StoreXboxCatalogSnapshot Current()=>snapshot with { IsStale=snapshot.ExpiresAt==null||snapshot.ExpiresAt<=clock() };

    public async Task<StoreXboxCatalogSnapshot> RefreshXboxCatalogAsync(bool force=false,CancellationToken cancellationToken=default)
    {
        Task<StoreXboxCatalogSnapshot> task;
        lock(sync)
        {
            if(!force&&snapshot.ExpiresAt>clock())return Current();
            if(refreshing!=null)task=refreshing;
            else
            {
                task=Task.Run(RefreshOwnedAsync);refreshing=task;
                _=task.ContinueWith(_=>{lock(sync)if(ReferenceEquals(refreshing,task))refreshing=null;},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
        }
        return await task.WaitAsync(cancellationToken);
    }
    private async Task<StoreXboxCatalogSnapshot> RefreshOwnedAsync()
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(40));var token=deadline.Token;
        try
        {
            string? heldTag;bool hadSnapshot;lock(sync){heldTag=etag;hadSnapshot=snapshot.ValidatedAt!=null;}
            var url=$"https://catalog.gamepass.com/sigls/v3?id={Sigl}&language={Uri.EscapeDataString(language)}&market={market}&platformContext=pc&subscriptionContext=cfq7ttc0kgq8";
            using var request=new HttpRequestMessage(HttpMethod.Get,url);
            if(heldTag!=null&&hadSnapshot)request.Headers.TryAddWithoutValidation("If-None-Match",heldTag);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            var noStore=response.Headers.CacheControl?.NoStore==true;
            string[] ids;
            if(response.StatusCode==HttpStatusCode.NotModified)
            {
                lock(sync)
                {
                    if(!hadSnapshot||heldTag==null||!metadataMayPersist||snapshot.ValidatedAt==null)throw Invalid();
                    ids=snapshot.Games.Where(g=>g.IsCurrentMember).Select(g=>g.ProductId).ToArray();
                    if(ids.Length==0)throw Invalid();
                }
                // A SIGL 304 validates membership only. Rehydrate actual product
                // metadata instead of inventing fresh title/platform evidence.
            }
            else
            {
                if(response.StatusCode!=HttpStatusCode.OK)throw new StoreOperationException("catalogue_network_unavailable");
                ids=ReadIds(await Read(response,MaximumResponse,token));
            }
            var products=new Dictionary<string,StoreXboxCatalogGame>(StringComparer.Ordinal);
            // Complete hydration, bounded fan-out. A partial failure keeps the last full cache.
            using var slots=new SemaphoreSlim(3,3);var productSync=new object();var metadataLifetime=TimeSpan.FromHours(1);
            await Task.WhenAll(ids.Chunk(50).Select(async batch=>
            {
                await slots.WaitAsync(token);
                try
                {
                    var metadata=$"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={string.Join(',',batch)}&market={market}&languages={Uri.EscapeDataString(language)}";
                    using var detail=await http.GetAsync(metadata,HttpCompletionOption.ResponseHeadersRead,token);
                    if(detail.StatusCode!=HttpStatusCode.OK)throw new StoreOperationException("catalogue_metadata_unavailable");
                    var parsed=ReadProducts(await Read(detail,MaximumResponse,token),batch);
                    IReadOnlyDictionary<string,StoreXboxInstallProvider> providers=new Dictionary<string,StoreXboxInstallProvider>();
                    try{providers=await StoreXboxInstallProviders.ReadAsync(http,batch,market,language,token);}
                    catch(Exception error)when(error is not OutOfMemoryException and not OperationCanceledException){/* Keep catalogue visible; absent evidence stays unknown. */}
                    parsed=parsed.Select(game=>providers.TryGetValue(game.ProductId,out var provider)?game with{InstallProvider=provider.Provider,InstallProviderId=provider.ExtensionId}:game with{InstallProvider="unknown"}).ToArray();
                    lock(productSync)
                    {
                        if(detail.Headers.CacheControl?.NoStore==true)noStore=true;
                        var age=Lifetime(detail);if(age<metadataLifetime)metadataLifetime=age;
                        foreach(var game in parsed)if(!products.TryAdd(game.ProductId,game))throw Invalid();
                    }
                }
                finally{slots.Release();}
            }));
            if(products.Count!=ids.Length)throw new StoreOperationException("catalogue_metadata_incomplete");
            var current=ids.Select(id=>products[id]).ToArray();
            var sourceRevision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Source,market,language,Games=current}))));
            lock(sync)
            {
                // Retain historical metadata separately from current membership. No
                // installed records, account grants or Steam shortcuts are deleted.
                // Historical records may enter a disk cache only if their previous
                // composed response permitted storage. Never launder a no-store
                // record through a later cacheable response's removed history.
                var old=(metadataMayPersist?snapshot.Games:Array.Empty<StoreXboxCatalogGame>()).Where(g=>!products.ContainsKey(g.ProductId)).Select(g=>g with{IsCurrentMember=false});
                var all=current.Concat(old).Take(10000).ToArray();var now=clock();var age=noStore?TimeSpan.Zero:Lifetime(response);if(metadataLifetime<age)age=metadataLifetime;
                var next=new StoreXboxCatalogSnapshot(snapshot.SourceRevision==sourceRevision?snapshot.Revision:snapshot.Revision+1,sourceRevision,Source,market,language,"pc","pc",now,now+age,false,all,null,
                    noStore?"transient_response_not_cached":response.Headers.Contains("Warning")?"upstream_legacy_route_warning":"public_catalogue_not_entitlement_evidence");
                var prior=snapshot;var priorTag=etag;var priorPermission=metadataMayPersist;
                snapshot=next;metadataMayPersist=!noStore;
                etag=noStore?null:response.Headers.ETag?.ToString()??(response.StatusCode==HttpStatusCode.NotModified?heldTag:null);
                try{Save();}catch{snapshot=prior;etag=priorTag;metadataMayPersist=priorPermission;throw;}
                return Current();
            }
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {
            lock(sync){snapshot=snapshot with{ErrorCode=error is StoreOperationException operation?operation.Code:error is OperationCanceledException?"catalogue_refresh_timeout":"catalogue_refresh_failed"};return Current();}
        }
    }
    private static string[] ReadIds(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Array||root.GetArrayLength()<2||root.GetArrayLength()>MaximumIds+1)throw Invalid();
        var seen=new HashSet<string>(StringComparer.Ordinal);var ids=new List<string>();var metadata=0;
        foreach(var row in root.EnumerateArray())
        {
            if(row.TryGetProperty("siglId",out var sigl))
            {if(++metadata!=1||sigl.GetString()!=Sigl)throw Invalid();continue;}
            if(!row.TryGetProperty("id",out var property)||property.ValueKind!=JsonValueKind.String)throw Invalid();
            var id=property.GetString()!;if(!ValidId(id)||!seen.Add(id))throw Invalid();ids.Add(id);
        }
        if(metadata!=1||ids.Count==0)throw Invalid();return ids.ToArray();
    }
    private static StoreXboxCatalogGame[] ReadProducts(byte[] bytes,string[] requested)
    {
        using var doc=JsonDocument.Parse(bytes);var rows=doc.RootElement.GetProperty("Products");if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()!=requested.Length)throw new StoreOperationException("catalogue_metadata_incomplete");
        var remaining=new HashSet<string>(requested,StringComparer.Ordinal);var result=new List<StoreXboxCatalogGame>();
        foreach(var row in rows.EnumerateArray())
        {
            var id=row.GetProperty("ProductId").GetString()!;if(!remaining.Remove(id)||!ValidId(id))throw Invalid();
            if(row.GetProperty("ProductKind").GetString()!="Game")throw new StoreOperationException("catalogue_product_type_changed");
            var localized=row.GetProperty("LocalizedProperties");if(localized.ValueKind!=JsonValueKind.Array||localized.GetArrayLength()==0)throw Invalid();
            var title=localized[0].GetProperty("ProductTitle").GetString();if(string.IsNullOrWhiteSpace(title)||title.Length>2048)throw Invalid();
            string? artwork=null;
            if(localized[0].TryGetProperty("Images",out var images)&&images.ValueKind==JsonValueKind.Array)
                artwork=images.EnumerateArray().Where(StoreMetadataArtwork.XboxCoverPurpose).OrderBy(StoreMetadataArtwork.XboxCoverPriority).Select(i=>i.TryGetProperty("Uri",out var uri)?Artwork(uri.GetString()):null).OfType<string>().SelectMany(url=>StoreMetadataArtwork.CoverVariants(StoreKind.Xbox,url)).FirstOrDefault();
            var platforms=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(row.TryGetProperty("DisplaySkuAvailabilities",out var skus)&&skus.ValueKind==JsonValueKind.Array)
                foreach(var sku in skus.EnumerateArray())if(sku.TryGetProperty("Availabilities",out var availabilities)&&availabilities.ValueKind==JsonValueKind.Array)
                    foreach(var availability in availabilities.EnumerateArray())if(availability.TryGetProperty("Conditions",out var conditions)&&conditions.TryGetProperty("ClientConditions",out var client)&&client.ValueKind==JsonValueKind.Object&&client.TryGetProperty("AllowedPlatforms",out var allowed)&&allowed.ValueKind==JsonValueKind.Array)
                        foreach(var platform in allowed.EnumerateArray())if(platform.TryGetProperty("PlatformName",out var name)&&name.GetString() is { } text)platforms.Add(text);
            var pc=platforms.Contains("Windows.Desktop")?"desktop-confirmed":platforms.Count>0?"not-desktop":"not-confirmed";
            result.Add(new(id,title,artwork,true,pc));
        }
        if(remaining.Count!=0)throw Invalid();return result.ToArray();
    }
    private static string? Artwork(string? value)
    {
        if(value?.StartsWith("//",StringComparison.Ordinal)==true)value="https:"+value;
        if(value==null||value.Length>8192||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0)return null;
        return uri.Host is "store-images.s-microsoft.com" or "store-images.microsoft.com" or "images-eds-ssl.xboxlive.com"?uri.AbsoluteUri:null;
    }
    public async Task<StoreXboxHandoffResult> OpenXboxInstallPageAsync(StoreXboxHandoffRequest request,CancellationToken cancellationToken=default)
    {
        StoreXboxHandoffResult pending;
        lock(sync)
        {
            if(request.RequestId==Guid.Empty||!ValidId(request.ProductId))return Refused(request,"request_invalid");
            if(requests.TryGetValue(request.RequestId,out var previous))return previous.ProductId==request.ProductId&&previous.Revision==request.ExpectedRevision?previous.Result:Refused(request,"request_id_reused");
            var item=snapshot.Games.SingleOrDefault(g=>g.ProductId==request.ProductId&&g.IsCurrentMember);
            if(request.ExpectedRevision!=snapshot.Revision||item==null||!item.CanOpenOfficialPage)return Refused(request,"catalogue_changed_or_product_unavailable");
            if(requests.Count>=1024)return Refused(request,"handoff_journal_full");
            pending=new(false,"outcome-unknown","handoff_pending",request.ProductId,request.RequestId);
            requests.Add(request.RequestId,new(request.ProductId,request.ExpectedRevision,pending));
            try{Save();}catch{requests.Remove(request.RequestId);return Refused(request,"handoff_journal_unavailable");}
        }
        StoreXboxHandoffResult result;
        var invoked = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri=new Uri("ms-windows-store://pdp/?ProductId="+request.ProductId);
            invoked = true;
            var accepted=await open(uri,cancellationToken);
            result=new(accepted,accepted?"official-page-requested":"not-opened",accepted?null:"official-client-unavailable",request.ProductId,request.RequestId);
        }
        // Once the official client boundary was invoked, an exception cannot
        // prove that no page opened. Retain the persisted one-shot outcome.
        catch(OperationCanceledException){result=invoked?pending:Refused(request,"handoff_canceled");}
        catch(Exception error) when(error is not OutOfMemoryException){result=pending with { ErrorCode="official-client-outcome-unknown" };}
        lock(sync)
        {
            requests[request.RequestId]=new(request.ProductId,request.ExpectedRevision,result);
            try{Save();}catch{return pending;} // Never repeat an effect after uncertain persistence.
            return result;
        }
    }
    private static StoreXboxHandoffResult Refused(StoreXboxHandoffRequest request,string error)=>new(false,"not-opened",error,request.ProductId,request.RequestId);
    private static Task<bool> OpenOfficial(Uri uri,CancellationToken token)
    {token.ThrowIfCancellationRequested();Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true})?.Dispose();return Task.FromResult(true);}
    private static bool ValidId(string? value)=>value!=null&&Regex.IsMatch(value,"^[A-Z0-9]{12}$");
    private static TimeSpan Lifetime(HttpResponseMessage response)
    {
        var age=(response.Headers.CacheControl?.MaxAge??TimeSpan.FromMinutes(10))-(response.Headers.Age??TimeSpan.Zero);
        if(response.Headers.CacheControl?.NoStore==true||response.Headers.CacheControl?.NoCache==true)return TimeSpan.Zero;
        return age<TimeSpan.Zero?TimeSpan.Zero:age>TimeSpan.FromHours(1)?TimeSpan.FromHours(1):age;
    }
    private static async Task<byte[]> Read(HttpResponseMessage response,int maximum,CancellationToken token)
    {
        if(response.Content.Headers.ContentLength>maximum)throw Invalid();
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,token))!=0){if(output.Length+count>maximum)throw Invalid();output.Write(buffer,0,count);}return output.ToArray();
    }
    private void Load()
    {
        try
        {
            if(!File.Exists(path))return;if(new FileInfo(path).Length>16*1024*1024)throw Invalid();
            var disk=JsonSerializer.Deserialize<Disk>(File.ReadAllBytes(path))??throw Invalid();var saved=disk.Snapshot;
            if(disk.Version!=1||disk.Source!=Source||disk.Market!=market||disk.Language!=language||saved.Source!=Source||saved.Market!=market||saved.Language!=language||saved.Revision<0||saved.Platform!="pc"||saved.Plan!="pc"||saved.Games.Count>10000||saved.Games.Any(g=>!ValidId(g.ProductId)||g.EntitlementState!="not-checked"||g.InstalledState!="not-checked")||saved.Games.Select(g=>g.ProductId).Distinct().Count()!=saved.Games.Count||saved.ValidatedAt>clock().AddMilliseconds(250)||saved.ExpiresAt>saved.ValidatedAt+TimeSpan.FromHours(1)||disk.Requests.Count>1024)throw Invalid();
            if(saved.Games.Any(g=>!StoreXboxInstallProviders.Valid(g.InstallProvider)))throw Invalid();
            // An older cache has no launcher evidence: retain its display data
            // but force normal background hydration on the next refresh.
            snapshot=saved.Games.Any(g=>g.InstallProvider is null)?saved with{ExpiresAt=saved.ValidatedAt,IsStale=true}:saved;
            etag=disk.Etag;foreach(var entry in disk.Requests)requests.Add(entry.Key,entry.Value);
        }
        catch(Exception error) when(error is not OutOfMemoryException){snapshot=snapshot with{ErrorCode="catalogue_cache_unreadable"};}
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+".new";
        using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){// No-store metadata exists only for the current UI response. Persist
        // only our local revision and user-request journal, never its HTTP
        // validators, title/image records or content-derived snapshot hash.
        JsonSerializer.Serialize(file,new Disk(1,Source,market,language,metadataMayPersist?etag:null,metadataMayPersist?snapshot:Empty(snapshot.Revision),requests));file.Flush(true);}File.Move(temporary,path,true);
    }
    private static StoreOperationException Invalid()=>new("catalogue_schema_invalid");
    public async Task<(bool Handled,object? Data)> TryHandleAsync(string method,JsonElement args,CancellationToken cancellationToken)
    {
        return method switch
        {
            "xbox-catalog" => (true,await GetXboxCatalogAsync(cancellationToken)),
            "xbox-refresh" => (true,await RefreshXboxCatalogAsync(args.TryGetProperty("force",out var force)&&force.ValueKind==JsonValueKind.True,cancellationToken)),
            "xbox-install-handoff" => (true,await OpenXboxInstallPageAsync(args.Deserialize<StoreXboxHandoffRequest>(StoreLibraryService.JsonOptions)??throw new StoreOperationException("request_invalid"),cancellationToken)),
            _ => (false,null)
        };
    }
}
