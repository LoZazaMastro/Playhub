// SPDX-License-Identifier: GPL-3.0-or-later
// Epic/GOG achievement response normalization adapted from Unifideck 0.8.0.
// Copyright (c) Unifideck contributors; Windows adaptation (c) 2026 LoZazaMastro.
// See Runtime/Notices/Unifideck-0.8.0-NOTICE.txt and GPL license accompanying it.
using System.Globalization;
using System.Text.Json;
namespace Playhub.StoreLibrary;

internal static class StoreAchievementNormalization
{
    internal static string Text(JsonElement row,string key,int max=8192)
        =>row.ValueKind==JsonValueKind.Object&&row.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString() is {} s&&s.Length<=max?s:"";
    internal static double? Percent(JsonElement value)
    {
        double n;
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out n)||value.ValueKind==JsonValueKind.String&&double.TryParse(value.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out n))
            return double.IsFinite(n)&&n>=0&&n<=100?n:null;
        return null;
    }
    internal static long? Timestamp(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out long seconds))return seconds is >=978307200 and <=32503680000?seconds:null;
        if(value.ValueKind==JsonValueKind.String&&DateTimeOffset.TryParse(value.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var stamp)&&stamp.Year is >=2001 and <3000)return stamp.ToUnixTimeSeconds();
        return null;
    }
    internal static string? Image(string? text,StoreKind store)
    {
        if(text is not {Length:>0 and <=2048}||!Uri.TryCreate(text,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo.Length!=0||!u.IsDefaultPort)return null;
        string h=u.IdnHost;
        bool match=store switch{
            StoreKind.Xbox=>h is "images-eds-ssl.xboxlive.com" or "images-eds.xboxlive.com" or "store-images.s-microsoft.com",
            StoreKind.Epic=>h=="epicgames.com"||h.EndsWith(".epicgames.com",StringComparison.Ordinal),
            StoreKind.Gog=>h=="gog-statics.com"||h.EndsWith(".gog-statics.com",StringComparison.Ordinal)||h=="images.gog.com",
            _=>false};
        return match?u.AbsoluteUri:null;
    }
    internal static IReadOnlyList<StoreAchievement> Xbox(IEnumerable<JsonElement> rows)
    {
        var result=new List<StoreAchievement>();
        foreach(var r in rows)
        {
            string key=Text(r,"id",256),name=Text(r,"name",1024);if(key.Length==0||name.Length==0)continue;
            bool unlocked=Text(r,"progressState")=="Achieved";
            long? time=r.TryGetProperty("progression",out var p)&&p.ValueKind==JsonValueKind.Object&&p.TryGetProperty("timeUnlocked",out var t)?Timestamp(t):null;
            double? rarity=r.TryGetProperty("rarity",out var rate)&&rate.ValueKind==JsonValueKind.Object&&rate.TryGetProperty("currentPercentage",out var v)?Percent(v):null;
            string? icon=null;if(r.TryGetProperty("mediaAssets",out var assets)&&assets.ValueKind==JsonValueKind.Array)
                foreach(var a in assets.EnumerateArray())if(Text(a,"type")=="Icon"&&Image(Text(a,"url",2048),StoreKind.Xbox) is {} safe){icon=safe;break;}
            result.Add(new(key,name,Text(r,unlocked?"description":"lockedDescription"),icon,icon,r.TryGetProperty("isSecret",out var secret)&&secret.ValueKind==JsonValueKind.True,unlocked,unlocked?time:null,rarity));
        }
        return result;
    }
    internal static IReadOnlyList<StoreAchievement> Epic(JsonElement definitions,JsonElement state)
    {
        var unlocked=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        if(state.ValueKind==JsonValueKind.Array)
            foreach(var wrapper in state.EnumerateArray())if(wrapper.TryGetProperty("playerAchievement",out var row)&&Text(row,"achievementName",256) is {Length:>0} id)unlocked[id]=row;
        var result=new List<StoreAchievement>();
        foreach(var wrapper in definitions.EnumerateArray())
        {
            if(!wrapper.TryGetProperty("achievement",out var r))continue;
            string id=Text(r,"name",256);if(id.Length==0)continue;
            unlocked.TryGetValue(id,out var progress);bool earned=progress.ValueKind==JsonValueKind.Object&&progress.TryGetProperty("unlocked",out var u)&&u.ValueKind==JsonValueKind.True;
            string name=Text(r,"unlockedDisplayName",1024);if(name.Length==0)name=Text(r,"lockedDisplayName",1024);if(name.Length==0)name=id;
            string description=Text(r,earned?"unlockedDescription":"lockedDescription");if(description.Length==0)description=Text(r,"unlockedDescription");
            var icon=Image(Text(r,"unlockedIconLink",2048),StoreKind.Epic);var locked=Image(Text(r,"lockedIconLink",2048),StoreKind.Epic)??icon;
            var time=earned&&progress.TryGetProperty("unlockDate",out var stamp)?Timestamp(stamp):null;
            var rarity=r.TryGetProperty("rarity",out var rarityValue)&&rarityValue.ValueKind==JsonValueKind.Object&&rarityValue.TryGetProperty("percent",out var percent)?Percent(percent):null;
            result.Add(new(id,name,description,icon,locked,r.TryGetProperty("hidden",out var hidden)&&hidden.ValueKind==JsonValueKind.True,earned,time,rarity));
        }
        return result;
    }
    internal static StoreAchievement? Gog(JsonElement r)
    {
        string id=Text(r,"achievement_key",256);if(id.Length==0)id=Text(r,"achievement_id",256);if(id.Length==0)return null;
        string name=Text(r,"name",1024);if(name.Length==0)name=id;
        bool earned=r.TryGetProperty("date_unlocked",out var date)&&date.ValueKind is not(JsonValueKind.Null or JsonValueKind.Undefined);
        string? icon=Image(Text(r,"image_url_unlocked",2048),StoreKind.Gog),locked=Image(Text(r,"image_url_locked",2048),StoreKind.Gog)??icon;
        return new(id,name,Text(r,"description"),icon,locked,r.TryGetProperty("visible",out var visible)&&visible.ValueKind==JsonValueKind.False,earned,earned?Timestamp(date):null,r.TryGetProperty("rarity",out var rarity)?Percent(rarity):null);
    }
}
