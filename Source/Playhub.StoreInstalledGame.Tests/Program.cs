// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.StoreLibrary;

static class Checks
{
 static int count;
 static void Check(bool value,string message){count++;if(!value)throw new InvalidOperationException(message);}
 static readonly StoreGameKey key=new(StoreKind.Epic,"owned-game");
 static readonly StoreCapabilities caps=new(StoreInstallKind.Direct,true,true,true,true,true);
 static StoreLibraryGame Game()=>new(key,"Owned Game",3,caps,true,true,@"F:\Playhub\InstallTests\Owned-UI");
 static StoreLibrarySnapshot Snap(long revision=10,bool installed=false)=>new(revision,DateTimeOffset.UtcNow,[new(StoreKind.Epic,StoreConnectionState.Connected,3,null,caps)],[Game()with{IsInstalled=installed,IsLaunchable=installed}],[]);
 static StoreInstalledGameStatus Status()=>new(key,3,10,true,true,true,Game().InstallPath);
 static StoreUninstallPreparation Prep(DateTimeOffset now)=>new(key,3,10,Game().InstallPath,Guid.NewGuid(),now.AddSeconds(30),true);
 static StoreLaunchDescriptor Descriptor()=>new(key,3,10,true,true,@"C:\Ignored\host.exe",null,["not-forwarded"],GameExecutablePath:@"F:\Playhub\InstallTests\Owned-UI\Game.exe");
 static async Task Main()
 {
  var now=DateTimeOffset.UtcNow;bool current=true;int launches=0;
  var service=new Fake();
  StoreInstalledGameController Controller()=>new(service,service,_=>current,(d,t)=>{launches++;return Task.FromResult(true);},()=>now);
  using(var c=Controller())
  {
   Check(await c.ReadStatusAsync(Game()) is not null,"fresh exact installation status");
   Check(await c.PlayAsync(Game())==StoreInstalledActionResult.Requested&&launches==1&&!c.Busy,"verified launch requests wrapper once");
   foreach(var invalid in new[]{Status()with{Key=new(StoreKind.Gog,"owned-game")},Status()with{AccountRevision=4},Status()with{LibraryRevision=-1},Status()with{InstallPath=@"F:\Foreign"},Status()with{IsInstalled=false},Status()with{ErrorCode="unavailable"}})
   {service.StatusValue=invalid;Check(await c.ReadStatusAsync(Game()) is null,"foreign or invalid status refused");}
   service.StatusValue=Status();
   foreach(var invalid in new[]{Descriptor()with{Key=new(StoreKind.Gog,"owned-game")},Descriptor()with{AccountRevision=4},Descriptor()with{LibraryRevision=9},Descriptor()with{IsInstalled=false},Descriptor()with{IsLaunchable=false},Descriptor()with{ErrorCode="refused"}})
   {service.DescriptorValue=invalid;Check(await c.PlayAsync(Game())==StoreInstalledActionResult.Stale&&launches==1,"fresh descriptor must match installation lineage");}
   service.DescriptorValue=Descriptor();service.DescriptorRead=()=>{current=false;return Task.FromResult(Descriptor());};Check(await c.PlayAsync(Game())==StoreInstalledActionResult.Stale&&launches==1,"descriptor await reentry cannot launch into a replaced view");current=true;service.DescriptorRead=null;service.StatusValue=Status()with{CanLaunch=false};Check(await c.PlayAsync(Game())==StoreInstalledActionResult.Unavailable&&launches==1,"no launch without capability");service.StatusValue=Status();
   service.Preparation=Prep(now);
   Check(await c.UninstallAsync(Game(),(p,t)=>Task.FromResult(false))==StoreInstalledActionResult.Canceled&&service.Uninstalls==0,"cancel never sends uninstall");
   Check(await c.UninstallAsync(Game(),(p,t)=>{Check(p==service.Preparation,"exact server preparation reaches dialog");return Task.FromResult(true);})==StoreInstalledActionResult.Completed&&service.Uninstalls==1,"exact confirmation and verified uninstalled snapshot");
   Check(service.SelectedPath==Game().InstallPath,"desktop removal sends the captured selected installation path to the scoped preparation boundary");
   int before=service.Uninstalls;
   foreach(var invalid in new[]{Prep(now)with{Nonce=null},Prep(now)with{Nonce=Guid.Empty},Prep(now)with{ExpiresAt=now},Prep(now)with{InstallPath=@"F:\Other"},Prep(now)with{AccountRevision=4},Prep(now)with{CanUninstall=false},Prep(now)with{ErrorCode="changed"}})
   {service.Preparation=invalid;Check(await c.UninstallAsync(Game(),(p,t)=>throw new Exception("must not prompt"))==StoreInstalledActionResult.Stale&&service.Uninstalls==before,"malformed preparation never prompts or commits");}
   service.Preparation=Prep(now);Check(await c.UninstallAsync(Game(),(p,t)=>{current=false;return Task.FromResult(true);})==StoreInstalledActionResult.Stale&&service.Uninstalls==before,"dialog reentry account/path replacement vetoes commit");current=true;
   service.Preparation=Prep(now);Check(await c.UninstallAsync(Game(),(p,t)=>{now=now.AddMinutes(1);return Task.FromResult(true);})==StoreInstalledActionResult.Stale&&service.Uninstalls==before,"dialog does not extend original nonce deadline");
   service.Preparation=Prep(now);service.Result=new(true,null,Snap(installed:true));Check(await c.UninstallAsync(Game(),(p,t)=>Task.FromResult(true))==StoreInstalledActionResult.Failed,"accepted reply alone never retires installed UI");
   service.Result=new(true,null,Snap(9));Check(await c.UninstallAsync(Game(),(p,t)=>Task.FromResult(true))==StoreInstalledActionResult.Failed,"older response cannot attest uninstall");
   service.Result=new(false,"changed",Snap());Check(await c.UninstallAsync(Game(),(p,t)=>Task.FromResult(true))==StoreInstalledActionResult.Failed,"server refusal never auto replays or succeeds");
   service.Result=new(true,null,Snap());
  }
  service=new Fake();var pending=new TaskCompletionSource<StoreInstalledGameStatus>(TaskCreationOptions.RunContinuationsAsynchronously);service.Read=_=>pending.Task;
  using(var c=Controller())
  {
   int prior=launches;var first=c.PlayAsync(Game());Check(c.Busy,"pending verified read owns action gate");Check(await c.UninstallAsync(Game(),(p,t)=>throw new Exception())==StoreInstalledActionResult.Busy&&service.Prepares==0,"second action cannot overlap pending launch");pending.SetResult(Status());Check(await first==StoreInstalledActionResult.Requested&&launches==prior+1,"one pending launch completes once");
  }
  service=new Fake();pending=new(TaskCreationOptions.RunContinuationsAsynchronously);service.Read=_=>pending.Task;
  using(var c=Controller()){var held=c.PlayAsync(Game());current=false;pending.SetResult(Status());Check(await held==StoreInstalledActionResult.Unavailable,"late read after view replacement gives no launch");}current=true;
  service=new Fake();pending=new(TaskCreationOptions.RunContinuationsAsynchronously);service.Read=_=>pending.Task;
  var disposed=Controller();var late=disposed.PlayAsync(Game());disposed.Dispose();pending.SetResult(Status());Check(await late==StoreInstalledActionResult.Unavailable,"closed view cannot launch after async read");
  Check(StoreInstalledGameController.Supported(Game()with{Key=new(StoreKind.Xbox,"9AAA00000001")}),"verified Xbox can query its real OS action capability");
  Check(!StoreInstalledGameController.Supported(Game()with{Key=new(StoreKind.Epic,"../escape")}),"wrapper key rejects traversal");
  var gogKey=new StoreGameKey(StoreKind.Gog,"123456");var offlineGame=Game()with{Key=gogKey};
  service=new Fake{StatusValue=Status()with{Key=gogKey,CanLaunch=true,CanUninstall=false},DescriptorValue=Descriptor()with{Key=gogKey}};
  using(var offline=Controller())
  {
   int prior=launches;Check(await offline.PlayAsync(offlineGame)==StoreInstalledActionResult.Requested&&launches==prior+1,"verified DRM-free GOG can play without connected-account policy");
   service.Preparation=Prep(now)with{Key=gogKey};
   Check(await offline.UninstallAsync(offlineGame,(p,t)=>Task.FromResult(false))==StoreInstalledActionResult.Canceled&&service.Prepares==1&&service.Uninstalls==0,"offline GOG requests real removal authority despite negative advisory status; cancel deletes nothing");
  }
  await LauncherChecks();Console.WriteLine($"PASS {count} installed-game UI coordinator/launcher checks; no process, browser, game or provider executed");
 }
 static async Task LauncherChecks()
 {
  var root=Path.Combine(Path.GetTempPath(),"Playhub-Inert-GameSession-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"StoreLibrary"));
  var exe=Path.Combine(root,"Playhub.GameSession.exe");await File.WriteAllTextAsync(exe,"inert bytes: never executed");var manifest=Path.Combine(root,"StoreLibrary","store-runtime.json");var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(exe)));
  void Write(bool finalized=true,string? pin=null,int clients=1)=>File.WriteAllText(manifest,JsonSerializer.Serialize(new{protocol=1,finalized,allowedClients=Enumerable.Range(0,clients).Select(_=>new{kind="app",path="Playhub.GameSession.exe",sha256=pin??hash})}));
  int calls=0;ProcessStartInfo? observed=null;var launcher=new StoreGameSessionLauncher(root,p=>{calls++;observed=p;return true;});Write();
  Check(await launcher.LaunchAsync(Descriptor(),default)&&calls==1,"pinned fixed wrapper starts through injected boundary");
  Check(observed!.FileName==exe&&!observed.UseShellExecute&&observed.CreateNoWindow&&observed.ArgumentList.SequenceEqual(new[]{"--store-library","epic","owned-game"}),"exact canonical wrapper argv; descriptor executable and arbitrary arguments ignored");
  Check(await launcher.LaunchAsync(Descriptor()with{Key=new(StoreKind.Gog,"123456")},default)&&observed!.ArgumentList.SequenceEqual(new[]{"--store-library","gog","123456"}),"verified GOG uses same fixed wrapper without external account launcher");
  Check(await launcher.LaunchAsync(Descriptor()with{Key=new(StoreKind.Amazon,"owned-game")},default)&&observed!.ArgumentList.SequenceEqual(new[]{"--store-library","amazon","owned-game"}),"verified Amazon uses its exact supported wrapper");
  Check(await launcher.LaunchAsync(Descriptor()with{Key=new(StoreKind.Xbox,"9AAA00000001")},default)&&observed!.ArgumentList.SequenceEqual(new[]{"--store-library","xbox","9AAA00000001"}),"verified Xbox uses pinned GameSession and exact product ID, never a guessed EXE");
  foreach(var id in new[]{"../x","a b","x;cmd","x\n",""})Check(!await launcher.LaunchAsync(Descriptor()with{Key=new(StoreKind.Epic,id)},default),"no unsafe argument accepted");
  int before=calls;Write(finalized:false);Check(!await launcher.LaunchAsync(Descriptor(),default)&&calls==before,"unfinalized manifest refuses launch");Write(clients:0);Check(!await launcher.LaunchAsync(Descriptor(),default),"missing exact client refuses launch");Write(clients:2);Check(!await launcher.LaunchAsync(Descriptor(),default),"ambiguous client refuses launch");Write(pin:new string('0',64));Check(!await launcher.LaunchAsync(Descriptor(),default),"modified/unpinned binary refuses launch");Write();await File.AppendAllTextAsync(exe,"modified");Check(!await launcher.LaunchAsync(Descriptor(),default)&&calls==before,"runtime replacement after packaging refuses launch");
  using var cancel=new CancellationTokenSource();cancel.Cancel();Check(!await launcher.LaunchAsync(Descriptor(),cancel.Token)&&calls==before,"canceled view starts nothing");
 }
 sealed class Fake:IStoreLibraryService,IStoreInstalledGameService,IStoreInstalledGamePathService
 {
  public StoreInstalledGameStatus StatusValue=Status();public StoreLaunchDescriptor DescriptorValue=Descriptor();public StoreUninstallPreparation Preparation=Prep(DateTimeOffset.UtcNow);public StoreCommandResult Result=new(true,null,Snap());public int Uninstalls,Prepares;
  public Func<CancellationToken,Task<StoreInstalledGameStatus>>? Read;public Func<Task<StoreLaunchDescriptor>>? DescriptorRead;
  public Task<StoreInstalledGameStatus> GetInstalledGameAsync(StoreGameKey k,CancellationToken t=default)=>Read?.Invoke(t)??Task.FromResult(StatusValue);
  public string? SelectedPath;
  public Task<StoreUninstallPreparation> PrepareUninstallAtPathAsync(StoreGameKey k,long a,long r,string path,CancellationToken t=default){SelectedPath=path;return PrepareUninstallAsync(k,a,r,t);}
  public Task<StoreUninstallPreparation> PrepareUninstallAsync(StoreGameKey k,long a,long r,CancellationToken t=default){Prepares++;return Task.FromResult(Preparation);}
  public Task<StoreCommandResult> UninstallAsync(StoreUninstallPreparation p,Guid id,CancellationToken t=default){Uninstalls++;return Task.FromResult(Result);}
  public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey k,CancellationToken t=default)=>DescriptorRead?.Invoke()??Task.FromResult(DescriptorValue);
  public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken t=default)=>Task.FromResult(Snap());
  public Task<StoreConnectSession> BeginConnectAsync(StoreKind s,CancellationToken t=default)=>throw new NotSupportedException();
  public Task<StoreCommandResult> CompleteConnectAsync(Guid i,string c,CancellationToken t=default)=>throw new NotSupportedException();
  public Task<StoreCommandResult> DisconnectAsync(StoreKind s,long r,CancellationToken t=default)=>throw new NotSupportedException();
  public Task<StoreCommandResult> RefreshAsync(StoreKind s,long r,CancellationToken t=default)=>throw new NotSupportedException();
  public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey k,long r,string? p=null,CancellationToken t=default)=>throw new NotSupportedException();
  public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest r,CancellationToken t=default)=>throw new NotSupportedException();
  public Task<StoreCommandResult> ControlAsync(StoreJobCommand r,CancellationToken t=default)=>throw new NotSupportedException();
 }
}


