// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
using System.Net;
using System.Text.Json;
namespace Playhub.StoreLibrary;

// Microsoft PCInline explicitly relates each game to a launcher application via
// GameCatalogExtensionId. These are launcher APP identities, never game IDs,
// title matches, publisher names, support URLs or inferred package prefixes.
internal sealed record StoreXboxInstallProvider(string Provider,string? ExtensionId);
internal static class StoreXboxInstallProviders
{
    internal static string Url(string market="US",string language="en-US")=>"https://catalog.gamepass.com/products?market="+Uri.EscapeDataString(market)+"&language="+Uri.EscapeDataString(language)+"&hydration=PCInline";
    internal static string Provider(string? extension)=>extension switch
    {"XPDM5VSMTKQLBJ"=>"battlenet","XPDP2QW12DFSFK"=>"ubisoft","XPFC0VB7MLFWLC"=>"ea",null or ""=>"microsoft",_=>"unknown"};
    internal static bool Valid(string? provider)=>provider is null or "microsoft" or "unknown" or "ea" or "ubisoft" or "battlenet";
    internal static string? Refusal(string? provider,string? extension)=>provider is "ea" or "ubisoft" or "battlenet"||extension is not null?
        "xbox_partner_"+(provider is "ea" or "ubisoft" or "battlenet"?provider:"unknown")+"_install_unavailable":null;
    internal static IReadOnlyDictionary<string,StoreXboxInstallProvider> Parse(JsonElement body,IReadOnlyCollection<string> ids)
    {
        if(!body.TryGetProperty("Products",out var products)||products.ValueKind!=JsonValueKind.Object||products.EnumerateObject().Count()>200)throw new StoreOperationException("xbox_install_provider_metadata_invalid");
        var result=new Dictionary<string,StoreXboxInstallProvider>(StringComparer.Ordinal);
        foreach(string id in ids)
        {
            if(!products.TryGetProperty(id,out var row)||row.ValueKind!=JsonValueKind.Object)continue;
            if(!row.TryGetProperty("StoreId",out var store)||store.ValueKind!=JsonValueKind.String||store.GetString()!=id)throw new StoreOperationException("xbox_install_provider_identity_mismatch");
            if(!row.TryGetProperty("GameCatalogExtensionId",out var field))continue;
            string? extension=field.ValueKind==JsonValueKind.Null?null:field.ValueKind==JsonValueKind.String?field.GetString():"invalid-extension";
            if(extension=="")extension=null;
            // An upstream bad field must not erase the other games in a batch.
            // Keep only a non-sensitive sentinel; never infer a native install
            // from an explicit but malformed external-launcher relationship.
            if(extension is not null&&(extension.Length is <12 or >32||!extension.All(char.IsAsciiLetterOrDigit)))extension="invalid-extension";
            result.Add(id,new(Provider(extension),extension));
        }
        return result;
    }
    internal static async Task<IReadOnlyDictionary<string,StoreXboxInstallProvider>> ReadAsync(HttpClient http,string[] ids,string market,string language,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,Url(market,language)){Content=StoreXboxAccountAuth.Json(new{Products=ids})};
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(response.StatusCode!=HttpStatusCode.OK||response.RequestMessage?.RequestUri!=request.RequestUri)throw new StoreOperationException("xbox_install_provider_metadata_unavailable");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[32768];int n;
        while((n=await input.ReadAsync(buffer,token))!=0){if(output.Length+n>8*1024*1024)throw new StoreOperationException("xbox_install_provider_metadata_invalid");output.Write(buffer,0,n);}
        using var document=JsonDocument.Parse(output.ToArray());return Parse(document.RootElement,ids);
    }
}
