// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Consumer Collections protocol, independently implemented; history is not ownership.
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Playhub.StoreLibrary;

internal sealed class StoreXboxAccountCollections(StoreXboxAccountAuth transport,Func<DateTimeOffset> now)
{
    readonly Dictionary<string,Product> lastProducts=new(StringComparer.Ordinal);
    internal static bool ValidId(string? value)=>value is not null&&Regex.IsMatch(value,"^[A-Z0-9]{12}$");
    internal async Task<StoreXboxOwnedGame[]> ReadAsync(XboxAccountTokens account,CancellationToken cancellationToken)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);budget.CancelAfter(TimeSpan.FromSeconds(110));
        var token=budget.Token;var access=new Dictionary<string,string>(StringComparer.Ordinal);var continuations=new HashSet<string>(StringComparer.Ordinal);
        string? continuation=null;bool complete=false;
        for(int page=0;page<30;page++)
        {
            var request=new Dictionary<string,object?> { ["maxPageSize"]=100,["excludeDuplicates"]=true,["market"]="US",["validityType"]="All",
                ["beneficiaries"]=new[]{new{identityType="xuid",identityValue=account.Xuid,localTicketReference="playhub"}} };
            if(continuation!=null)request["continuationToken"]=continuation;
            var(status,body)=await transport.Send("https://collections.mp.microsoft.com/v7.0/collections/query",StoreXboxAccountAuth.Json(request),token,
                "XBL3.0 x="+account.UserHash+";"+account.MarketplaceToken,4*1024*1024);
            if(status!=HttpStatusCode.OK||!body.TryGetProperty("items",out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>100)
                throw Error("xbox_library_unavailable");
            foreach(var row in rows.EnumerateArray())
            {
                if(row.ValueKind!=JsonValueKind.Object)throw Error("xbox_library_response_invalid");
                string id=StoreXboxAccountAuth.Text(row,"productId",12);
                if(!ValidId(id))throw Error("xbox_library_response_invalid");
                string kind=StoreXboxAccountAuth.Text(row,"productKind",40),statusValue=StoreXboxAccountAuth.Text(row,"status",40);
                if(id.StartsWith("CFQ",StringComparison.Ordinal)||kind!="Game"||statusValue!="Active")continue;
                // Consumer Collections omits false optional flags on real
                // purchased entitlements. The Active Game row is the grant;
                // absence of isTrial/licensingRequired does not invalidate it.
                bool hasTrial=row.TryGetProperty("isTrial",out var trial)&&trial.ValueKind!=JsonValueKind.Null;
                if(hasTrial&&trial.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Error("xbox_library_response_invalid");
                if(hasTrial&&trial.GetBoolean()||row.TryGetProperty("skuType",out var sku)&&sku.GetString()=="Trial")continue;
                if(row.TryGetProperty("endDate",out var end)&&end.ValueKind!=JsonValueKind.Null)
                {
                    if(end.ValueKind!=JsonValueKind.String||!DateTimeOffset.TryParse(end.GetString(),out var expires))throw Error("xbox_library_response_invalid");
                    if(expires<=now())continue;
                }
                bool hasConditional=row.TryGetProperty("licensingRequired",out var conditional)&&conditional.ValueKind!=JsonValueKind.Null;
                if(hasConditional&&conditional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Error("xbox_library_response_invalid");
                string type=hasConditional&&conditional.GetBoolean()?"conditional":"owned";
                // The same product can have a purchased SKU and a conditional
                // subscription grant. A valid owned grant remains owned in
                // either page order; another grant must not erase it.
                if(access.TryGetValue(id,out var old)&&old=="owned")type="owned";
                access[id]=type;
                if(access.Count>5000)throw Error("xbox_library_too_large");
            }
            if(!body.TryGetProperty("continuationToken",out var next)||next.ValueKind==JsonValueKind.Null||next.ValueKind==JsonValueKind.String&&string.IsNullOrEmpty(next.GetString()))
            {complete=true;break;}
            if(next.ValueKind!=JsonValueKind.String||next.GetString() is not {Length:>0 and <=8192} cursor||rows.GetArrayLength()==0||!continuations.Add(cursor))
                throw Error("xbox_library_incomplete");
            continuation=cursor;
        }
        if(!complete)throw Error("xbox_library_incomplete");
        var products=await Products(access.Keys.ToArray(),token);
        // A bundle's platform can be proved by its explicitly listed products.
        // No inference from '(PC)' in a title, a played-history row or an EXE.
        var parts=products.Values.SelectMany(p=>p.Parts).Distinct(StringComparer.Ordinal).Where(id=>!products.ContainsKey(id)).ToArray();
        if(parts.Length>5000)throw Error("xbox_library_too_large");
        foreach(var pair in await Products(parts,token))products.Add(pair.Key,pair.Value);
        return access.Select(pair=>{
            var p=products.GetValueOrDefault(pair.Key)??new Product(pair.Key,false,false,null,[]);bool pc=p.Pc||p.Parts.Any(id=>products.TryGetValue(id,out var child)&&child.Pc);
            bool xbox=p.Xbox||p.Parts.Any(id=>products.TryGetValue(id,out var child)&&child.Xbox);
            var childFamilies=p.Parts.Where(id=>products.TryGetValue(id,out var child)&&child.Pc&&child.PackageFamilyName is not null)
                .Select(id=>products[id].PackageFamilyName!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            // A multi-game bundle is not installed merely because one bonus
            // game is present. A separately owned child already has its own row.
            string? family=p.PackageFamilyName??(p.Parts.Length==1&&childFamilies.Length==1&&!access.ContainsKey(p.Parts[0])?childFamilies[0]:null);
            return new StoreXboxOwnedGame(pair.Key,p.Title,pc,xbox,pair.Value,p.Artwork,family,p.RequiredBytes,p.ArtworkFallbackUrls,InstallProvider:p.InstallProvider,InstallProviderId:p.InstallProviderId);
        }).OrderBy(g=>g.Title,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    sealed record Product(string Title,bool Pc,bool Xbox,string? Artwork,string[] Parts,string? PackageFamilyName=null,long? RequiredBytes=null,IReadOnlyList<string>? ArtworkFallbackUrls=null,string? PcPackageFamily=null,bool IsGame=false,string? InstallProvider=null,string? InstallProviderId=null);
    internal async Task<StoreXboxOwnedGame?> GamePassMetadataAsync(string id,CancellationToken token)
    {
        if(!ValidId(id))throw Error("xbox_product_invalid");
        var products=await Products([id],token,allowCached:false);
        if(!products.TryGetValue(id,out var product)||!product.IsGame)return null;
        if(StoreXboxInstallProviders.Refusal(product.InstallProvider,product.InstallProviderId) is not null)return new(id,product.Title,true,product.Xbox,"catalogue",product.Artwork,product.PackageFamilyName,product.RequiredBytes,product.ArtworkFallbackUrls,InstallProvider:product.InstallProvider,InstallProviderId:product.InstallProviderId);
        if(product.Pc&&product.PcPackageFamily is not null)return new(id,product.Title,true,product.Xbox,"catalogue",product.Artwork,product.PcPackageFamily,product.RequiredBytes,product.ArtworkFallbackUrls,InstallProvider:product.InstallProvider,InstallProviderId:product.InstallProviderId);
        if(product.Parts.Length is <1 or >50)return null;
        var children=await Products(product.Parts,token,allowCached:false);
        var installable=children.Where(pair=>pair.Value.IsGame&&pair.Value.Pc&&pair.Value.PcPackageFamily is not null).ToArray();
        if(installable.Length!=1)return null;
        var child=installable[0];return new(id,product.Title,true,product.Xbox,"catalogue",product.Artwork??child.Value.Artwork,child.Value.PcPackageFamily,child.Value.RequiredBytes,product.ArtworkFallbackUrls,child.Key,product.InstallProvider??child.Value.InstallProvider,product.InstallProviderId??child.Value.InstallProviderId);
    }
    internal async Task<IReadOnlyList<StoreXboxOwnedGame>> InstalledMetadataAsync(string[] ids,CancellationToken token)
    {
        if(ids.Length>2000||ids.Any(id=>!ValidId(id)))throw Error("xbox_product_invalid");
        var products=await Products(ids.Distinct(StringComparer.Ordinal).ToArray(),token);
        return products.Select(p=>new StoreXboxOwnedGame(p.Key,p.Value.Title,true,p.Value.Xbox,"installed",p.Value.Artwork,
            RequiredBytes:p.Value.RequiredBytes,ArtworkFallbackUrls:p.Value.ArtworkFallbackUrls,InstallProvider:p.Value.InstallProvider,InstallProviderId:p.Value.InstallProviderId)).ToArray();
    }
    async Task<Dictionary<string,Product>> Products(string[] ids,CancellationToken token,bool allowCached=true)
    {
        var result=new Dictionary<string,Product>(StringComparer.Ordinal);
        foreach(var batch in ids.Chunk(20))
        {
            string url="https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds="+string.Join(',',batch)+"&market=US&languages=en-US";
            var(status,body)=await transport.Send(url,null,token,limit:8*1024*1024,method:HttpMethod.Get);
            if(status!=HttpStatusCode.OK||!body.TryGetProperty("Products",out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>batch.Length)
                throw Error("xbox_library_metadata_incomplete");
            var missing=new HashSet<string>(batch,StringComparer.Ordinal);
            foreach(var row in rows.EnumerateArray())
            {
                string id=StoreXboxAccountAuth.Text(row,"ProductId",12);
                if(!missing.Remove(id))throw Error("xbox_library_metadata_identity_mismatch");
                if(!row.TryGetProperty("LocalizedProperties",out var localized)||localized.ValueKind!=JsonValueKind.Array||localized.GetArrayLength()==0)throw Error("xbox_library_metadata_incomplete");
                string title=StoreXboxAccountAuth.Text(localized[0],"ProductTitle",2048);string[] covers=[];
                if(localized[0].TryGetProperty("Images",out var images)&&images.ValueKind==JsonValueKind.Array)
                    covers=images.EnumerateArray().Where(StoreMetadataArtwork.XboxCoverPurpose).OrderBy(StoreMetadataArtwork.XboxCoverPriority)
                        .Select(i=>i.TryGetProperty("Uri",out var uri)?Image(uri.GetString()):null).OfType<string>().SelectMany(url=>StoreMetadataArtwork.CoverVariants(StoreKind.Xbox,url)).Distinct(StringComparer.Ordinal).Take(4).ToArray();
                var platforms=new HashSet<string>(StringComparer.Ordinal);var children=new HashSet<string>(StringComparer.Ordinal);
                string? family=row.TryGetProperty("Properties",out var productProperties)&&productProperties.ValueKind==JsonValueKind.Object
                    &&productProperties.TryGetProperty("PackageFamilyName",out var primaryFamily)?Family(primaryFamily):null;
                var pcFamilies=new HashSet<string>(StringComparer.Ordinal);long? requiredBytes=null;
                if(row.TryGetProperty("DisplaySkuAvailabilities",out var skus)&&skus.ValueKind==JsonValueKind.Array)
                    foreach(var sku in skus.EnumerateArray())
                    {
                        if(sku.TryGetProperty("Availabilities",out var availabilities)&&availabilities.ValueKind==JsonValueKind.Array)
                            foreach(var availability in availabilities.EnumerateArray())if(availability.TryGetProperty("Conditions",out var conditions)&&conditions.TryGetProperty("ClientConditions",out var client)&&client.ValueKind==JsonValueKind.Object&&client.TryGetProperty("AllowedPlatforms",out var allowed)&&allowed.ValueKind==JsonValueKind.Array)
                                foreach(var platform in allowed.EnumerateArray())if(platform.TryGetProperty("PlatformName",out var platformName)&&platformName.ValueKind==JsonValueKind.String)platforms.Add(platformName.GetString()!);
                        if(sku.TryGetProperty("Sku",out var item)&&item.TryGetProperty("Properties",out var props))
                        {
                            if(props.TryGetProperty("Packages",out var packages)&&packages.ValueKind==JsonValueKind.Array)
                                foreach(var package in packages.EnumerateArray())if(package.TryGetProperty("PlatformDependencies",out var deps)&&deps.ValueKind==JsonValueKind.Array)
                                {
                                    bool desktop=false;
                                    foreach(var dep in deps.EnumerateArray())if(dep.TryGetProperty("PlatformName",out var name)&&name.ValueKind==JsonValueKind.String)
                                    { string platform=name.GetString()!;platforms.Add(platform);desktop|=platform is "Windows.Desktop" or "Windows.Universal"; }
                                    if(desktop)
                                    {
                                        if(package.TryGetProperty("PackageFamilyName",out var packageFamily)&&Family(packageFamily) is { } pf)pcFamilies.Add(pf);
                                        if(package.TryGetProperty("MaxInstallSizeInBytes",out var size)&&PackageBytes(size) is {} bytes)
                                            requiredBytes=Math.Max(requiredBytes??0,bytes);
                                    }
                                }
                            if(props.TryGetProperty("BundledSkus",out var bundled)&&bundled.ValueKind==JsonValueKind.Array)
                                foreach(var part in bundled.EnumerateArray())if(part.TryGetProperty("BigId",out var child)&&ValidId(child.GetString()))children.Add(child.GetString()!);
                        }
                    }
                bool pc=platforms.Contains("Windows.Desktop")||platforms.Contains("Windows.Universal"),xbox=platforms.Contains("Windows.Xbox");
                family??=pcFamilies.Count==1?pcFamilies.Single():null;
                var product=new Product(title,pc,xbox,covers.FirstOrDefault(),children.ToArray(),family,requiredBytes,covers.Skip(1).ToArray(),
                    pcFamilies.Count==1?pcFamilies.Single():null,row.TryGetProperty("ProductKind",out var kind)&&kind.GetString()=="Game");result.Add(id,product);lastProducts[id]=product;
            }
            try
            {
                var(providerStatus,providerBody)=await transport.Send(StoreXboxInstallProviders.Url(),StoreXboxAccountAuth.Json(new{Products=batch}),token,limit:8*1024*1024);
                if(providerStatus==HttpStatusCode.OK)
                    foreach(var pair in StoreXboxInstallProviders.Parse(providerBody,batch))if(result.TryGetValue(pair.Key,out var product))
                    {var classified=product with{InstallProvider=pair.Value.Provider,InstallProviderId=pair.Value.ExtensionId};result[pair.Key]=classified;lastProducts[pair.Key]=classified;}
            }
            catch(Exception error)when(error is not OutOfMemoryException and not OperationCanceledException){/* Missing launcher metadata grants no partner/native classification. */}
            // A successful public-catalog response may omit delisted or
            // market-specific products. That is missing metadata, not a failed
            // Collections ownership query. Retain known metadata by exact ID;
            // never guess PC support for an unknown product.
            if(allowCached)foreach(string id in missing)if(lastProducts.TryGetValue(id,out var prior))result.Add(id,prior);
        }
        return result;
    }
    static string? Family(JsonElement value)=>value.ValueKind==JsonValueKind.String&&value.GetString() is {Length:>0 and <=255} text
        &&Regex.IsMatch(text,@"^[A-Za-z0-9._-]+_[a-z0-9]{13}$")?text:null;
    static long? PackageBytes(JsonElement size)
    {
        // DisplayCatalog returns this optional field as either a JSON number,
        // decimal string or null. TryGetInt64 throws for non-number kinds;
        // absent/malformed metadata must never hide the owned PC library.
        long bytes=0;
        bool valid=size.ValueKind==JsonValueKind.Number&&size.TryGetInt64(out bytes)
            ||size.ValueKind==JsonValueKind.String&&long.TryParse(size.GetString(),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out bytes);
        return valid&&bytes>0&&bytes<2L*1024*1024*1024*1024?bytes:null;
    }
    static string? Image(string? text)
    {
        if(text?.StartsWith("//",StringComparison.Ordinal)==true)text="https:"+text;
        return text is {Length:>0 and <=8192}&&Uri.TryCreate(text,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort
            &&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&uri.Host is "store-images.s-microsoft.com" or "store-images.microsoft.com" or "images-eds-ssl.xboxlive.com"?text:null;
    }
    static StoreOperationException Error(string code)=>new(code);
}
