using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using PlayhubSetup;
using Playhub.Models;
using Playhub.Services;

var root = Path.Combine(Path.GetTempPath(),"playhub-lc-inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int count=0;void Check(bool value,string msg){if(!value)throw new Exception(msg);count++;}
(string Zip,string Plugins,string Dest) New(string version="2.5.8",string separator="/"){
 string dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
 string zip=Path.Combine(dir,"Launch-Curtain_Installer-2.5.8.zip"),plugins=Path.Combine(dir,"homebrew","plugins"),dest=Path.Combine(plugins,"launch-curtain");Directory.CreateDirectory(plugins);
 using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(var item in new[]{("plugin.json","{\"name\":\"Launch Curtain\"}"),("package.json","{\"version\":\""+version+"\"}"),("dist/index.js","new-poll-fix"),("main.py","original-main")}){using var w=new StreamWriter(z.CreateEntry(("launch-curtain/"+item.Item1).Replace("/",separator)).Open());w.Write(item.Item2);}
 return(zip,plugins,dest);
}
void Populate((string Zip,string Plugins,string Dest) f){Directory.CreateDirectory(f.Plugins);ZipFile.ExtractToDirectory(f.Zip,f.Plugins,true);}
try{

 var windows=New(separator:"\\");Populate(windows);
 Check(BundledLaunchCurtainMaintenance.Inspect(windows.Zip,windows.Plugins).Status=="current","normal Windows ZIP backslashes compare exact current payload");
 File.WriteAllText(Path.Combine(windows.Dest,"dist/index.js"),"old");
 Check(BundledLaunchCurtainMaintenance.Inspect(windows.Zip,windows.Plugins).Status=="update","same-version normal Windows ZIP is admitted for actual frontend maintenance");
 using(var z=ZipFile.Open(windows.Zip,ZipArchiveMode.Update)){z.CreateEntry("launch-curtain\\empty\\");}
 Check(BundledLaunchCurtainMaintenance.Inspect(windows.Zip,windows.Plugins).Status=="update","normal Windows directory entry is safely ignored after validation");
 foreach(string badPath in new[]{"launch-curtain\\..\\escape","launch-curtain/../escape","launch-curtain\\.\\escape","launch-curtain\\C:\\escape","\\launch-curtain\\escape","launch-curtain//empty","launch-curtain\\dist\\index.js.","launch-curtain\\dist\\index.js ","launch-curtain\\..\\outside\\"}){
  var badZip=New(separator:"\\");Populate(badZip);using(var z=ZipFile.Open(badZip.Zip,ZipArchiveMode.Update)){z.CreateEntry(badPath);}
  Check(BundledLaunchCurtainMaintenance.Inspect(badZip.Zip,badZip.Plugins).Status=="payload-invalid","unsafe normalized Windows ZIP name rejected: "+badPath);
 }
 foreach(string collision in new[]{"launch-curtain/dist/index.js","launch-curtain\\DIST\\INDEX.JS"}){
  var duplicate=New(separator:"\\");Populate(duplicate);using(var z=ZipFile.Open(duplicate.Zip,ZipArchiveMode.Update)){z.CreateEntry(collision);}
  Check(BundledLaunchCurtainMaintenance.Inspect(duplicate.Zip,duplicate.Plugins).Status=="payload-invalid","normalized mixed-separator/case duplicate rejected");
 }
 var f=New();int installs=0,evals=0;Task Install(DeckyPluginInfo p,string path,Func<bool> allowed){if(!allowed())throw new OperationCanceledException();installs++;Check(p.IsInstalled&&p.InstalledFolder==f.Dest&&p.InstallerZip==f.Zip,"exact normal service inputs");ZipFile.ExtractToDirectory(f.Zip,f.Plugins,true);return Task.CompletedTask;}
 Task<JsonNode?> Eval(string expr,CancellationToken token){evals++;Check(expr==BundledLaunchCurtainMaintenance.ReloadScript,"only actual normal reload script");return Task.FromResult<JsonNode?>(JsonValue.Create("reloaded"));}
 var r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(r.Status=="not-installed"&&installs==0&&evals==0,"optional missing never installed");
 Populate(f);r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(r.Status=="current"&&installs==0,"current all payload bytes skip");
 File.WriteAllText(Path.Combine(f.Dest,"dist/index.js"),"old-poll-code");string data=Path.Combine(f.Plugins,"..","data","launch-curtain");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"launch-curtain.json"),"user settings");File.WriteAllText(Path.Combine(data,"sound.wav"),"user sound");
 var p=BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins);Check(p.Status=="update"&&p.ChangedFiles==1,"same version different frontend requires maintenance");
 r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(r.Status=="updated-reloaded"&&r.Reloaded&&installs==1&&evals==1,"exact one install plus normal reload");Check(File.ReadAllText(Path.Combine(data,"launch-curtain.json"))=="user settings"&&File.ReadAllText(Path.Combine(data,"sound.wav"))=="user sound","external configuration and media untouched");Check(!File.Exists(Path.Combine(f.Dest,BundledLaunchCurtainMaintenance.ReloadMarker)),"reload receipt removes own pending marker");
 r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(installs==1&&evals==1,"repeated setup does not reinstall");
 File.Delete(Path.Combine(f.Dest,"main.py"));Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).ChangedFiles==1,"all payload files checked beyond frontend");Populate(f);
 foreach(var v in new[]{"2.5.9","2.7.0","unknown","2.5.8-custom"}){File.WriteAllText(Path.Combine(f.Dest,"package.json"),"{\"version\":\""+v+"\"}");r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(r.Status is "newer-preserved" or "installed-version-unknown","newer and unknown versions preserved");Check(installs==1,"preserve path never invokes installer");}Populate(f);
 File.WriteAllText(Path.Combine(f.Dest,".playhub-release.json"),"{\"version\":\"2.9.0\"}");Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).Status=="newer-preserved","newer installed marker respected");File.Delete(Path.Combine(f.Dest,".playhub-release.json"));
 File.WriteAllText(Path.Combine(f.Dest,"release-info.json"),"{\"version\":\"2.5.9\"}");Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).Status=="newer-preserved","newer legacy catalog release-info marker prevents downgrade even with old package version");
 File.WriteAllText(Path.Combine(f.Dest,"release-info.json"),"{\"version\":\"unknown\"}");Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).Status=="installed-version-unknown","unknown legacy catalog marker cannot grant maintenance");File.Delete(Path.Combine(f.Dest,"release-info.json"));
 File.WriteAllText(Path.Combine(f.Dest,"plugin.json"),"{\"name\":\"Foreign\"}");Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).Status=="identity-preserved","foreign identity never touched");Populate(f);
 File.WriteAllText(Path.Combine(f.Dest,"launch-curtain.json"),"legacy");Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).Status=="legacy-data-preserved","legacy config never deleted");File.Delete(Path.Combine(f.Dest,"launch-curtain.json"));Directory.CreateDirectory(Path.Combine(f.Dest,"launch-images"));Check(BundledLaunchCurtainMaintenance.Inspect(f.Zip,f.Plugins).Status=="legacy-data-preserved","legacy images never deleted");Directory.Delete(Path.Combine(f.Dest,"launch-images"));
 File.WriteAllText(Path.Combine(f.Dest,"dist/index.js"),"old");
 r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,(_,_)=>throw new IOException("no Steam"),()=>BundledProcessSafety.Clear);Check(r.Status=="updated-reload-pending"&&!r.Reloaded,"unavailable backend is explicit pending not fake reload");Check(File.Exists(Path.Combine(f.Dest,BundledLaunchCurtainMaintenance.ReloadMarker)),"pending retained");int before=installs;
 r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(r.Reloaded&&installs==before,"current pending retries reload without reinstall");
 File.WriteAllText(Path.Combine(f.Dest,"dist/index.js"),"old");r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,(_,_)=>Task.FromResult<JsonNode?>(JsonValue.Create("busy")),()=>BundledProcessSafety.Clear);Check(r.Status=="updated-reload-pending","busy does not claim reload");
 var bad=New();Populate(bad);using(var z=ZipFile.Open(bad.Zip,ZipArchiveMode.Update)){using var w=new StreamWriter(z.CreateEntry("launch-curtain/../escape").Open());w.Write("x");}Check(BundledLaunchCurtainMaintenance.Inspect(bad.Zip,bad.Plugins).Status=="payload-invalid","traversal rejected before installer");
 // Exercise the REAL modified DeckyPluginService only against an absent TEMP
 // destination: no StopPluginProcesses path, network, shell or OS process calls.
 string fresh=Path.Combine(root,"fresh");AppPaths.TestDownloads=Path.Combine(root,"downloads");
 var info=new DeckyPluginInfo{FolderName="launch-curtain",InstallerZip=f.Zip,RepositorySlug="bad/must-not-contact",ReleaseZipUrl="http://invalid.invalid/must-not-fetch.zip",CatalogReleaseZipUrl="http://invalid.invalid/must-not-fetch.zip",Version="2.5.8"};
 await new DeckyPluginService().InstallOrUpdateAsync(info,fresh,bundledOnly:true);Check(File.ReadAllText(Path.Combine(fresh,"launch-curtain/dist/index.js"))=="new-poll-fix","actual bundled-only service ignores all remote URL fields");Check(info.InstalledVersion=="2.5.8","actual service marker reports bundled version");
 string winFresh=Path.Combine(root,"fresh-windows-zip");
 var winInfo=new DeckyPluginInfo{FolderName="launch-curtain",InstallerZip=windows.Zip,Version="2.5.8"};
 await new DeckyPluginService().InstallOrUpdateAsync(winInfo,winFresh,bundledOnly:true);
 Check(File.ReadAllText(Path.Combine(winFresh,"launch-curtain/dist/index.js"))=="new-poll-fix","actual normal service extracts Windows backslash ZIP to correct plugin folder");
 Check(winInfo.InstalledVersion=="2.5.8","actual Windows ZIP install uses packaged version without downgrade or remote lookup");
 // Process veto is independent of a falsely idle SteamUIStore.
 var expected=new[]{@"C:\Playhub\Playhub.GameSession.exe",@"C:\Playhub\Playhub.XboxSession.exe"};
 const string sid="S-1-5-21-inert-current";
 BundledProcessSafety Classify(params BundledProcessFact[] facts)=>BundledPluginProcessGuard.Classify(true,3,sid,expected,facts);
 Check(Classify()==BundledProcessSafety.Clear,"known empty process enumeration permits maintenance");
 foreach(string image in expected){Check(Classify(new BundledProcessFact(true,true,42,134389459887816137,3,sid,image))==BundledProcessSafety.Active,"exact wrapper or source process vetoes independently of Steam idle");}
 Check(Classify(new BundledProcessFact(false,false))==BundledProcessSafety.Unknown,"uncertain process query vetoes");
 Check(BundledPluginProcessGuard.Classify(false,3,sid,expected,[])==BundledProcessSafety.Unknown,"enumeration failure vetoes");
 Check(Classify(new BundledProcessFact(true,true,42,134389459887816137,4,sid,expected[0]))==BundledProcessSafety.Clear,"positively different session is not our active session");
 Check(Classify(new BundledProcessFact(true,true,42,134389459887816137,3,"S-1-5-21-inert-other",expected[0]))==BundledProcessSafety.Clear,"positively different user is not our process");
 Check(Classify(new BundledProcessFact(true,true,42,134389459887816137,3,sid,@"C:\Other\Playhub.GameSession.exe"))==BundledProcessSafety.Clear,"same name from different exact installation is not our process");
 Check(Classify(new BundledProcessFact(true,true,42,0,3,sid,expected[0]))==BundledProcessSafety.Unknown,"unknown process birth never grants absence");
 Check(Classify(new BundledProcessFact(true,false))==BundledProcessSafety.Clear,"positive exited process does not block");
 Check(Classify(new BundledProcessFact(true,true,42,134389459887816137,3,"",expected[0]))==BundledProcessSafety.Unknown,"unavailable owner SID never grants absence");
 Check(BundledPluginProcessGuard.Classify(true,3,sid,["relative",expected[1]],[])==BundledProcessSafety.Unknown,"unknown installed image paths veto");
 Populate(f);File.Delete(Path.Combine(f.Dest,BundledLaunchCurtainMaintenance.ReloadMarker));
 File.WriteAllText(Path.Combine(f.Dest,"dist/index.js"),"old");
 foreach(var safety in new[]{BundledProcessSafety.Active,BundledProcessSafety.Unknown}){
  int i=installs,e=evals;
  r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>safety);
  Check(r.Status.StartsWith("deferred-")&&installs==i&&evals==e,"active/uncertain veto BEFORE install and reload");
  Check(File.ReadAllText(Path.Combine(f.Dest,"dist/index.js"))=="old"&&!File.Exists(Path.Combine(f.Dest,BundledLaunchCurtainMaintenance.ReloadMarker)),"veto never stops/deletes/copies/marks installed plugin");
 }
 {int i=installs,e=evals;r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>throw new IOException("enumeration uncertain"));Check(r.Status=="deferred-uncertain-session"&&installs==i&&evals==e,"process getter exception is conservative defer");}
 {int i=installs,e=evals,reads=0;
  r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>++reads==1?BundledProcessSafety.Clear:BundledProcessSafety.Active);
  Check(r.Status=="deferred-active-session"&&installs==i&&evals==e&&reads==2,"new game after planning is vetoed by normal install's final guard");
 }
 {int i=installs,e=evals,reads=0;
  r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>++reads<=2?BundledProcessSafety.Clear:BundledProcessSafety.Active);
  Check(r.Status=="updated-reload-deferred-active-session"&&installs==i+1&&evals==e&&reads==3,"new game after install blocks reload even if Steam would report idle");
  Check(File.Exists(Path.Combine(f.Dest,BundledLaunchCurtainMaintenance.ReloadMarker)),"late active session retains matching pending receipt");
 }
 foreach(var safety in new[]{BundledProcessSafety.Active,BundledProcessSafety.Unknown}){
  int i=installs,e=evals;r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>safety);
  Check(r.Status.StartsWith("updated-reload-deferred-")&&installs==i&&evals==e,"current-byte pending reload route also honors active/uncertain veto");
 }
 {int i=installs;r=await BundledLaunchCurtainMaintenance.RunAsync(f.Zip,f.Plugins,Install,Eval,()=>BundledProcessSafety.Clear);Check(r.Reloaded&&installs==i,"later known-empty pending route reloads once without reinstall");}
 // Actual normal service guard runs after extract/Installing progress. Destination
 // is absent, so even a broken assertion cannot enter StopPluginProcesses.
 string denied=Path.Combine(root,"denied-install");int finalGuards=0;
 var deniedInfo=new DeckyPluginInfo{FolderName="launch-curtain",InstallerZip=f.Zip,Version="2.5.8"};
 bool canceled=false;try{await new DeckyPluginService().InstallOrUpdateAsync(deniedInfo,denied,bundledOnly:true,canInstall:()=>{finalGuards++;return false;});}catch(OperationCanceledException){canceled=true;}
 Check(canceled&&finalGuards==1&&!Directory.Exists(Path.Combine(denied,"launch-curtain"))&&!deniedInfo.IsInstalled,"actual service final veto precedes all Stop/Delete/Copy/install metadata effects");
 Console.WriteLine($"{count} actual maintenance/service/process-veto checks PASS; TEMP data and fake reload only");
}finally{Directory.Delete(root,true);}
namespace Playhub.Services{internal static class AppPaths{internal static string TestDownloads="";public static string DownloadsRoot=>TestDownloads;public static string DefaultDeckyPluginsPath=>throw new Exception("production wrapper forbidden in test");public static string SettingsFile=>throw new Exception("production wrapper forbidden in test");public static string LocalDataRoot=>throw new Exception("production wrapper forbidden in test");}}
namespace Playhub.Integrations{internal static class SteamPluginBridge{internal static Task<JsonNode?> EvaluateAsync(string e,CancellationToken c)=>throw new Exception("live evaluator forbidden");}}
