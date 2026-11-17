// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Globalization;
using System.Net;
using System.Text.Json;
namespace Playhub.StoreLibrary;

internal sealed partial class StoreXboxAccountAuth
{
    internal async Task<JsonElement[]> ReadLiveAchievementsAsync(XboxAccountTokens account,uint titleId,string language,CancellationToken token)
    {
        if(titleId==0||account.LiveToken is null)throw Failed("xbox_achievements_title_unavailable");
        var rows=new List<JsonElement>();var seen=new HashSet<string>(StringComparer.Ordinal);var ids=new HashSet<string>(StringComparer.Ordinal);string? cursor=null;
        for(int page=0;page<10;page++){
            string url="https://achievements.xboxlive.com/users/xuid("+account.Xuid+")/achievements?titleId="+titleId.ToString(CultureInfo.InvariantCulture)+"&types=Persistent&maxItems=100"+(cursor is null?"":"&continuationToken="+Uri.EscapeDataString(cursor));
            var(status,body)=await Send(url,null,token,"XBL3.0 x="+account.UserHash+";"+account.LiveToken,2*1024*1024,HttpMethod.Get,"4",language);
            if(status!=HttpStatusCode.OK)throw Failed(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden?"xbox_signin_expired":"xbox_achievements_unavailable");
            if(!body.TryGetProperty("achievements",out var items)||items.ValueKind!=JsonValueKind.Array||!body.TryGetProperty("pagingInfo",out var paging)||paging.ValueKind!=JsonValueKind.Object)throw Failed("xbox_response_invalid");
            foreach(var item in items.EnumerateArray()){
                var normalized=NormalizeLiveAchievement(item,titleId);if(normalized is null)continue;
                var key=normalized.Value.GetProperty("serviceConfigId").GetString()+":"+normalized.Value.GetProperty("id").GetString();
                if(ids.Add(key)){if(rows.Count>=500)throw Failed("xbox_response_too_large");rows.Add(normalized.Value);}
            }
            cursor=paging.TryGetProperty("continuationToken",out var continuation)&&continuation.ValueKind==JsonValueKind.String?continuation.GetString():null;
            if(string.IsNullOrEmpty(cursor))return rows.ToArray();
            if(cursor.Length>4096||!seen.Add(cursor))throw Failed("xbox_response_invalid");
        }
        throw Failed("xbox_response_too_large");
    }
    internal static JsonElement? NormalizeLiveAchievement(JsonElement row,uint titleId)
    {
        if(row.ValueKind!=JsonValueKind.Object)return null;
        string? Get(string key,int max)=>row.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.String&&p.GetString() is {Length:>0} s&&s.Length<=max?s:null;
        if(Get("achievementType",32)!="Persistent"||Get("id",128) is not {} id||Get("name",512) is not {} name
            ||!row.TryGetProperty("titleAssociations",out var titles)||titles.ValueKind!=JsonValueKind.Array||!titles.EnumerateArray().Any(t=>t.ValueKind==JsonValueKind.Object&&t.TryGetProperty("id",out var x)&&x.ToString()==titleId.ToString(CultureInfo.InvariantCulture)))return null;
        bool achieved=Get("progressState",32)=="Achieved"&&(!row.TryGetProperty("isRevoked",out var revoked)||revoked.ValueKind!=JsonValueKind.True);
        string? unlocked=null;
        if(achieved&&row.TryGetProperty("progression",out var progression)&&progression.ValueKind==JsonValueKind.Object&&progression.TryGetProperty("timeUnlocked",out var time)&&time.ValueKind==JsonValueKind.String&&DateTimeOffset.TryParse(time.GetString(),out var stamp)&&stamp.Year>=2001&&stamp.Year<3000)unlocked=stamp.ToUniversalTime().ToString("O");
        string? icon=null;
        if(row.TryGetProperty("mediaAssets",out var media)&&media.ValueKind==JsonValueKind.Array)
            foreach(var asset in media.EnumerateArray().Take(20))if(asset.ValueKind==JsonValueKind.Object&&asset.TryGetProperty("type",out var kind)&&kind.ValueKind==JsonValueKind.String&&kind.GetString()=="Icon"&&asset.TryGetProperty("url",out var raw)&&raw.ValueKind==JsonValueKind.String&&raw.GetString() is {Length:<=2048} url&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&uri.IsDefaultPort&&(uri.Host is "images-eds-ssl.xboxlive.com" or "images-eds.xboxlive.com" or "store-images.s-microsoft.com")){icon=url;break;}
        double? rarity=null;
        if(row.TryGetProperty("rarity",out var r)&&r.ValueKind==JsonValueKind.Object&&r.TryGetProperty("currentPercentage",out var percent)&&percent.ValueKind==JsonValueKind.Number&&percent.TryGetDouble(out var n)&&double.IsFinite(n)&&n>=0&&n<=100)rarity=n;
        return JsonSerializer.SerializeToElement(new{id,serviceConfigId=Get("serviceConfigId",128)??"",name,description=Get("description",4096)??"",lockedDescription=Get("lockedDescription",4096)??"",isSecret=row.TryGetProperty("isSecret",out var secret)&&secret.ValueKind==JsonValueKind.True,
            achievementType="Persistent",progressState=achieved?"Achieved":"NotStarted",progression=new{timeUnlocked=unlocked},rarity=new{currentPercentage=rarity},mediaAssets=icon is null?Array.Empty<object>():new object[]{new{type="Icon",url=icon}},titleAssociations=new[]{new{id=titleId.ToString(CultureInfo.InvariantCulture)}}});
    }
}
