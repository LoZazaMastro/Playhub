// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Playhub.StoreLibrary;

public sealed record StoreAchievement(string Key,string Name,string Description,string? ImageUnlocked,string? ImageLocked,
    bool Hidden,bool Unlocked,long? UnlockedAt,double? Rarity);
public sealed record StoreAchievementsSnapshot(StoreGameKey Key,long AccountRevision,string Title,DateTimeOffset ObservedAt,
    string Scope,IReadOnlyList<StoreAchievement> Achievements);
public interface IStoreAchievementsProvider
{
    Task<IReadOnlyList<StoreAchievement>> ReadAchievementsAsync(StoreLibraryGame game,string language,CancellationToken token);
}

// A game's last confirmed progress survives normal game-stop and client restart.
// Both the private account identity AND its durable revision bind every entry.
internal sealed class StoreAchievementCache
{
    private sealed record Entry(int Version,string Identity,string Language,StoreAchievementsSnapshot Snapshot);
    private readonly string directory;
    private readonly Func<DateTimeOffset> now;
    private readonly ConcurrentDictionary<string,Lazy<Task<StoreAchievementsSnapshot>>> pending=new();
    internal StoreAchievementCache(string directory,Func<DateTimeOffset>? clock=null)
    {this.directory=Path.GetFullPath(directory);StoreCredentialVault.PrivateDirectory(this.directory);now=clock??(()=>DateTimeOffset.UtcNow);}
    private static string Digest(string identity,StoreGameKey key,long revision,string language)
        =>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{identity,key,revision,language},StoreLibraryService.JsonOptions))));
    internal async Task<StoreAchievementsSnapshot?> ReadAsync(string identity,StoreLibraryGame game,string language,bool force,bool cachedOnly,
        Func<CancellationToken,Task<IReadOnlyList<StoreAchievement>>> read,Func<CancellationToken,Task<bool>> current,CancellationToken token)
    {
        string digest=Digest(identity,game.Key,game.AccountRevision,language),path=Path.Combine(directory,digest+".json");
        if(!force||cachedOnly)
        {
            try
            {
                var file=new FileInfo(path);
                if(file.Exists&&file.Length is >0 and <=8*1024*1024&&(file.Attributes&FileAttributes.ReparsePoint)==0)
                {
                    var saved=JsonSerializer.Deserialize<Entry>(await File.ReadAllBytesAsync(path,token),StoreLibraryService.JsonOptions);
                    if(saved is {Version:1}&&saved.Identity==identity&&saved.Language==language&&saved.Snapshot.Key==game.Key
                        &&saved.Snapshot.AccountRevision==game.AccountRevision&&saved.Snapshot.Title==game.Title
                        &&saved.Snapshot.ObservedAt<=now().AddMinutes(5)
                        &&saved.Snapshot.Scope==Scope(game.Key,game.AccountRevision,language)
                        &&Valid(saved.Snapshot.Achievements,game.Key.Store)&&await current(token))return saved.Snapshot;
                }
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){/* A damaged cache is a miss, never a zero-progress result. */}
        }
        if(cachedOnly)return null;
        Lazy<Task<StoreAchievementsSnapshot>>? operation=null;
        operation=pending.GetOrAdd(digest,_=>new(()=>Load(),LazyThreadSafetyMode.ExecutionAndPublication));
        var task=operation.Value;var held=operation;
        _=task.ContinueWith(_=>pending.TryRemove(new KeyValuePair<string,Lazy<Task<StoreAchievementsSnapshot>>>(digest,held)),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        try{return await task.WaitAsync(token);}
        finally{if(operation.IsValueCreated&&operation.Value.IsCompleted)pending.TryRemove(new KeyValuePair<string,Lazy<Task<StoreAchievementsSnapshot>>>(digest,operation));}
        async Task<StoreAchievementsSnapshot> Load()
        {
            using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(55));var stop=budget.Token;
            if(!await current(stop))throw new StoreOperationException("account_changed");
            var rows=(await read(stop)).ToArray();
            if(!Valid(rows,game.Key.Store))throw new StoreOperationException("achievements_response_invalid");
            if(!await current(stop))throw new StoreOperationException("account_changed");
            var value=new StoreAchievementsSnapshot(game.Key,game.AccountRevision,game.Title,now(),Scope(game.Key,game.AccountRevision,language),Array.AsReadOnly(rows));
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Entry(1,identity,language,value),StoreLibraryService.JsonOptions);
            if(bytes.Length>8*1024*1024)throw new StoreOperationException("achievements_response_too_large");
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary,bytes,stop);
                if(!await current(stop))throw new StoreOperationException("account_changed");
                File.Move(temporary,path,true);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return value;
        }
    }
    internal static string Scope(StoreGameKey key,long revision,string language)=>$"{key.Store.ToString().ToLowerInvariant()}:{key.Id}:{revision}:{language}";
    internal static bool Valid(IReadOnlyList<StoreAchievement>? rows,StoreKind store)=>rows is {Count:<=2000}
        &&rows.All(r=>r is not null&&r.Key is {Length:>0 and <=256}&&r.Name is {Length:>0 and <=1024}&&r.Description is {Length:<=8192}
            &&(r.ImageUnlocked is null||StoreAchievementNormalization.Image(r.ImageUnlocked,store)==r.ImageUnlocked)
            &&(r.ImageLocked is null||StoreAchievementNormalization.Image(r.ImageLocked,store)==r.ImageLocked)
            &&(r.Rarity is null||double.IsFinite(r.Rarity.Value)&&r.Rarity>=0&&r.Rarity<=100)
            &&(r.UnlockedAt is null||r.Unlocked&&r.UnlockedAt>=978307200&&r.UnlockedAt<=32503680000))
        &&rows.Select(r=>r.Key).Distinct(StringComparer.Ordinal).Count()==rows.Count;
}

