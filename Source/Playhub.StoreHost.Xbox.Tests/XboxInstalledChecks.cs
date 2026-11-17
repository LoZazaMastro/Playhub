// SPDX-License-Identifier: MIT. Synthetic current-user registrations and no OS activation.
using System.Reflection;
using System.Text.Json;
using Playhub.StoreLibrary;
static class XboxInstalledChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.Parse("2026-11-14T12:00:00Z");int scans=0,opens=0;
        var mutable=new List<StoreXboxInstalledGame>{new("Synthetic.Game_abcdefghijklm!App","Synthetic game","Synthetic.Game_abcdefghijklm")};
        Func<CancellationToken,Task<XboxInstalledRead>> read=_=>{scans++;return Task.FromResult(new XboxInstalledRead(mutable,true));};
        var service=new StoreXboxInstalledLibrary("not-used",t=>read(t),_=>{opens++;return Task.FromResult(true);},()=>now);
        var empty=await service.GetXboxInstalledLibraryAsync();check(empty.Games.Count==0&&empty.ObservedAt is null&&empty.IsStale&&scans==0,"installed cache read never scans/signs in");
        check(empty.AccountState=="external-client-not-observed"&&empty.Scope=="current-windows-user-installed","installed scope is not account ownership");
        var first=await service.RefreshXboxInstalledLibraryAsync();check(scans==1&&first.Games.Count==1&&!first.IsStale&&first.OfficialAppInstalled,"injected exact local registration applied");
        check(first.Games[0].OwnershipState=="not-checked"&&first.Games[0].AccessState=="not-checked","local installation never grants purchase/access");
        mutable.Clear();check((await service.GetXboxInstalledLibraryAsync()).Games.Count==1,"snapshot clones mutable scanner result");
        var observed=first.ObservedAt;await service.GetXboxInstalledLibraryAsync();check(scans==1&&observed==(await service.GetXboxInstalledLibraryAsync()).ObservedAt,"read never mints observation freshness");
        var id=Guid.NewGuid();var opened=await service.OpenXboxAppAsync(new(first.Revision,id));check(opened.Accepted&&opened.State=="opened-official-client"&&opens==1,"explicit official client handoff only");
        check(opened.AccountState=="external-client-not-observed"&&(await service.GetXboxInstalledLibraryAsync()).AccountState==empty.AccountState,"handoff does not mark signed in");
        await service.OpenXboxAppAsync(new(first.Revision,id));check(opens==1,"same request cannot open client twice");
        check((await service.OpenXboxAppAsync(new(first.Revision+1,id))).ErrorCode=="request_identity_conflict","changed request tuple cannot reuse id");
        check(!(await service.OpenXboxAppAsync(new(first.Revision,Guid.Empty))).Accepted,"empty handoff identity refused");
        check(!(await service.OpenXboxAppAsync(new(first.Revision+1,Guid.NewGuid()))).Accepted&&opens==1,"stale snapshot cannot hand off");
        read=_=>throw new IOException("private raw source text");var failed=await service.RefreshXboxInstalledLibraryAsync();check(failed.Games.Count==1&&failed.Revision==first.Revision&&failed.IsStale&&failed.ErrorCode=="installed_scan_failed","failed scan keeps prior snapshot and sanitizes error");
        check(!(await service.OpenXboxAppAsync(new(first.Revision,Guid.NewGuid()))).Accepted,"failed/unknown observation cannot open using stale proof");
        read=_=>Task.FromResult(new XboxInstalledRead([new("Bad!App","Bad","Other")],true));var bad=await service.RefreshXboxInstalledLibraryAsync();check(bad.ErrorCode=="installed_scan_invalid"&&bad.Games.Count==1,"mismatched family identity refused");
        read=_=>Task.FromResult(new XboxInstalledRead([],true,true));var partial=await service.RefreshXboxInstalledLibraryAsync();check(partial.ErrorCode=="installed_scan_partial"&&partial.Games.Count==1,"partial scan cannot clear prior library");
        read=_=>Task.FromResult(new XboxInstalledRead([],false));var none=await service.RefreshXboxInstalledLibraryAsync();check(none.Games.Count==0&&!none.IsStale&&!none.OfficialAppInstalled,"successful local empty is distinct from unknown failure");
        check((await service.OpenXboxAppAsync(new(none.Revision,Guid.NewGuid()))).ErrorCode=="official_xbox_app_unavailable","missing official app never launches arbitrary URI/executable");
        var source=new TaskCompletionSource<XboxInstalledRead>(TaskCreationOptions.RunContinuationsAsynchronously);int concurrentScans=0;
        var shared=new StoreXboxInstalledLibrary("not-used",_=>{Interlocked.Increment(ref concurrentScans);return source.Task;},_=>Task.FromResult(false),()=>now);
        using var cancel=new CancellationTokenSource();var one=shared.RefreshXboxInstalledLibraryAsync(cancel.Token);var two=shared.RefreshXboxInstalledLibraryAsync();cancel.Cancel();
        try{await one;throw new Exception("caller cancel missing");}catch(OperationCanceledException){}
        source.SetResult(new([],true));await two;check(concurrentScans==1,"concurrent refresh is one owned scan and caller cancel does not corrupt it");
        now=now.AddMinutes(6);check((await shared.GetXboxInstalledLibraryAsync()).IsStale,"cached local observation expires without refresh");
        check(!(await shared.OpenXboxAppAsync(new(1,Guid.NewGuid()))).Accepted,"expired snapshot cannot hand off");
        var json=JsonSerializer.SerializeToElement(new StoreXboxAppRequest(1,Guid.NewGuid()),StoreLibraryService.JsonOptions);
        var route=await ((IStoreProtocolExtension)shared).TryHandleAsync("xbox-installed",json,CancellationToken.None);check(route.Handled&&route.Data is StoreXboxInstalledSnapshot,"read RPC has typed local snapshot");
        check(!(await ((IStoreProtocolExtension)shared).TryHandleAsync("xbox-signout-system",json,CancellationToken.None)).Handled,"no system signout route exists");
        var type=typeof(StoreXboxInstalledLibrary).Assembly.GetType("Playhub.StoreLibrary.WindowsXboxInstalledReader")!;
        var parse=type.GetMethod("Parse",BindingFlags.NonPublic|BindingFlags.Static)!;
        var parsed=(XboxInstalledRead)parse.Invoke(null,["{\"Version\":1,\"Games\":[],\"Partial\":false,\"OfficialAppInstalled\":false}"])!;
        check(parsed.Games.Count==0&&!parsed.OfficialAppInstalled,"actual production parser handles empty array without fake auth");
        var script=(string)type.GetField("Script",BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue()!;
        check(script.Contains("Get-AppxPackage")&&!script.Contains("-AllUsers")&&!script.Contains("Remove-AppxPackage")&&!script.Contains("Add-AppxPackage")&&!script.Contains("Get-Credential"),"actual scanner is current-user read-only, no auth/package writes");
        check(script.Contains("Microsoft.GamingApp_8wekyb3d8bbwe")&&script.Contains("Microsoft.Xbox.App")&&script.Contains("$app.SignatureKind -eq 'Store'"),"official app requires exact registered Microsoft family/app/signature");
    }
}
