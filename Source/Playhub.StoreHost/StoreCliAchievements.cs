// SPDX-License-Identifier: GPL-3.0-or-later
// Achievement GraphQL operations and GOG Galaxy flow adapted from Unifideck 0.8.0.
// Copyright (c) Unifideck contributors; Windows adaptation (c) 2026 LoZazaMastro.
// Unlike the Linux reference, this adapter always validates TLS and refuses redirects.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace Playhub.StoreLibrary;

internal interface IStoreAchievementAccount { string? ReadAchievementAccountIdentity(); }
public sealed partial class StoreCliProvider:IStoreAchievementsProvider,IStoreAchievementAccount
{
    internal HttpClient AchievementHttp {get;set;}=new(new SocketsHttpHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None}){Timeout=TimeSpan.FromSeconds(25)};
    string? IStoreAchievementAccount.ReadAchievementAccountIdentity()
    {
        try
        {
            using var value=JsonDocument.Parse(StoreCredentialVault.Read(vault));
            var auth=Store==StoreKind.Epic?value.RootElement:value.RootElement.GetProperty(GogClient);
            string id=auth.GetProperty(Store==StoreKind.Epic?"account_id":"user_id").ToString();
            return id.Length is >0 and <=256?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id))):null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException){return null;}
    }
    public async Task<IReadOnlyList<StoreAchievement>> ReadAchievementsAsync(StoreLibraryGame game,string language,CancellationToken token)
    {
        if(game.Key.Store!=Store||!game.IsInstalled)throw new StoreOperationException("owned_library_changed_or_unavailable");
        ValidateId(game.Key.Id);using var scope=OpenQuery(token);
        if(Store==StoreKind.Epic)
        {
            string metadata=Path.Combine(Path.GetDirectoryName(scope.AuthPath)!,"metadata",game.Key.Id+".json");
            string sandbox;
            if(File.Exists(metadata))
            {
                if(new FileInfo(metadata).Length>4*1024*1024)throw new StoreOperationException("achievements_title_unavailable");
                using var data=JsonDocument.Parse(await File.ReadAllBytesAsync(metadata,token));
                if(data.RootElement.GetProperty("app_name").GetString()!=game.Key.Id)throw new StoreOperationException("achievements_title_unavailable");
                sandbox=data.RootElement.GetProperty("metadata").GetProperty("namespace").GetString()??"";
            }
            else sandbox=OfficialInstallations.Read(Store,token).Find(game.Key.Id)?.EpicNamespace??"";
            if(sandbox.Length is <1 or >256||sandbox.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('-' or '_' or '.')))throw new StoreOperationException("achievements_title_unavailable");
            for(int attempt=0;attempt<2;attempt++)
            {
                using var document=JsonDocument.Parse(await File.ReadAllBytesAsync(scope.AuthPath,token));var auth=document.RootElement;
                string access=auth.GetProperty("access_token").GetString()??"",account=auth.GetProperty("account_id").GetString()??"";
                if(access.Length is <1 or >32768||account.Length is <1 or >256)throw new StoreOperationException("authentication_required");
                try{return await ReadEpicAchievements(game.Key.Id,sandbox,account,access,language,token);}
                catch(StoreOperationException error)when(error.Code=="authentication_expired"&&attempt==0&&scope.OwnsCredentials)
                {var refreshed=await query(tool,["status"],scope.Environment,token);if(refreshed.Code!=0)throw new StoreOperationException("authentication_expired");}
            }
            throw new StoreOperationException("authentication_expired");
        }
        if(scope.OwnsCredentials)await RefreshGogToken(token);
        using var credential=JsonDocument.Parse(await File.ReadAllBytesAsync(scope.AuthPath,token));var gog=credential.RootElement.GetProperty(GogClient);
        string refresh=gog.GetProperty("refresh_token").GetString()??"",user=gog.GetProperty("user_id").ToString();
        if(refresh.Length is <1 or >32768||user.Length is <1 or >32||!user.All(char.IsAsciiDigit))throw new StoreOperationException("authentication_required");
        return await ReadGogAchievements(game.Key.Id,refresh,user,token);
    }
    private const string EpicDefinitionsQuery="""
query Achievement($sandboxId: String!, $locale: String!) {
 Achievement { productAchievementsRecordBySandbox(sandboxId: $sandboxId, locale: $locale) {
 totalAchievements achievements { achievement { name hidden unlockedDisplayName lockedDisplayName unlockedDescription lockedDescription unlockedIconLink lockedIconLink rarity { percent } } }
 } } }
""";
    private const string EpicStateQuery="""
query PlayerAchievement($epicAccountId: String!, $sandboxId: String!) {
 PlayerAchievement { playerAchievementGameRecordsBySandbox(epicAccountId: $epicAccountId, sandboxId: $sandboxId) {
 records { totalUnlocked playerAchievements { playerAchievement { unlocked unlockDate achievementName } } }
 } } }
""";
    private async Task<IReadOnlyList<StoreAchievement>> ReadEpicAchievements(string id,string sandbox,string account,string access,string language,CancellationToken token)
    {
        using var definitions=await AchievementRequest(new("https://store.epicgames.com/graphql"),access,new{query=EpicDefinitionsQuery,variables=new{sandboxId=sandbox,locale=language}},token);
        using var progress=await AchievementRequest(new("https://store.epicgames.com/graphql"),access,new{query=EpicStateQuery,variables=new{epicAccountId=account,sandboxId=sandbox}},token);
        static JsonElement Data(JsonDocument doc,string key)
        {
            if(doc.RootElement.TryGetProperty("errors",out var errors)&&errors.ValueKind==JsonValueKind.Array&&errors.GetArrayLength()>0)throw new StoreOperationException("achievements_unavailable");
            return doc.RootElement.GetProperty("data").GetProperty(key);
        }
        var record=Data(definitions,"Achievement").GetProperty("productAchievementsRecordBySandbox");
        if(record.ValueKind==JsonValueKind.Null)return Array.Empty<StoreAchievement>();
        var rows=record.GetProperty("achievements");if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>2000)throw new StoreOperationException("achievements_response_invalid");
        var state=Data(progress,"PlayerAchievement").GetProperty("playerAchievementGameRecordsBySandbox").GetProperty("records");
        JsonElement unlocks=JsonSerializer.SerializeToElement(Array.Empty<object>());
        if(state.ValueKind==JsonValueKind.Array&&state.GetArrayLength()>0)unlocks=state[0].GetProperty("playerAchievements");
        else if(state.ValueKind==JsonValueKind.Object)unlocks=state.GetProperty("playerAchievements");
        else if(state.ValueKind!=JsonValueKind.Array&&state.ValueKind!=JsonValueKind.Null)throw new StoreOperationException("achievements_response_invalid");
        return StoreAchievementNormalization.Epic(rows,unlocks);
    }
    private async Task<IReadOnlyList<StoreAchievement>> ReadGogAchievements(string id,string refresh,string user,CancellationToken token)
    {
        using var builds=await GogPublicMetadata(new($"https://content-system.gog.com/products/{id}/os/windows/builds?generation=2"),false,token);
        var list=builds.RootElement.GetProperty("items");
        if(list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()==0)return Array.Empty<StoreAchievement>();
        string link=list[0].GetProperty("link").GetString()??"";
        if(!Uri.TryCreate(link,UriKind.Absolute,out var manifestUri))throw new StoreOperationException("achievements_title_unavailable");
        using var manifest=await GogPublicMetadata(manifestUri,true,token);var root=manifest.RootElement;
        string client=StoreAchievementNormalization.Text(root,"clientId",128),secret=StoreAchievementNormalization.Text(root,"clientSecret",512);
        if(client.Length==0||secret.Length==0)return Array.Empty<StoreAchievement>();
        if(!client.All(char.IsAsciiDigit))throw new StoreOperationException("achievements_response_invalid");
        // Refresh material remains in this process and is never a CLI argument.
        using var authorization=await AchievementRequest(new("https://auth.gog.com/token?"+Query(new(){["client_id"]=client,["client_secret"]=secret,["grant_type"]="refresh_token",["refresh_token"]=refresh})),null,null,token);
        string access=authorization.RootElement.GetProperty("access_token").GetString()??"";
        if(access.Length is <1 or >32768)throw new StoreOperationException("authentication_expired");
        var rows=new List<StoreAchievement>();var ids=new HashSet<string>(StringComparer.Ordinal);var pages=new HashSet<string>(StringComparer.Ordinal);string? page=null;
        for(int i=0;i<50;i++)
        {
            string url=$"https://gameplay.gog.com/clients/{client}/users/{user}/achievements"+(page is null?"":"?page_token="+Uri.EscapeDataString(page));
            using var result=await AchievementRequest(new(url),access,null,token,emptyOn404:true);var body=result.RootElement;
            var items=body.GetProperty("items");if(items.ValueKind!=JsonValueKind.Array)throw new StoreOperationException("achievements_response_invalid");
            foreach(var item in items.EnumerateArray())if(StoreAchievementNormalization.Gog(item) is {} row&&ids.Add(row.Key)){rows.Add(row);if(rows.Count>2000)throw new StoreOperationException("achievements_response_too_large");}
            page=body.TryGetProperty("page_token",out var next)&&next.ValueKind==JsonValueKind.String?next.GetString():null;
            if(string.IsNullOrEmpty(page)||page=="0")return rows;
            if(page.Length>4096||!pages.Add(page))throw new StoreOperationException("achievements_response_invalid");
        }
        throw new StoreOperationException("achievements_response_too_large");
    }
    private async Task<JsonDocument> AchievementRequest(Uri uri,string? access,object? body,CancellationToken token,bool emptyOn404=false)
    {
        if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||uri.Host is not("store.epicgames.com" or "auth.gog.com" or "gameplay.gog.com"))throw new StoreOperationException("achievements_endpoint_invalid");
        using var request=new HttpRequestMessage(body is null?HttpMethod.Get:HttpMethod.Post,uri);
        request.Headers.Accept.Add(new("application/json"));request.Headers.UserAgent.ParseAdd(Store==StoreKind.Epic?"EpicGamesLauncher/15.17.1-35104619+++Portal+Release-Live":"GalaxyClient/2.0.45");
        if(access is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        if(body is not null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await AchievementHttp.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(Store==StoreKind.Epic&&response.StatusCode==HttpStatusCode.Forbidden&&access is not null&&body is not null)
        {
            var system=await EpicSystemRequest(access,body,token);
            if(system is {Code:200})return JsonDocument.Parse(system.Value.Body);
            if(system is {Code:401})throw new StoreOperationException("authentication_expired");
            throw new StoreOperationException("achievements_unavailable");
        }
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new StoreOperationException("authentication_expired");
        if(emptyOn404&&response.StatusCode==HttpStatusCode.NotFound)return JsonDocument.Parse("{\"items\":[],\"page_token\":null,\"total_count\":0}");
        if(!response.IsSuccessStatusCode||response.RequestMessage?.RequestUri!=uri)throw new StoreOperationException("achievements_unavailable");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[32768];int n;
        while((n=await input.ReadAsync(buffer,token))>0){if(output.Length+n>8*1024*1024)throw new StoreOperationException("achievements_response_too_large");output.Write(buffer,0,n);}
        return JsonDocument.Parse(output.ToArray());
    }
}