internal sealed class StoreAchievementsProtocol: IStoreProtocolExtension
{
    private readonly StoreQueueEngine engine;private readonly IReadOnlyDictionary<StoreKind,IStoreProvider> providers;
    private readonly StoreAchievementCache cache;
    internal StoreAchievementsProtocol(StoreQueueEngine engine,IReadOnlyList<IStoreProvider> providers,string state)
    {this.engine=engine;this.providers=providers.ToDictionary(p=>p.Store);cache=new(Path.Combine(state,"achievements-v1"));}
    public async Task<(bool Handled,object? Data)> TryHandleAsync(string method,JsonElement args,CancellationToken token)
    {
        if(method!="store-achievements")return(false,null);
        var key=args.GetProperty("key").Deserialize<StoreGameKey>(StoreLibraryService.JsonOptions)??throw new StoreOperationException("request_invalid");
        long revision=args.GetProperty("expectedAccountRevision").GetInt64();string language=args.TryGetProperty("language",out var lang)?lang.GetString()??"en-US":"en-US";
        if(key.Store is not(StoreKind.Epic or StoreKind.Gog or StoreKind.Xbox)||string.IsNullOrEmpty(key.Id)||key.Id.Length>250
            ||key.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('-' or '_' or '.'))||!Regex.IsMatch(language,"^[a-z]{2,3}(?:-[A-Za-z]{2,4})?$"))throw new StoreOperationException("request_invalid");
        if(!providers.TryGetValue(key.Store,out var provider)||provider is not IStoreAchievementsProvider source)throw new StoreOperationException("achievements_unavailable");
        var snapshot=await engine.GetSnapshotAsync(token);var game=snapshot.Games.SingleOrDefault(g=>g.Key==key&&g.IsInstalled&&g.AccountRevision==revision);
        async Task<string?> Identity(CancellationToken stop)=>provider is IStoreAchievementAccount local?local.ReadAchievementAccountIdentity():(await provider.GetAccountAsync(stop))?.AccountIdentity;
        var account=await Identity(token);
        if(game is null||account is null||!snapshot.Accounts.Any(a=>a.Store==key.Store&&a.State==StoreConnectionState.Connected&&a.Revision==revision))throw new StoreOperationException("account_changed");
        async Task<bool> Current(CancellationToken stop)
        {
            var next=await engine.GetSnapshotAsync(stop);
            return next.Accounts.Any(a=>a.Store==key.Store&&a.State==StoreConnectionState.Connected&&a.Revision==revision)
                &&next.Games.Any(g=>g.Key==key&&g.IsInstalled&&g.AccountRevision==revision&&g.InstallPath==game.InstallPath)
                &&await Identity(stop)==account;
        }
        var result=await cache.ReadAsync(account,game,language,args.TryGetProperty("forceRefresh",out var force)&&force.ValueKind==JsonValueKind.True,
            args.TryGetProperty("cachedOnly",out var only)&&only.ValueKind==JsonValueKind.True,ct=>source.ReadAchievementsAsync(game,language,ct),Current,token);
        if(!await Current(token))throw new StoreOperationException("account_changed");
        return(true,result??(object)JsonSerializer.SerializeToElement<object?>(null));
    }
}
