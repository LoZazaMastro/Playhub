// SPDX-License-Identifier: MIT. Catalog membership and request provenance are not license grants.
using System.Text.Json;
namespace Playhub.StoreLibrary;
internal sealed record StoreGamePassInstallRequest(string ProductId,long ExpectedRevision,long ExpectedAccountRevision,Guid RequestId,string? VolumeName=null);
public sealed partial class StoreXboxAccountProvider
{
    internal sealed record GamePassRequest(Guid RequestId,string ProductId,string Family,string AccountIdentity,long CatalogRevision,long AccountRevision,
        string Title,string? ArtworkUrl,long? RequiredBytes,DateTimeOffset RequestedAt,string? VolumeName=null,string? InstallProductId=null,string? InstallProvider=null,string? InstallProviderId=null);
    sealed record GamePassDisk(int Version,IReadOnlyList<GamePassRequest> Requests);
    readonly Dictionary<Guid,GamePassRequest> gamePassRequests=[];
    bool gamePassLoaded,gamePassValid=true;
    readonly SemaphoreSlim gamePassGate=new(1,1);
    internal string? GamePassCacheOverride { private get; set; }
    internal Func<string,CancellationToken,Task<StoreXboxOwnedGame?>>? GamePassMetadataLookup { private get; set; }
    string? GamePassCachePath=>GamePassCacheOverride??(ownedLibraryCachePath is null?null:Path.Combine(Path.GetDirectoryName(ownedLibraryCachePath)!,"xbox-gamepass-requests-v1.json"));
    void LoadGamePassRequests()
    {
        if(gamePassLoaded)return;gamePassLoaded=true;
        try
        {
            if(GamePassCachePath is not {} path||!File.Exists(path))return;
            if(new FileInfo(path).Length>4*1024*1024)throw new IOException();
            var disk=JsonSerializer.Deserialize<GamePassDisk>(File.ReadAllBytes(path));
            if(disk?.Version!=1||disk.Requests is null||disk.Requests.Count>2000||disk.Requests.Any(row=>row is null||row.RequestId==Guid.Empty||
                !StoreXboxAccountCollections.ValidId(row.ProductId)||!WindowsXboxPackageCatalog.ValidFamily(row.Family)||row.AccountIdentity is not{Length:64}||!row.AccountIdentity.All(Uri.IsHexDigit)||
                row.CatalogRevision<0||row.AccountRevision<1||row.Title is not{Length:>0 and <=2048}||row.Title.Any(char.IsControl)||row.RequiredBytes<0||row.RequestedAt>now().AddMinutes(5)||row.VolumeName is not null&&!StoreWindowsInstallControl.ValidVolume(row.VolumeName)||row.InstallProductId is not null&&!StoreXboxAccountCollections.ValidId(row.InstallProductId))||disk.Requests.Select(row=>row.RequestId).Distinct().Count()!=disk.Requests.Count)throw new IOException();
            foreach(var row in disk.Requests)gamePassRequests.Add(row.RequestId,row);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){gamePassValid=false;gamePassRequests.Clear();}
    }
    void SaveGamePassRequest(GamePassRequest request)
    {
        LoadGamePassRequests();if(!gamePassValid||GamePassCachePath is not{}path||gamePassRequests.Count>=2000)throw new StoreOperationException("gamepass_provenance_unavailable");
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";gamePassRequests.Add(request.RequestId,request);
        try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,new GamePassDisk(1,gamePassRequests.Values.ToArray()));file.Flush(true);}File.Move(temporary,path,true);}
        catch{gamePassRequests.Remove(request.RequestId);throw;}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}}
    }
    internal bool CanRequestGamePass
    {get{try{lock(sync){Load();LoadGamePassRequests();return !closed&&saved is not null&&gamePassValid&&GamePassCachePath is not null&&UseWindowsStoreManagedInstalls&&Engine.WindowsManagedRuntime.Available;}}
        catch(Exception error)when(error is not OutOfMemoryException){return false;}}}
    internal bool CurrentGamePassFamily(string product,string family)
    {lock(sync){Load();LoadGamePassRequests();return !closed&&saved is not null&&gamePassValid&&gamePassRequests.Values.Any(row=>row.AccountIdentity==Identity(saved.Xuid)&&row.ProductId==product&&string.Equals(row.Family,family,StringComparison.OrdinalIgnoreCase));}}
    internal bool CurrentInstallFamily(string product,string family)=>CurrentOwnedFamily(product,family)||CurrentGamePassFamily(product,family);
    internal bool CurrentGamePassRegisteredProduct(string product,string family,string? registered)
    {lock(sync){Load();LoadGamePassRequests();return !closed&&saved is not null&&gamePassValid&&gamePassRequests.Values.Any(row=>row.AccountIdentity==Identity(saved.Xuid)&&row.ProductId==product&&row.Family.Equals(family,StringComparison.OrdinalIgnoreCase)&&row.InstallProductId==registered);}}
    GamePassRequest? GamePassLaunchRequest(string product)
    {LoadGamePassRequests();return saved is null||!gamePassValid?null:gamePassRequests.Values.LastOrDefault(row=>row.AccountIdentity==Identity(saved.Xuid)&&row.ProductId==product);}
    internal StoreXboxOwnedGame? RetainedInstallMetadata(string product,string family)
    {
        lock(sync)
        {
            Load();if(saved is null)return null;string identity=Identity(saved.Xuid);
            var purchased=ownedIdentity==identity?owned.Games.FirstOrDefault(row=>row.ProductId==product&&row.SupportsPc&&row.PackageFamilyName==family):null;
            if(purchased is not null)return purchased;
            var request=GamePassLaunchRequest(product);
            return request?.Family.Equals(family,StringComparison.OrdinalIgnoreCase)==true
                ?new(request.ProductId,request.Title,true,false,"catalogue",request.ArtworkUrl,request.Family,request.RequiredBytes,InstallProductId:request.InstallProductId,InstallProvider:request.InstallProvider,InstallProviderId:request.InstallProviderId):null;
        }
    }
    internal async Task<StoreXboxHandoffResult> InstallGamePassAsync(StoreGamePassInstallRequest request,Func<CancellationToken,Task<StoreXboxCatalogSnapshot>> readCatalog,CancellationToken token)
    {await gamePassGate.WaitAsync(token);try{return await InstallGamePassCoreAsync(request,readCatalog,token);}finally{gamePassGate.Release();}}
    async Task<StoreXboxHandoffResult> InstallGamePassCoreAsync(StoreGamePassInstallRequest request,Func<CancellationToken,Task<StoreXboxCatalogSnapshot>> readCatalog,CancellationToken token)
    {
        StoreXboxHandoffResult Refuse(string error)=>new(false,"not-opened",error,request.ProductId,request.RequestId);
        if(request.RequestId==Guid.Empty||!StoreXboxAccountCollections.ValidId(request.ProductId)||request.ExpectedAccountRevision<1)return Refuse("request_invalid");
        if(request.VolumeName is not null&&!StoreWindowsInstallControl.ValidVolume(request.VolumeName)||Engine.WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime&&request.VolumeName is null)return Refuse("windows_store_destination_unavailable");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(45));var ct=deadline.Token;
        var catalog=await readCatalog(ct);
        bool ValidCatalog(StoreXboxCatalogSnapshot value)=>request.ExpectedRevision>=0&&value.Revision>=request.ExpectedRevision&&value.ErrorCode is null&&value.Platform=="pc"&&value.Plan=="pc"&&
            (!value.IsStale&&value.ExpiresAt>now()||value.SourceWarning=="transient_response_not_cached"&&value.ValidatedAt is{}validated&&validated<=now().AddMilliseconds(250)&&validated>=now().AddSeconds(-45)&&value.ExpiresAt==validated)&&
            value.Games.Count(row=>row.ProductId==request.ProductId&&row.IsCurrentMember&&row.PcAvailability=="desktop-confirmed")==1;
        if(!ValidCatalog(catalog))return Refuse("gamepass_catalogue_changed");
        var catalogGame=catalog.Games.Single(row=>row.ProductId==request.ProductId);
        if(StoreXboxInstallProviders.Refusal(catalogGame.InstallProvider,catalogGame.InstallProviderId) is {} catalogPartnerError)return Refuse(catalogPartnerError);
        Saved account;string identity;GamePassRequest? existing;
        lock(sync){Load();LoadGamePassRequests();if(!CanRequestGamePass)return Refuse("gamepass_install_unavailable");account=saved!;identity=Identity(account.Xuid);gamePassRequests.TryGetValue(request.RequestId,out existing);
            if(existing is not null&&(existing.ProductId!=request.ProductId||existing.CatalogRevision!=request.ExpectedRevision||existing.AccountRevision!=request.ExpectedAccountRevision||existing.AccountIdentity!=identity||existing.VolumeName!=request.VolumeName))return Refuse("request_id_reused");}
        var state=await Engine.GetSnapshotAsync(ct);var currentAccount=state.Accounts.Single(row=>row.Store==StoreKind.Xbox);
        if(currentAccount.State!=StoreConnectionState.Connected||currentAccount.Revision!=request.ExpectedAccountRevision)return Refuse("account_changed");
        if(Engine.WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime destinations&&!(await destinations.GetInstallVolumesAsync(ct)).Any(v=>v.VolumeName==request.VolumeName))return Refuse("windows_store_destination_unavailable");
        var metadata=existing?.InstallProvider is null?await (GamePassMetadataLookup?.Invoke(request.ProductId,ct)??collection.GamePassMetadataAsync(request.ProductId,ct)):new StoreXboxOwnedGame(existing.ProductId,existing.Title,true,false,"catalogue",existing.ArtworkUrl,existing.Family,existing.RequiredBytes,InstallProductId:existing.InstallProductId,InstallProvider:existing.InstallProvider,InstallProviderId:existing.InstallProviderId);
        if(metadata?.ProductId==request.ProductId&&StoreXboxInstallProviders.Refusal(metadata.InstallProvider,metadata.InstallProviderId) is {} partnerError)return Refuse(partnerError);
        if(metadata?.ProductId!=request.ProductId||!metadata.SupportsPc||!WindowsXboxPackageCatalog.ValidFamily(metadata.PackageFamilyName))return Refuse("gamepass_pc_package_unavailable");
        if(!ValidCatalog(await readCatalog(ct)))return Refuse("gamepass_catalogue_changed");
        metadata=metadata with{Title=catalog.Games.Single(row=>row.ProductId==request.ProductId).Title};
        lock(sync)
        {
            if(closed||saved is null||!ReferenceEquals(account,saved)||Identity(saved.Xuid)!=identity)return Refuse("account_changed");
            if(existing is null){existing=new(request.RequestId,request.ProductId,metadata.PackageFamilyName!,identity,request.ExpectedRevision,request.ExpectedAccountRevision,metadata.Title,metadata.ArtworkUrl,metadata.RequiredBytes,now(),request.VolumeName,metadata.InstallProductId,metadata.InstallProvider,metadata.InstallProviderId);
                try{SaveGamePassRequest(existing);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or StoreOperationException){return Refuse("gamepass_provenance_unavailable");}}
        }
        var prepared=await Engine.StageGamePassGameAsync(this,metadata,request.ExpectedAccountRevision,ct);if(!prepared.Accepted)return Refuse(prepared.ErrorCode??"account_changed");
        var result=await Engine.BeginWindowsManagedInstallAsync(this,request.ProductId,request.ExpectedAccountRevision,metadata.PackageFamilyName!,request.RequestId,ct,"gamePass",request.VolumeName,existing.InstallProductId);
        return new(result.Accepted,result.Accepted?"official-install-requested":"not-opened",result.ErrorCode,request.ProductId,request.RequestId);
    }
    async Task<IReadOnlyList<ProviderGame>> AppendGamePassInstallsAsync(IReadOnlyList<ProviderGame> rows,CancellationToken token)
    {
        GamePassRequest[] records;string? identity;
        lock(sync){LoadGamePassRequests();identity=saved is null?null:Identity(saved.Xuid);records=gamePassValid?gamePassRequests.Values.Where(row=>row.AccountIdentity==identity).GroupBy(row=>row.ProductId).Select(group=>group.Last()).ToArray():[];}
        if(records.Length==0)return rows;
        var packages=await PackageBatchLookup(records.Select(row=>row.Family),token);
        lock(sync)if(saved is null||Identity(saved.Xuid)!=identity)throw new StoreOperationException("account_changed");
        var result=rows.ToDictionary(row=>row.Id,StringComparer.Ordinal);
        foreach(var row in records)
        {
            if(CurrentOwnedFamily(row.ProductId,row.Family))continue;
            var package=packages.GetValueOrDefault(row.Family);if(package?.StatusOk!=true||package.IsPlaceholder||package.StoreProductId!=(row.InstallProductId??row.ProductId))continue;
            result[row.ProductId]=new(row.ProductId,row.Title,false,row.RequiredBytes,package.InstallLocation,package.Aumid is not null&&package.ExecutablePath is not null,row.ArtworkUrl,LibraryKind:"gamePass",InstallProvider:row.InstallProvider);
        }
        return result.Values.ToArray();
    }
}

