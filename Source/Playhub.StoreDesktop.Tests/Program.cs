using Playhub.StoreLibrary;
using Playhub.Services;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

int checks=0;
void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
StoreSteamCollectionMigration Make(string state="pending",int confirmed=0,int total=0,Guid? id=null,string? account="76561198000000001")=>new(id??Guid.NewGuid(),"2.7.0:store-collections-v2",account,state,confirmed,total);
var deferred=new TaskCompletionSource<StoreSteamCollectionMigration>(TaskCreationOptions.RunContinuationsAsynchronously);int requests=0,reads=0;
using(var controller=new StoreMigrationController(_=>{requests++;return deferred.Task;},(_,_)=>{reads++;return Task.FromResult(Make());}))
{
 var first=controller.StartAsync();var second=controller.StartAsync();var third=controller.RefreshAsync();
 Check(requests==1&&ReferenceEquals(first,second)&&ReferenceEquals(first,third)&&controller.Busy,"Single-flight duplicate requests");
 var pending=Make();deferred.SetResult(pending);await first;
 Check(controller.Snapshot==pending&&controller.NeedsPolling&&!controller.ReadFailed&&!controller.InitialCompleted&&controller.WasPending,"Pending incorrectly completed");
}
using(var complete=new StoreMigrationController(_=>Task.FromResult(Make("completed",2,2)),(_,_)=>throw new Exception()))
{
 await complete.StartAsync();Check(complete.InitialCompleted&&!complete.WasPending&&!complete.NeedsPolling,"Completed receipt reprompted");
}
var current=Make("waitingSteam",account:null);reads=0;
using(var controller=new StoreMigrationController(_=>Task.FromResult(current),(_,_)=>{reads++;return Task.FromResult(current);}))
{
 await controller.StartAsync();Check(controller.Snapshot?.State=="waitingSteam"&&controller.NeedsPolling,"Waiting Steam claimed success");
 current=current with{State="pending",SteamAccountId="76561198000000001",Total=3,Confirmed=1};await controller.RefreshAsync();
 Check(reads==1&&controller.Snapshot==current&&!controller.ReadFailed,"Waiting Steam did not bind current account");
 current=current with{Confirmed=2};await controller.RefreshAsync();Check(controller.Snapshot?.Confirmed==2,"Partial progress not observed");
 current=current with{State="completed",Confirmed=3};await controller.RefreshAsync();
 Check(controller.Snapshot?.State=="completed"&&!controller.InitialCompleted&&controller.WasPending&&!controller.NeedsPolling,"Completion not server-authoritative");
}
foreach(string invalid in new[]{"count","id","version","account","state"})
{
 var original=Make("pending",1,3);var wrong=invalid switch{
 "count"=>original with{State="completed",Confirmed=1},"id"=>original with{RequestId=Guid.NewGuid()},
 "version"=>original with{Version="different"},"account"=>original with{SteamAccountId="76561198000000002"},_=>original with{State="success"}};
 using var controller=new StoreMigrationController(_=>Task.FromResult(original),(_,_)=>Task.FromResult(wrong));
 await controller.StartAsync();await controller.RefreshAsync();Check(controller.ReadFailed&&controller.Snapshot==original&&!controller.NeedsPolling,"Invalid readback accepted: "+invalid);
}
int retryReads=0;var retrySnapshot=Make();
using(var controller=new StoreMigrationController(_=>Task.FromResult(retrySnapshot),(_,_)=>++retryReads==1?Task.FromException<StoreSteamCollectionMigration>(new IOException()):Task.FromResult(retrySnapshot with{Total=4,Confirmed=2})))
{
 await controller.StartAsync();await controller.RefreshAsync();Check(controller.ReadFailed&&controller.Snapshot==retrySnapshot,"Read failure destroyed prior progress");
 await controller.RefreshAsync();Check(!controller.ReadFailed&&controller.Snapshot?.Confirmed==2,"Read retry not recoverable");
}
var failed=Make("failed");requests=0;
using(var controller=new StoreMigrationController(_=>Task.FromResult(++requests==1?failed:Make()),(_,_)=>throw new Exception()))
{
 await controller.StartAsync();await controller.RefreshAsync();Check(requests==2&&controller.Snapshot?.RequestId!=failed.RequestId,"Failed request not retried fresh");
}
var delayed=new TaskCompletionSource<StoreSteamCollectionMigration>(TaskCreationOptions.RunContinuationsAsynchronously);int changes=0;
var disposed=new StoreMigrationController(_=>delayed.Task,(_,_)=>throw new Exception());disposed.Changed+=(_,_)=>changes++;
var late=disposed.StartAsync();int before=changes;disposed.Dispose();delayed.SetResult(Make("completed"));await late;
Check(changes==before&&disposed.Snapshot is null,"Late response after disposal changed UI");
Check(StoreMigrationController.Valid(Make("completed")),"Zero-game native completion rejected");
Check(!StoreMigrationController.Valid(Make("pending",-1,3))&&!StoreMigrationController.Valid(Make("completed",3,2)),"Invalid progress accepted");
string folder=Path.Combine(Path.GetTempPath(),"Playhub-Desktop-Migration-Tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
string file=Path.Combine(folder,"announced.json");var announcements=new StoreMigrationAnnouncements(file);Guid announce=Guid.NewGuid();
announcements.Remember(announce);announcements.Remember(announce);
Check(new StoreMigrationAnnouncements(file).Contains(announce)&&JsonSerializer.Deserialize<Guid[]>(File.ReadAllText(file))!.Length==1,"Announcement not durable/idempotent");
for(int i=0;i<80;i++)announcements.Remember(Guid.NewGuid());
Check(JsonSerializer.Deserialize<Guid[]>(File.ReadAllText(file))!.Length==64,"Announcement history unbounded");
File.WriteAllText(file,"malformed");Check(!new StoreMigrationAnnouncements(file).Contains(announce),"Malformed announcement file trusted");
string[] keys=StoreDesktopCopy.All["en"].Keys.Order().ToArray();
Check(StoreDesktopCopy.All.Count==12,"Missing desktop language");
foreach(var language in LocalizationService.Languages)
{
 var words=StoreDesktopCopy.All[language.Key];Check(words.Keys.Order().SequenceEqual(keys),"Missing keys "+language.Key);
 foreach(var entry in words)
 {
  Check(!string.IsNullOrWhiteSpace(entry.Value)&&!Regex.IsMatch(entry.Value,@"\\u[0-9a-fA-F]{4}"),"Empty/escaped copy "+language.Key+" "+entry.Key);
  Check(Regex.Matches(entry.Value,@"\{[01]\}").Select(m=>m.Value).Order().SequenceEqual(Regex.Matches(StoreDesktopCopy.All["en"][entry.Key],@"\{[01]\}").Select(m=>m.Value).Order()),"Format parity "+language.Key+" "+entry.Key);
 }
 Check(StoreDesktopCopy.Get(language.Key,"migrationTitle")==words["migrationTitle"],"Saved app locale ignored "+language.Key);
 if(language.Key!="en")Check(words["migrationTitle"]!=StoreDesktopCopy.All["en"]["migrationTitle"]&&words["accountsHint"]!=StoreDesktopCopy.All["en"]["accountsHint"],"English placeholder "+language.Key);
 _=string.Format(CultureInfo.InvariantCulture,words["migrationProgress"],1,2);
}
Console.WriteLine("PASS "+checks+" desktop migration/language/receipt checks; no Steam, accounts or installers invoked");
