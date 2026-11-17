// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Globalization;
using System.Net;
using System.Text.Json;
namespace Playhub.StoreLibrary;

internal sealed record StoreXboxAchievements(string ProductId,long AccountRevision,string TitleId,string Title,DateTimeOffset ObservedAt,JsonElement[] Achievements);

public sealed partial class StoreXboxAccountProvider
{
    internal async Task<StoreXboxAchievements> ReadXboxAchievementsAsync(string productId,long revision,string language,CancellationToken token)
    {
        if(!StoreXboxAccountCollections.ValidId(productId)||!System.Text.RegularExpressions.Regex.IsMatch(language,"^[a-z]{2,3}(?:-[A-Za-z]{2,4})?$"))throw new StoreOperationException("request_invalid");
        var snapshot=await Engine.GetSnapshotAsync(token);
        var game=snapshot.Games.SingleOrDefault(g=>g.Key==new StoreGameKey(StoreKind.Xbox,productId)&&g.AccountRevision==revision&&g.IsInstalled&&g.InstallPath is not null);
        if(game is null)throw new StoreOperationException("owned_library_changed_or_unavailable");
        await ReadOwnedLaunchAsync(productId,game.InstallPath!,token);
        return await WithAccountAuthorizationAsync(revision,async(account,ct)=>{
            var owner=LaunchOwner(productId);string id=owner.InstallProductId??productId;
            // Exact Store product metadata, never a title search or another
            // profile's history. The fresh account fence also covers paging.
            var(status,metadata)=await auth.Send("https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds="+id+"&market=US&languages="+Uri.EscapeDataString(language),null,ct,method:HttpMethod.Get);
            if(status!=HttpStatusCode.OK||!metadata.TryGetProperty("Products",out var products)||products.ValueKind!=JsonValueKind.Array)throw new StoreOperationException("xbox_achievements_title_unavailable");
            var exact=products.EnumerateArray().Where(p=>p.TryGetProperty("ProductId",out var pid)&&pid.GetString()==id).ToArray();
            if(exact.Length!=1)throw new StoreOperationException("xbox_achievements_title_unavailable");
            uint? title=null;
            if(exact[0].TryGetProperty("AlternateIds",out var aliases)&&aliases.ValueKind==JsonValueKind.Array)
                foreach(var alias in aliases.EnumerateArray())
                    if(alias.TryGetProperty("IdType",out var type)&&type.GetString()=="XboxTitleId"&&alias.TryGetProperty("Value",out var value)&&uint.TryParse(value.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out uint parsed)&&parsed>0){if(title is not null&&title!=parsed)throw new StoreOperationException("xbox_achievements_title_unavailable");title=parsed;}
            if(title is null)throw new StoreOperationException("xbox_achievements_title_unavailable");
            var rows=await auth.ReadLiveAchievementsAsync(account,title.Value,language,ct);
            var latest=LaunchOwner(productId);
            if(!ReferenceEquals(latest.Account,owner.Account)||latest.Family!=owner.Family||latest.InstallProductId!=owner.InstallProductId)throw new StoreOperationException("account_changed");
            return new StoreXboxAchievements(productId,revision,title.Value.ToString(CultureInfo.InvariantCulture),game.Title,now(),rows);
        },token);
    }
}