public sealed class StoreGamePassProtocol(StoreXboxCatalog catalog,StoreXboxAccountProvider provider):IStoreProtocolExtension
{
    public async Task<(bool Handled,object? Data)> TryHandleAsync(string method,JsonElement args,CancellationToken token)
    {
        if(method is "xbox-catalog" or "xbox-refresh")
        {var snapshot=method=="xbox-catalog"?await catalog.GetXboxCatalogAsync(token):await catalog.RefreshXboxCatalogAsync(args.TryGetProperty("force",out var force)&&force.ValueKind==JsonValueKind.True,token);return(true,snapshot with{CanRequestInstall=provider.CanRequestGamePass});}
        if(method=="xbox-gamepass-install")
        {
            // No-store catalog responses are display data, not reusable
            // entitlement evidence. Revalidate for THIS explicit request and
            // check that bounded fresh response again after metadata I/O.
            // A no-store response must stay scoped to this explicit request.
            // Reading the persistent cache again would discard the fresh result.
            Task<StoreXboxCatalogSnapshot>? refreshed=null;
            Task<StoreXboxCatalogSnapshot> Read(CancellationToken ct)=>refreshed??=catalog.RefreshXboxCatalogAsync(true,ct);
            return(true,await provider.InstallGamePassAsync(args.Deserialize<StoreGamePassInstallRequest>(StoreLibraryService.JsonOptions)??throw new StoreOperationException("request_invalid"),Read,token));
        }
        return(false,null);
    }
}
