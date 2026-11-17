// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Playhub.StoreLibrary;

public sealed record XboxInstalledRead(IReadOnlyList<StoreXboxInstalledGame> Games,bool OfficialAppInstalled,bool Partial=false);
// Local registrations and an official-client handoff, never an authenticated account provider.
public sealed class StoreXboxInstalledLibrary : IStoreXboxInstalledService,IStoreProtocolExtension
{
    readonly object sync=new();readonly Func<CancellationToken,Task<XboxInstalledRead>> scan;
    readonly Func<CancellationToken,Task<bool>> open;readonly Func<DateTimeOffset> now;
    StoreXboxInstalledSnapshot snapshot=new(0,null,Array.Empty<StoreXboxInstalledGame>(),false,true,ErrorCode:"installed_not_scanned");
    Task<StoreXboxInstalledSnapshot>? refreshing;
    readonly Dictionary<Guid,(long Revision,StoreXboxAppResult Result)> requests=[];
    public StoreXboxInstalledLibrary(string privateDirectory,Func<CancellationToken,Task<XboxInstalledRead>>? reader=null,
        Func<CancellationToken,Task<bool>>? officialApp=null,Func<DateTimeOffset>? clock=null)
    {
        scan=reader??new WindowsXboxInstalledReader(privateDirectory).ReadAsync;
        open=officialApp??WindowsXboxInstalledReader.OpenOfficialAppAsync;now=clock??(()=>DateTimeOffset.UtcNow);
    }
    StoreXboxInstalledSnapshot Current()=>snapshot with{IsStale=snapshot.IsStale||snapshot.ObservedAt is null||now()<snapshot.ObservedAt||now()-snapshot.ObservedAt>TimeSpan.FromMinutes(5)};
    public Task<StoreXboxInstalledSnapshot> GetXboxInstalledLibraryAsync(CancellationToken cancellationToken=default)
    {cancellationToken.ThrowIfCancellationRequested();lock(sync)return Task.FromResult(Current());}
    public Task<StoreXboxInstalledSnapshot> RefreshXboxInstalledLibraryAsync(CancellationToken cancellationToken=default)
    {
        Task<StoreXboxInstalledSnapshot> held;
        lock(sync)
        {
            if(refreshing is not null)held=refreshing;
            else
            {
                held=Task.Run(RefreshOwnedAsync);refreshing=held;
                _=held.ContinueWith(_=>{lock(sync)if(ReferenceEquals(refreshing,held))refreshing=null;},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
        }
        return held.WaitAsync(cancellationToken);
    }
    async Task<StoreXboxInstalledSnapshot> RefreshOwnedAsync()
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            var result=await scan(deadline.Token);
            if(result.Partial)throw new StoreOperationException("installed_scan_partial");
            if(result.Games.Count>2000||result.Games.Any(g=>!Valid(g))||result.Games.Select(g=>g.Aumid).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=result.Games.Count)
                throw new StoreOperationException("installed_scan_invalid");
            var games=Array.AsReadOnly(result.Games.OrderBy(g=>g.Title,StringComparer.OrdinalIgnoreCase).ThenBy(g=>g.Aumid,StringComparer.Ordinal).ToArray());
            lock(sync)return snapshot=new(snapshot.Revision+1,now(),games,result.OfficialAppInstalled,false);
        }
        catch(Exception e)when(e is not OutOfMemoryException)
        {
            string code=e is StoreOperationException s&&s.Code is "installed_scan_partial" or "installed_scan_invalid"?s.Code:e is OperationCanceledException?"installed_scan_timeout":"installed_scan_failed";
            lock(sync)return snapshot=snapshot with{IsStale=true,ErrorCode=code};
        }
    }
    static bool Valid(StoreXboxInstalledGame g)=>g is not null&&g.Title is {Length:>0 and <=500}&&!g.Title.Any(char.IsControl)
        &&g.PackageFamilyName is {Length:>0 and <=255}&&Regex.IsMatch(g.PackageFamilyName,@"^[A-Za-z0-9._-]+$")
        &&g.Aumid.StartsWith(g.PackageFamilyName+"!",StringComparison.Ordinal)&&g.Aumid.Length<=512
        &&Regex.IsMatch(g.Aumid[(g.PackageFamilyName.Length+1)..],@"^[A-Za-z0-9._-]+$")
        &&g.InstalledState=="registered-current-user"&&g.OwnershipState=="not-checked"&&g.AccessState=="not-checked";
    public async Task<StoreXboxAppResult> OpenXboxAppAsync(StoreXboxAppRequest request,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock(sync)
        {
            if(request.RequestId==Guid.Empty)return Refused(request,"request_invalid");
            if(requests.TryGetValue(request.RequestId,out var old))return old.Revision==request.ExpectedRevision?old.Result:Refused(request,"request_identity_conflict");
            var current=Current();
            if(current.IsStale||current.Revision!=request.ExpectedRevision)return Refused(request,"installed_snapshot_changed");
            if(!current.OfficialAppInstalled)return Refused(request,"official_xbox_app_unavailable");
            if(requests.Count>=256)return Refused(request,"request_history_full");
            requests.Add(request.RequestId,(request.ExpectedRevision,new(false,"opening-official-client",null,request.RequestId)));
        }
        StoreXboxAppResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result=await open(cancellationToken)?new(true,"opened-official-client",null,request.RequestId):Refused(request,"official_xbox_app_open_failed");
        }
        catch(Exception e)when(e is not OutOfMemoryException){result=Refused(request,e is OperationCanceledException?"request_canceled":"official_xbox_app_open_failed");}
        lock(sync){requests[request.RequestId]=(request.ExpectedRevision,result);return result;}
    }
    static StoreXboxAppResult Refused(StoreXboxAppRequest request,string reason)=>new(false,"not-opened",reason,request.RequestId);
    public async Task<(bool Handled,object? Data)> TryHandleAsync(string method,JsonElement args,CancellationToken cancellationToken)
        =>method switch
        {
            "xbox-installed"=>(true,await GetXboxInstalledLibraryAsync(cancellationToken)),
            "xbox-installed-refresh"=>(true,await RefreshXboxInstalledLibraryAsync(cancellationToken)),
            "xbox-open-official"=>(true,await OpenXboxAppAsync(args.Deserialize<StoreXboxAppRequest>(StoreLibraryService.JsonOptions)??throw new StoreOperationException("request_invalid"),cancellationToken)),
            _=>(false,null)
        };
}
