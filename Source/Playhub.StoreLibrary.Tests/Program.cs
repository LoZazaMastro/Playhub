using Playhub.StoreLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Playhub.Services;

static class Program
{
 static int count; static void Check(bool value,string name){count++;if(!value)throw new Exception(name);}
 static readonly StoreGameKey key=new(StoreKind.Epic,"namespace:game");
 static readonly StoreCapabilities caps=new(StoreInstallKind.Direct,true,true,true,true,true);
 static StoreLibraryGame Game(long account=1,bool installed=false)=>new(key,"Original title",account,caps,installed,installed);
 static StoreAccount Account(long revision=1)=>new(StoreKind.Epic,StoreConnectionState.Connected,revision,null,caps);
 static StoreLibrarySnapshot Snap(long revision=1,StoreLibraryGame? game=null,StoreDownloadJob[]? jobs=null)=>new(revision,DateTimeOffset.UtcNow,[Account()],[game??Game()],jobs??[]);
 static StoreDownloadJob Job(StoreJobState state=StoreJobState.Downloading)=>new(Guid.NewGuid(),key,"Title",state,1,1,@"C:\Games\Title",50,100,10,DateTimeOffset.UtcNow,true,true,true);
 static StoreInstallPreparation Prep(StoreGameKey? k=null,Guid? nonce=null,string? path=@"C:\Games\Title")=>new(k??key,1,3,nonce,DateTimeOffset.UtcNow.AddMinutes(5),100,path,path,[new(@"C:\","Drive",1000,true)],StoreInstallKind.Direct);
 static async Task Main()
 {
  var fake=new Service();using var c=new StoreLibraryController(fake);await c.ReadAsync();Check(c.Snapshot.Revision==1,"actual service read");Check(c.CanInstall(c.Snapshot.Games[0]),"owned direct install enabled");
  var old=new TaskCompletionSource<StoreLibrarySnapshot>();fake.Read=_=>old.Task;var reading=c.ReadAsync();await c.DisconnectAsync(StoreKind.Epic);old.SetResult(new(1,DateTimeOffset.UtcNow,[],[Game()],[]));await reading;Check(c.Snapshot.Revision==2,"late read cannot overwrite command epoch");Check(fake.Disconnects==1,"disconnect exact service call");
  fake.Read=_=>Task.FromResult(Snap(3));await c.ReadAsync();var g=c.Snapshot.Games[0];
  Check(!await c.InstallAsync(g,Prep(nonce:Guid.NewGuid()),"en"),"forged preparation no enqueue");Check(fake.Enqueues==0,"no implicit install");
  fake.Prepare=(k,a,p,ct)=>Task.FromResult(Prep(path:p));var first=await c.PrepareInstallAsync(g);Check(first is not null,"initial choices from service");Check(!await c.InstallAsync(g,first!,"en"),"suggested path without nonce cannot install");
  fake.Prepare=(k,a,p,ct)=>Task.FromResult(Prep(nonce:Guid.NewGuid(),path:p));var selected=await c.PrepareInstallAsync(g,@"C:\Games\Title");Check(selected?.SelectionNonce is not null,"selected path validated by service");
  var delayed=new TaskCompletionSource<StoreCommandResult>();fake.Enqueue=(r,ct)=>{Check(r.SelectionNonce==selected!.SelectionNonce,"exact selection nonce");Check(r.ExpectedPreparationRevision==3&&r.ExpectedAccountRevision==1,"exact prepared/account revision");Check(r.InstallPath==@"C:\Games\Title"&&r.Key==key,"canonical game and selected path");return delayed.Task;};
  var install=c.InstallAsync(g,selected!,"en");Check(c.Busy,"pending is busy, not completed");Check(!await c.InstallAsync(g,selected!,"en"),"duplicate click cannot enqueue twice");Check(fake.Enqueues==1,"one actual enqueue");Check(!c.Snapshot.Games[0].IsInstalled,"pending never invents installed state");delayed.SetResult(new(true,null,Snap(4,jobs:[Job()])));Check(await install,"acknowledged enqueue accepted");Check(!await c.InstallAsync(c.Snapshot.Games[0],selected!,"en"),"consumed selection cannot replay");
  var job=c.Snapshot.Jobs[0];var pause=new TaskCompletionSource<StoreCommandResult>();fake.Control=(r,ct)=>{Check(r.JobId==job.JobId&&r.ExpectedRevision==job.Revision&&r.Action==StoreJobAction.Pause,"exact job/revision command");return pause.Task;};var pausing=c.ControlAsync(job,StoreJobAction.Pause);
  Check(c.Snapshot.Jobs[0].State==StoreJobState.Downloading,"no optimistic paused state");Check(!c.CanControl(job,StoreJobAction.Cancel),"no overlapping command");pause.SetResult(new(true,null,Snap(5,jobs:[job with{State=StoreJobState.PausedByUser,Revision=2}])));Check(await pausing&&c.Snapshot.Jobs[0].State==StoreJobState.PausedByUser,"paused only from service ACK");
  var malformed=Snap(6,jobs:[job with{DownloadedBytes=-1}]);fake.Read=_=>Task.FromResult(malformed);await c.ReadAsync();Check(c.Snapshot.Revision==5&&c.ErrorKey is not null,"invalid progress preserves confirmed snapshot");
  fake.Read=_=>throw new Exception("secret provider output never UI");await c.ReadAsync();Check(c.ErrorKey=="StoreLibrary.ReadFailed","bounded error key only");
  fake.Read=_=>Task.FromResult(Snap(7,Game(installed:true)));await c.ReadAsync();Check(!c.CanInstall(c.Snapshot.Games[0]),"installed cannot pretend new install");
  var external=Game()with{Capabilities=caps with{InstallKind=StoreInstallKind.ExternalHandoff}};fake.Read=_=>Task.FromResult(Snap(8,external));await c.ReadAsync();Check(!c.CanInstall(c.Snapshot.Games[0]),"unsupported external enqueue never direct install");
  Check(StoreLibraryPresentation.Percent(job with{TotalBytes=null}) is null,"unknown total indeterminate");Check(StoreLibraryPresentation.Percent(job with{TotalBytes=0}) is null,"zero total indeterminate");Check(StoreLibraryPresentation.Percent(job with{DownloadedBytes=150})==100,"display bound does not imply completed");Check(!StoreLibraryController.Valid(Snap(9,jobs:[job with{BytesPerSecond=double.NaN}])) ,"NaN speed refused");Check(!StoreLibraryController.Valid(Snap(9,jobs:[job,job])),"duplicate job identity refused");Check(!StoreLibraryController.Valid(Snap(9,Game()with{IsInstalled=false,IsLaunchable=true})),"launchable needs installed fact");
  fake.Read=_=>Task.FromResult(Snap(10));await c.ReadAsync();fake.Connect=(s,ct)=>Task.FromResult(new StoreConnectSession(Guid.NewGuid(),s,new Uri("https://foreign.invalid/login"),DateTimeOffset.UtcNow.AddMinutes(5),true));Check(await c.BeginConnectAsync(StoreKind.Epic) is null,"foreign auth browser URI refused");
  fake.Connect=(s,ct)=>Task.FromResult(new StoreConnectSession(Guid.NewGuid(),s,new Uri("https://www.epicgames.com/id/login"),DateTimeOffset.UtcNow.AddMinutes(5),true));var session=await c.BeginConnectAsync(StoreKind.Epic);Check(session is not null,"official auth session available");c.CancelConnect();Check(!await c.CompleteConnectAsync(session!,"inert-code"),"cancelled auth cannot complete");Check(fake.Completes==0,"no code exchange after cancel");
  var f2=new Service();var c2=new StoreLibraryController(f2);await c2.ReadAsync();c2.Dispose();await c2.ReadAsync();Check(f2.Reads==1&&f2.Controls==0,"view disposal cancels no download job");
  var allAccounts=Enum.GetValues<StoreKind>().Select(s=>new StoreAccount(s,StoreConnectionState.Disconnected,1,null,caps)).ToArray();
  Check(allAccounts.Length==4&&StoreLibraryController.Valid(new(12,DateTimeOffset.UtcNow,allAccounts,[],[])),"Amazon joins existing stores without rejecting the whole snapshot");
  Check(!StoreLibraryController.Valid(new(12,DateTimeOffset.UtcNow,[..allAccounts,allAccounts[0]],[],[])),"Additional provider never permits duplicate or unknown account slots");
  Check(StoreLibraryPresentation.Name(StoreKind.Amazon)=="Amazon Games"&&StoreLibraryPresentation.ServiceLogo(StoreKind.Amazon)=="Amazon","Amazon has its own name and logo");
  Check(StoreLibraryPresentation.ServiceLogo((StoreKind)999)is null,"Unknown store never inherits the Xbox logo");
  foreach(var uri in new[]{"https://amazon.com/ap/signin","https://www.amazon.com/ap/signin?openid.mode=checkid_setup"})
   Check(StoreLibraryController.AuthorizationUriAllowed(StoreKind.Amazon,new Uri(uri)),"Official Amazon login allowed");
  foreach(var uri in new[]{"http://amazon.com/ap/signin","https://amazon.com.evil.invalid/ap/signin","https://evil.amazon.com/ap/signin","https://amazon.com/ap/other","https://amazon.com:444/ap/signin","https://user@amazon.com/ap/signin","https://amazon.com/ap/signin#fragment"})
   Check(!StoreLibraryController.AuthorizationUriAllowed(StoreKind.Amazon,new Uri(uri)),"Untrusted Amazon login refused");
  var cancelService=new Service();var cancelJob=Job();cancelService.Read=_=>Task.FromResult(Snap(1,jobs:[cancelJob]));using var cancelController=new StoreLibraryController(cancelService);await cancelController.ReadAsync();
  Check(!await cancelController.ControlAsync(cancelJob,StoreJobAction.Cancel)&&cancelService.Controls==0,"direct cancel cannot bypass confirmation");
  var decision=new TaskCompletionSource<bool>();var waitingCancel=cancelController.CancelAsync(cancelJob,()=>decision.Task);Check(cancelService.Controls==0,"no cancellation before decision");decision.SetResult(false);Check(!await waitingCancel&&cancelService.Controls==0,"No preserves download");
  cancelService.Read=_=>Task.FromResult(Snap(2,jobs:[cancelJob with{Revision=2}]));cancelService.Control=(request,ct)=>{Check(request.Action==StoreJobAction.Cancel&&request.ExpectedRevision==2&&request.JobId==cancelJob.JobId,"Yes dispatches exact freshly reread job");return Task.FromResult(new StoreCommandResult(true,null,Snap(3,jobs:[cancelJob with{Revision=3,State=StoreJobState.Canceling,CanCancel=false}])));};
  Check(await cancelController.CancelAsync(cancelJob,()=>Task.FromResult(true))&&cancelService.Controls==1,"explicit Yes and service ACK cancel");
  var foreignService=new Service();foreignService.Read=_=>Task.FromResult(Snap(1,jobs:[cancelJob]));using var foreignController=new StoreLibraryController(foreignService);await foreignController.ReadAsync();foreignService.Read=_=>Task.FromResult(Snap(2,jobs:[cancelJob with{Key=new(StoreKind.Gog,"foreign-game")} ]));Check(!await foreignController.CancelAsync(cancelJob,()=>Task.FromResult(true))&&foreignService.Controls==0,"confirmation cannot transfer to replaced job lineage");
  var loginService=new Service();
  loginService.Connect=(store,ct)=>Task.FromResult(new StoreConnectSession(Guid.NewGuid(),store,new Uri("https://www.epicgames.com/id/login"),DateTimeOffset.UtcNow.AddMinutes(5),true));
  loginService.CompleteResult=new(true,null,Snap(2)); loginService.RefreshResult=new(true,null,Snap(3,Game())); loginService.DisconnectResult=new(true,null,new(4,DateTimeOffset.UtcNow,[],[],[]));
  using(var login=new StoreLibraryController(loginService))
  {
   await login.ReadAsync();var auth=await login.BeginConnectAsync(StoreKind.Epic);
   Check(auth is not null&&await login.ConnectAndLoadLibraryAsync(auth,"inert-code"),"sign-in completes and loads actual library");
   Check(loginService.Completes==1&&loginService.Refreshes==1&&login.VisibleGames.Count()==1,"library fetched automatically without extra click");
   await login.DisconnectAsync(StoreKind.Epic);Check(!login.VisibleGames.Any(),"sign out removes uninstalled account games");
  }
  var failedLibraryService=new Service();
  StoreLibrarySnapshot MissingLibrary(long revision,bool withGame=false)=>new(revision,DateTimeOffset.UtcNow,
   [Account(),new StoreAccount(StoreKind.Gog,StoreConnectionState.Connected,1,null,caps)],withGame?[Game()]:[],[]);
  failedLibraryService.Read=_=>Task.FromResult(MissingLibrary(3));
  failedLibraryService.Connect=(store,ct)=>Task.FromResult(new StoreConnectSession(Guid.NewGuid(),store,new Uri("https://www.epicgames.com/id/login"),DateTimeOffset.UtcNow.AddMinutes(5),true));
  failedLibraryService.CompleteResult=new(true,null,MissingLibrary(4));failedLibraryService.RefreshResult=new(false,"library_unavailable",MissingLibrary(5));
  using(var failedLibrary=new StoreLibraryController(failedLibraryService))
  {
   await failedLibrary.ReadAsync();var failedLibraryAuth=await failedLibrary.BeginConnectAsync(StoreKind.Epic);
   await failedLibrary.ConnectAndLoadLibraryAsync(failedLibraryAuth!,"inert-code");
   Check(failedLibrary.ErrorKey is not null&&!failedLibrary.VisibleGames.Any(),"failed initial library load stays a failure, not a successful empty library");
   failedLibraryService.Read=_=>Task.FromResult(MissingLibrary(5));await failedLibrary.ReadAsync();
   Check(failedLibrary.ErrorKey is not null,"ordinary two-second snapshot poll cannot erase a failed library refresh");
   failedLibraryService.RefreshResult=new(true,null,MissingLibrary(6));await failedLibrary.RefreshAsync(StoreKind.Gog);
   Check(failedLibrary.ErrorKey is not null,"successful refresh of another store cannot clear the failed Epic library");
   failedLibraryService.RefreshResult=new(true,null,MissingLibrary(7,true));await failedLibrary.RefreshAsync(StoreKind.Epic);
   Check(failedLibrary.ErrorKey is null&&failedLibrary.VisibleGames.Count()==1,"successful same-store library readback clears its pending error");
   failedLibraryService.RefreshResult=new(false,"library_unavailable",MissingLibrary(8));await failedLibrary.RefreshAsync(StoreKind.Epic);
   failedLibraryService.DisconnectResult=new(true,null,new StoreLibrarySnapshot(9,DateTimeOffset.UtcNow,[Account(),new StoreAccount(StoreKind.Gog,StoreConnectionState.Disconnected,2,null,caps)],[],[]));await failedLibrary.DisconnectAsync(StoreKind.Gog);
   Check(failedLibrary.ErrorKey is not null,"disconnecting another store cannot erase the failed Epic refresh");
   failedLibraryService.DisconnectResult=new(true,null,new StoreLibrarySnapshot(10,DateTimeOffset.UtcNow,[],[],[]));await failedLibrary.DisconnectAsync(StoreKind.Epic);
   Check(failedLibrary.ErrorKey is null,"explicit same-store disconnect clears its obsolete library error");
  }
  var pendingAttempts=new List<int>();var pendingErrors=0;
  var pending=await Playhub.Importing.ImportPostCommitDelivery.RunAsync(new[]{1,2,3}, item=>
  { pendingAttempts.Add(item);if(item==1)throw new InvalidOperationException("Steam bridge unavailable");if(item==3)throw new IOException("artwork unavailable");return Task.CompletedTask;},_=>pendingErrors++);
  Check(pending==2&&pendingErrors==2&&pendingAttempts.SequenceEqual(new[]{1,2,3}),"postcommit artwork failures remain pending and later games still deliver");
  string callback;
  Check(StoreAuthorizationCallback.TryReadRedirect(StoreKind.Gog,new Uri("https://embed.gog.com/on_login_success?origin=client&code=inert-code"),out callback)&&callback=="inert-code","GOG blank success redirect completes sign-in");
  Check(StoreAuthorizationCallback.TryReadRedirect(StoreKind.Amazon,new Uri("https://www.amazon.com/?openid.oa2.authorization_code=inert-code"),out callback),"Amazon return captures authorization before retail page");
  foreach(var host in new[]{"amazon.com","www.amazon.com"})
   Check(StoreAuthorizationCallback.TryReadRedirect(StoreKind.Amazon,new Uri($"https://{host}/ap/maplanding?openid.oa2.authorization_code=inert-code"),out callback)&&callback=="inert-code","Amazon device callback completes sign-in");
  foreach(var address in new[]{"https://www.amazon.com.evil/ap/maplanding?openid.oa2.authorization_code=x","https://amazon.com/ap/signin?openid.oa2.authorization_code=x","https://user@amazon.com/ap/maplanding?openid.oa2.authorization_code=x","https://amazon.com:444/ap/maplanding?openid.oa2.authorization_code=x","https://amazon.com/ap/maplanding?openid.oa2.authorization_code=x#foreign","https://amazon.com/ap/maplanding?openid.oa2.authorization_code=x&openid.oa2.authorization_code=y"})
   Check(!StoreAuthorizationCallback.TryReadRedirect(StoreKind.Amazon,new Uri(address),out callback),"foreign or ambiguous Amazon device callback refused");
  Check(StoreAuthorizationCallback.TryReadEpicResponse(new Uri("https://www.epicgames.com/id/api/redirect"),"{\"authorizationCode\":\"inert-code\"}",out callback),"Epic JSON callback completes without copy and paste");
  foreach(var address in new[]{"https://embed.gog.com.evil.invalid/on_login_success?code=x","https://embed.gog.com/on_login_success?code=x&code=y","http://embed.gog.com/on_login_success?code=x","https://user@embed.gog.com/on_login_success?code=x","https://embed.gog.com/on_login_success?code=%0Ax"})
   Check(!StoreAuthorizationCallback.TryReadRedirect(StoreKind.Gog,new Uri(address),out callback),"foreign or ambiguous callback never accepted");
  Check(!StoreAuthorizationCallback.TryReadEpicResponse(new Uri("https://example.org/id/api/redirect"),"{\"authorizationCode\":\"inert-code\"}",out callback),"Epic code never read from foreign response");
  Check(!StoreAuthorizationCallback.TryReadRedirect(StoreKind.Amazon,new Uri("https://www.amazon.com/?openid.oa2.authorization_code=x&openid.oa2.authorization_code=y"),out callback),"ambiguous Amazon callback code is refused");
  Check(!StoreAuthorizationCallback.TryReadRedirect(StoreKind.Gog,new Uri("https://embed.gog.com/on_login_success?code=x#foreign"),out callback),"GOG callback fragments are not a sign-in receipt");
  Check(!StoreAuthorizationCallback.TryReadEpicResponse(new Uri("https://www.epicgames.com/id/api/redirect"),new string('x',32769),out callback),"Epic authorization response stays bounded");
  Check(!StoreAuthorizationCallback.TryReadEpicResponse(new Uri("https://www.epicgames.com/id/api/redirect"),"{\"authorizationCode\":null}",out callback),"missing Epic authorization code never becomes a connected account");
  StoreLibrarySnapshot Metadata(long revision,long epicRevision=1)=>new(revision,DateTimeOffset.UtcNow,
   [Account(epicRevision),new StoreAccount(StoreKind.Gog,StoreConnectionState.Connected,1,null,caps)],
   [Game(epicRevision),new StoreLibraryGame(new(StoreKind.Gog,"100"),"GOG fixture",1,caps,false,false)],[]);
  var metadataService=new Service();metadataService.Read=_=>Task.FromResult(Metadata(1));
  var epicRefresh=new TaskCompletionSource<StoreCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
  metadataService.Refresh=(store,revision,token)=>store==StoreKind.Epic?epicRefresh.Task:Task.FromResult(new StoreCommandResult(false,"library_unavailable",Metadata(2)));
  using(var metadata=new StoreLibraryController(metadataService))
  {
   await metadata.ReadAsync();var refreshing=metadata.RefreshAsync(StoreKind.Epic);
   Check(ReferenceEquals(refreshing,metadata.RefreshAsync(StoreKind.Epic))&&metadataService.Refreshes==1,"same-store refresh is one shared operation");
   Check(metadata.IsRefreshing(StoreKind.Epic)&&!metadata.Busy&&metadata.CanInstall(metadata.VisibleGames.First()),"metadata refresh keeps other library actions usable");
   Check(!await metadata.RefreshAsync(StoreKind.Gog)&&metadata.ErrorStore==StoreKind.Gog,"refresh failure identifies the exact store");
   epicRefresh.SetResult(new(true,null,Metadata(3)));Check(await refreshing,"other-store metadata completes independently");
   Check(metadata.LibraryError(StoreKind.Epic)is null&&metadata.LibraryError(StoreKind.Gog)=="StoreLibrary.RefreshFailed"&&metadata.ErrorStore==StoreKind.Gog,"successful Epic refresh cannot masquerade as a GOG failure or erase it");
  }
  var changedService=new Service();changedService.Read=_=>Task.FromResult(Metadata(10));
  var changedRefresh=new TaskCompletionSource<StoreCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);changedService.Refresh=(_,_,_)=>changedRefresh.Task;
  using(var changed=new StoreLibraryController(changedService))
  {
   await changed.ReadAsync();var refreshing=changed.RefreshAsync(StoreKind.Epic);
   changedService.Read=_=>Task.FromResult(Metadata(11,2));await changed.ReadAsync();
   changedRefresh.SetResult(new(true,null,Metadata(12)));Check(!await refreshing,"late metadata from a replaced account is rejected");
   Check(changed.Snapshot.Accounts.Single(a=>a.Store==StoreKind.Epic).Revision==2&&changed.VisibleGames.Single(g=>g.Key.Store==StoreKind.Epic).AccountRevision==2,"old refresh cannot resurrect previous-account games");
  }
  Check(StoreLibraryPresentation.StateGlyph(StoreJobState.Completed)!=StoreLibraryPresentation.StateGlyph(StoreJobState.PausedByUser)
   &&StoreLibraryPresentation.StateGlyph(StoreJobState.Downloading)!=StoreLibraryPresentation.StateGlyph(StoreJobState.PausedByUser),"download completion, pause and active states have distinct icons");
  Check(StoreInstallDestination.FromLibraryFolder(@"D:\Games","A:B/Game?")==@"D:\Games\A B Game","browsing a populated library parent selects a new sanitized game subfolder");
  Check(StoreInstallDestination.FolderName("CON.txt")=="_CON.txt"&&StoreInstallDestination.FolderName("..")=="Game","Windows reserved names and dot paths cannot become destination segments");
  Check(StoreInstallDestination.FolderName("長いゲームタイトル")=="長いゲームタイトル","non-Latin game titles retain their readable folder name");
  Check(StoreInstallDestination.ChangeDrive(@"E:\",@"C:\Games\Custom\Exact folder",StoreKind.Epic,"Ignored")==@"E:\Games\Custom\Exact folder","changing drive preserves the explicit relative destination");
  Check(StoreInstallDestination.Suggest(StoreKind.Amazon,"A game",[new(@"C:\","C",100,true),new(@"D:\","D",200,true)],150)==@"D:\Games\Amazon Games\A game","suggestion chooses a ready drive with sufficient space and a new game folder");
  var destinationService=new Service();destinationService.Prepare=(_,_,path,_)=>Task.FromResult(Prep(nonce:Guid.NewGuid(),path:path)with{ErrorCode="destination_not_empty_or_unwritable"});
  using(var destination=new StoreLibraryController(destinationService))
  {
   await destination.ReadAsync();Check(await destination.PrepareInstallAsync(destination.VisibleGames.Single(),@"C:\Games\Existing")is null
    &&destination.ErrorKey=="StoreLibrary.DestinationNotEmpty"&&destinationService.Enqueues==0,"nonempty destination stays refused with a specific user-facing explanation");
  }
  var sizeService=new Service();sizeService.Read=_=>Task.FromResult(Snap(1,Game()with{DownloadBytes=100}));
  sizeService.Prepare=(_,_,path,_)=>Task.FromResult(Prep(nonce:Guid.NewGuid(),path:path));
  using(var sizeChange=new StoreLibraryController(sizeService))
  {
   await sizeChange.ReadAsync();var rendered=sizeChange.VisibleGames.Single();var prepared=await sizeChange.PrepareInstallAsync(rendered,@"C:\Games\Title");
   sizeService.Read=_=>Task.FromResult(Snap(2,Game()with{DownloadBytes=80}));await sizeChange.ReadAsync();
   Check(sizeChange.CanInstall(rendered),"a size-only metadata update does not invalidate a rendered game action");
   Check(await sizeChange.InstallAsync(rendered,prepared!,"en")&&sizeService.Enqueues==1,"same key/account/prepared nonce/path confirms exactly once after metadata changes");
  }
  var monotonicService=new Service();using(var monotonic=new StoreLibraryController(monotonicService))
  {
   await monotonic.ReadAsync();var oldRead=new TaskCompletionSource<StoreLibrarySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
   monotonicService.Read=_=>oldRead.Task;var supersededRead=monotonic.ReadAsync();
   monotonicService.Refresh=(_,_,_)=>Task.FromResult(new StoreCommandResult(true,null,Snap(2)));await monotonic.RefreshAsync(StoreKind.Epic);
   oldRead.SetResult(Snap(1));await supersededRead;
   Check(monotonic.Snapshot.Revision==2&&monotonic.ErrorKey is null,"older valid read after background refresh is ignored without a false error");
  }
  foreach(bool failPreparation in new[]{false,true})
  {
   var concurrentService=new Service();using var concurrent=new StoreLibraryController(concurrentService);await concurrent.ReadAsync();
   var preparingResult=new TaskCompletionSource<StoreInstallPreparation>(TaskCreationOptions.RunContinuationsAsynchronously);
   concurrentService.Prepare=(_,_,_,_)=>preparingResult.Task;
   var preparing=concurrent.PrepareInstallAsync(concurrent.VisibleGames.Single());
   var refreshed=Snap(3)with{Games=[Game(),Game()with{Key=new(StoreKind.Epic,"second-game"),Title="Second owned game"}]};
   concurrentService.Refresh=(_,_,_)=>Task.FromResult(new StoreCommandResult(true,null,refreshed));
   Check(await concurrent.RefreshAsync(StoreKind.Epic)&&concurrent.Busy,"background refresh can complete while destination preparation owns the command slot");
   preparingResult.SetResult(Prep()with{ErrorCode=failPreparation?"destination_not_empty_or_unwritable":null});await preparing;
   Check(concurrent.Snapshot.Revision==3&&concurrent.VisibleGames.Count()==2,"deferred refresh snapshot is published when the command slot is released");
   Check(concurrent.ErrorKey==(failPreparation?"StoreLibrary.DestinationNotEmpty":null),"publishing deferred metadata preserves a real preparation error");
  }
  int boundary=count, translations=0;
  foreach(string lang in new[]{"en","it","es","fr","de","pt","uk","zh","ja","ko","hi","ru"})
  {
   using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Assets","Localization",lang+".json")));
   foreach(var pair in json.RootElement.EnumerateObject().Where(p=>p.Name.StartsWith("StoreLibrary.",StringComparison.Ordinal)))
   {Check(LocalizationService.Translate(lang,pair.Name)==pair.Value.GetString(),"actual locale loader "+lang+" "+pair.Name);translations++;}
  }
  Console.WriteLine($"PASS {boundary} actual StoreLibraryController/service-boundary checks + {translations} translations through actual LocalizationService; no host, browser, GPU, UI or downloads executed");
 }
 sealed class Service:IStoreLibraryService
 {
  public int Reads,Enqueues,Controls,Disconnects,Completes,Refreshes;
  public StoreCommandResult CompleteResult=new(true,null,Snap(11)),RefreshResult=new(true,null,Snap(3)),DisconnectResult=new(true,null,new(2,DateTimeOffset.UtcNow,[],[],[]));
  public Func<CancellationToken,Task<StoreLibrarySnapshot>> Read=_=>Task.FromResult(Snap());
  public Func<StoreKind,CancellationToken,Task<StoreConnectSession>> Connect=(s,ct)=>throw new NotImplementedException();
  public Func<StoreGameKey,long,string?,CancellationToken,Task<StoreInstallPreparation>> Prepare=(k,a,p,ct)=>Task.FromResult(Prep(path:p));
  public Func<StoreEnqueueRequest,CancellationToken,Task<StoreCommandResult>> Enqueue=(r,ct)=>Task.FromResult(new StoreCommandResult(true,null,Snap(4)));
  public Func<StoreJobCommand,CancellationToken,Task<StoreCommandResult>> Control=(r,ct)=>Task.FromResult(new StoreCommandResult(false,"unsupported",Snap(4)));
  public Func<StoreKind,long,CancellationToken,Task<StoreCommandResult>>? Refresh;
  public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken ct=default){Reads++;return Read(ct);}
  public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key,CancellationToken ct=default)=>throw new NotSupportedException();
  public Task<StoreConnectSession> BeginConnectAsync(StoreKind s,CancellationToken ct=default)=>Connect(s,ct);
  public Task<StoreCommandResult> CompleteConnectAsync(Guid id,string code,CancellationToken ct=default){Completes++;return Task.FromResult(CompleteResult);}
  public Task<StoreCommandResult> DisconnectAsync(StoreKind s,long rev,CancellationToken ct=default){Disconnects++;return Task.FromResult(DisconnectResult);}
  public Task<StoreCommandResult> RefreshAsync(StoreKind s,long rev,CancellationToken ct=default){Refreshes++;return Refresh?.Invoke(s,rev,ct)??Task.FromResult(RefreshResult);}
  public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey k,long rev,string? path=null,CancellationToken ct=default)=>Prepare(k,rev,path,ct);
  public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest r,CancellationToken ct=default){Enqueues++;return Enqueue(r,ct);}
  public Task<StoreCommandResult> ControlAsync(StoreJobCommand r,CancellationToken ct=default){Controls++;return Control(r,ct);}
 }
}
