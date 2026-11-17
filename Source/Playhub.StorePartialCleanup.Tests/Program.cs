// SPDX-License-Identifier: MIT. Original inert Playhub checks, Copyright (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;
using Playhub.StoreLibrary;

if (args is ["--inert-worker", var behavior])
{
    if (behavior == "bad-json") Console.WriteLine("{invalid-json");
    else Console.WriteLine(JsonSerializer.Serialize(new { kind="exit",exitCode=-2,stopRequested=true,drained=behavior!="undrained",forced=false }));
    Console.Out.Flush();
    if (behavior != "undrained") await Console.In.ReadLineAsync();
    Console.Error.WriteLine("Original fixture pipe drained");
    return;
}
await Checks.Run();

static class Checks
{
    static int passed;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly StoreCapabilities Caps = new(StoreInstallKind.Direct,true,true,true,true,true);
    static readonly StoreGameKey Key = new(StoreKind.Epic,"original-fixture");
    static readonly byte[] Full = [1,2,3,4,5,6,7,8];
    static StorePartialManifest Manifest => new(new string('A',64),[new("assets/data.bin",Full.Length,"SHA256",Convert.ToHexString(SHA256.HashData(Full)))],[".tmp"]);
    static void Check(bool value,string why){if(!value)throw new Exception(why);passed++;}
    static FieldInfo Field(string name)=>typeof(StoreQueueEngine).GetField(name,Hidden)!;
    static List<StoreDownloadJob> Rows(StoreQueueEngine e)=>(List<StoreDownloadJob>)Field("jobs").GetValue(e)!;
    static StoreDownloadJob Job(Guid id,string path,StoreJobState state=StoreJobState.Failed)=>new(id,Key,"Original inert fixture",state,7,4,path,0,8,null,DateTimeOffset.UtcNow,false,false,true);
    static string NewRoot(string parent)
    {string root=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);return root;}
    static StorePartialReceipt Receipt(StoreDownloadJob job)
    {
        File.WriteAllText(Path.Combine(job.InstallPath,".playhub-install-owner"),job.JobId.ToString("D"));
        var receipt=StoreOwnedPartialFiles.Capture(job,"fixture-account");Check(receipt!=null,"fresh marker-only root capture");
        return receipt! with {Manifest=Manifest,WorkerStarted=true,DrainConfirmed=true};
    }
    static string Payload(string root,byte[] bytes,string suffix="")
    {string file=Path.Combine(root,"assets","data.bin"+suffix);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,bytes);return file;}
    static string Seed(string parent,StoreDownloadJob job,StorePartialReceipt? receipt,bool installed=false)
    {
        string file=Path.Combine(parent,Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(file,JsonSerializer.Serialize(new{Version=1,Revision=20L,
            Accounts=new[]{new StoreAccount(job.Key.Store,StoreConnectionState.Connected,4,"Fixture",Caps)},
            Games=new[]{new StoreLibraryGame(job.Key,"Original fixture",4,Caps,installed,installed,installed?job.InstallPath:null)},Jobs=new[]{job},
            AccountIdentities=new Dictionary<StoreKind,string>{{job.Key.Store,"fixture-account"}},
            JobAccountIdentities=new Dictionary<Guid,string>{{job.JobId,"fixture-account"}},PartialDownloads=receipt==null?Array.Empty<StorePartialReceipt>():new[]{receipt}}));
        return file;
    }
    static StoreQueueEngine Engine(string file,Provider? provider=null)
    {var e=new StoreQueueEngine([provider??new Provider()],new StoreInstallFileSystem(),file);((CancellationTokenSource)Field("shutdown").GetValue(e)!).Cancel();return e;}
    static async Task<StoreLibrarySnapshot> Settled(StoreQueueEngine e)
    {
        for(int i=0;i<300;i++){var s=await e.GetSnapshotAsync();if(Rows(e).All(row=>row.State!=StoreJobState.Canceling))return s;await Task.Delay(10);}
        throw new Exception("Original fixture cleanup never settled");
    }
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"Playhub-CancelCleanup-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            await HistoricalGeneration(root);await AlwaysDismiss(root);await MarkerAndRestart(root);await ActualPartial(root);await RefusalsAndRetry(root);await DrainAndLateCompleted(root);await WindowsRequestOnly(root);await LegacyDismiss(root);await RequeueSamePath(root);await CorruptReceipt(root);await PipeDispatch(root);await CliDrain();await PartialSourceChecks.Run(root,Check);
            Console.WriteLine($"PASS {passed} cancel-cleanup checks; original TEMP files, fake provider and original hidden console children only. No game, account, installed host or network.");
        }
        finally
        {
            string full=Path.GetFullPath(root),parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if(Path.GetDirectoryName(full)!=parent||!Path.GetFileName(full).StartsWith("Playhub-CancelCleanup-Inert-",StringComparison.Ordinal))throw new Exception("TEMP cleanup escaped fixture");
            Directory.Delete(full,true);
        }
    }
    static async Task AlwaysDismiss(string parent)
    {
        foreach (var state in Enum.GetValues<StoreJobState>())
        {
            var job=Job(Guid.NewGuid(),NewRoot(parent),state);string payload=Payload(job.InstallPath,[1,2,3]);string disk=Seed(parent,job,null);
            await using(var e=Engine(disk))
            {
                Rows(e)[0]=job;
                bool held=state is StoreJobState.Downloading or StoreJobState.Starting or StoreJobState.Canceling or StoreJobState.Pausing;
                if(held){Field("active").SetValue(e,job.JobId);Field("drainUnconfirmed").SetValue(e,true);}
                Check((await e.GetSnapshotAsync()).Jobs.Single().CanRemove,"every state offers removal: "+state);
                var reply=await e.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid(),DeletePartial:false));
                Check(reply.Accepted&&reply.Snapshot.Jobs.Count==0&&File.Exists(payload),"every state dismisses immediately without speculative deletion: "+state);
                if(held)Check((Guid?)Field("active").GetValue(e)==job.JobId&&(bool)Field("drainUnconfirmed").GetValue(e)!,"dismissal retains undrained worker lease: "+state);
                Rows(e)[0]=Rows(e)[0] with{State=StoreJobState.Interrupted,ErrorCode="worker_drain_unconfirmed"};
                typeof(StoreQueueEngine).GetMethod("Changed",Hidden)!.Invoke(e,null);
                Check((await e.GetSnapshotAsync()).Jobs.Count==0,"late worker error cannot resurrect dismissed row: "+state);
                Field("active").SetValue(e,null);Field("drainUnconfirmed").SetValue(e,false);
            }
            await using(var restored=Engine(disk))Check((await restored.GetSnapshotAsync()).Jobs.Count==0&&File.Exists(payload),"dismissal survives restart even with incomplete drainage: "+state);
        }
    }
    static async Task MarkerAndRestart(string parent)
    {
        var job=Job(Guid.NewGuid(),NewRoot(parent));var receipt=Receipt(job) with {WorkerStarted=false,Manifest=null};string file=Seed(parent,job,receipt);var request=new StoreCancelCleanupRequest(job.JobId,job.Revision,Guid.NewGuid());
        await using(var e=Engine(file))
        {
            Check((await e.GetSnapshotAsync()).Jobs.Single() is{CanRemove:true,RemovalKind:"cleanup"},"marker-only failed job offers authoritative real cleanup kind");
            var stale=await e.CancelCleanupAsync(request with{ExpectedRevision=job.Revision-1});Check(!stale.Accepted&&stale.ErrorCode=="job_changed","stale JOB revision cannot hide row");
            Check((await e.CancelCleanupAsync(request)).Accepted,"marker-only cleanup accepted");
            Check((await Settled(e)).Jobs.Count==0,"successful cleanup automatically disappears");
            Check((await e.CancelCleanupAsync(request)).Accepted,"same request is idempotent");
            var reuse=await e.CancelCleanupAsync(request with{JobId=Guid.NewGuid()});Check(!reuse.Accepted&&reuse.ErrorCode=="request_id_reused","request cannot target another job");
        }
        await using(var e=Engine(file))
        {Check((await e.GetSnapshotAsync()).Jobs.Count==0,"restart retains tombstone");Check((await e.CancelCleanupAsync(request)).Accepted,"same accepted request remains idempotent across restart");}
        using var disk=JsonDocument.Parse(File.ReadAllText(file));Check(disk.RootElement.GetProperty("Jobs").GetArrayLength()==1,"history removal retains provenance job");
        Check(File.ReadAllText(Path.Combine(job.InstallPath,".playhub-install-owner"))==job.JobId.ToString("D"),"marker provenance preserved");
    }
    static async Task HistoricalGeneration(string parent)
    {
        foreach(var scenario in new[]{(StoreJobState.Canceled,false,false),(StoreJobState.Completed,false,false),(StoreJobState.PausedByUser,false,false),(StoreJobState.Canceled,true,false),(StoreJobState.Canceled,false,true)})
        {
            var(oldState,uncertain,parentOverlap)=scenario;
            string path=NewRoot(parent);var key=new StoreGameKey(StoreKind.Gog,"original");var old=Job(Guid.NewGuid(),path,oldState) with{Key=key};var oldReceipt=Receipt(old) with{DrainConfirmed=!uncertain};
            string prior=Payload(path,[9,8,7]);string backup=path+"-previous";
            if(parentOverlap){backup=path;path=Path.Combine(path,"fresh-child");Directory.CreateDirectory(path);}
            else{Directory.Move(path,backup);Directory.CreateDirectory(path);}
            var job=Job(Guid.NewGuid(),path,StoreJobState.Queued) with{Key=key};var receipt=Receipt(job) with{WorkerStarted=false,Manifest=null};string disk=Seed(parent,job,receipt);
            var data=JsonNode.Parse(File.ReadAllText(disk))!;data["Jobs"]!.AsArray().Add(JsonSerializer.SerializeToNode(old));data["JobAccountIdentities"]![old.JobId.ToString()]=JsonValue.Create("fixture-account");File.WriteAllText(disk,data.ToJsonString());
            if(uncertain){data["PartialDownloads"]!.AsArray().Add(JsonSerializer.SerializeToNode(oldReceipt));File.WriteAllText(disk,data.ToJsonString());}
            var provider=new StartProvider();await using var e=new StoreQueueEngine([provider],new StoreInstallFileSystem(),disk);
            int index=Rows(e).FindIndex(j=>j.JobId==job.JobId);Rows(e)[index]=job;
            var accounts=(Dictionary<StoreKind,StoreAccount>)Field("accounts").GetValue(e)!;accounts[StoreKind.Gog]=accounts[StoreKind.Gog] with{State=StoreConnectionState.Connected};
            Field("steam").SetValue(e,new StoreSteamActivity(Guid.NewGuid(),1,DateTimeOffset.UtcNow,false));
            var gate=(SemaphoreSlim)Field("gate").GetValue(e)!;await gate.WaitAsync();try{await (Task)typeof(StoreQueueEngine).GetMethod("DispatchLocked",Hidden)!.Invoke(e,null)!;}finally{gate.Release();}
            for(int wait=0;wait<200;wait++){var row=(await e.GetSnapshotAsync()).Jobs.First(j=>j.JobId==job.JobId);if(provider.Starts>0&&row.State!=StoreJobState.Starting||row.State==StoreJobState.Failed)break;await Task.Delay(5);}
            var current=(await e.GetSnapshotAsync()).Jobs.First(j=>j.JobId==job.JobId);
            if(oldState==StoreJobState.PausedByUser||uncertain||parentOverlap)Check(provider.Starts==0&&current.CanRemove&&current.RemovalKind=="history","unsafe destination blocks worker and file cleanup but always permits dismissal");
            else Check(provider.Starts==1&&current.CanRemove,"historical terminal reference cannot own a replaced fresh-root generation: "+oldState+" actual="+current.ErrorCode);
            Check(File.ReadAllBytes(Path.Combine(backup,"assets","data.bin")).SequenceEqual(new byte[]{9,8,7}),"moved historical payload untouched by new generation");
        }
    }
    static async Task ActualPartial(string parent)
    {
        foreach(var suffix in new[]{"",".tmp"})
        {
            var job=Job(Guid.NewGuid(),NewRoot(parent));var receipt=Receipt(job);string payload=Payload(job.InstallPath,[1,2,3],suffix);
            await using var e=Engine(Seed(parent,job,receipt));Check((await e.GetSnapshotAsync()).Jobs.Single().CanRemove,"bound drained partial removable");
            Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid()))).Accepted,"actual partial cleanup accepted");
            Check((await Settled(e)).Jobs.Count==0&&!File.Exists(payload),"actual partial deleted only after full preflight");
        }
        var fullJob=Job(Guid.NewGuid(),NewRoot(parent));var fullReceipt=Receipt(fullJob);string fullFile=Payload(fullJob.InstallPath,Full);
        StoreOwnedPartialFiles.Delete(fullReceipt,default);Check(!File.Exists(fullFile),"full-size payload requires and passes vendor hash");
    }
    static async Task RefusalsAndRetry(string parent)
    {
        var job=Job(Guid.NewGuid(),NewRoot(parent));var receipt=Receipt(job);string partial=Payload(job.InstallPath,[1,2]);string extra=Path.Combine(job.InstallPath,"user-save.dat");File.WriteAllText(extra,"keep");string file=Seed(parent,job,receipt);
        Guid firstRequest=Guid.NewGuid();
        await using(var e=Engine(file))
        {
            Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,firstRequest))).Accepted,"foreign-file attempt accepted for preflight");
            var s=await Settled(e);Check(s.Jobs.Count==0&&Rows(e).Single().State==StoreJobState.Interrupted&&Rows(e).Single().ErrorCode=="partial_cleanup_foreign_file","cleanup failure remains internal and never resurrects dismissed row");
            Check(File.Exists(partial)&&File.ReadAllText(extra)=="keep","failed preflight deletes no file including known partial");
            Check((await e.CancelCleanupAsync(new(job.JobId,Rows(e).Single().Revision,Guid.NewGuid()))).Accepted,"dismissal remains idempotent after cleanup failure");
        }
        await using(var e=Engine(file))
        {Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,firstRequest))).Accepted,"older accepted attempt stays idempotent after retry and restart");Check(!(await e.CancelCleanupAsync(new(Guid.NewGuid(),job.Revision,firstRequest))).Accepted,"older request cannot be reused for a different job after restart");}
        var changed=Job(Guid.NewGuid(),NewRoot(parent));var changedReceipt=Receipt(changed);string modified=Payload(changed.InstallPath,[9,9,9,9,9,9,9,9]);
        try{StoreOwnedPartialFiles.Delete(changedReceipt,default);throw new Exception("modified full-size payload accepted");}catch(StoreOperationException e){Check(e.Code=="partial_cleanup_file_modified","modified full-size content refused");}
        Check(File.ReadAllBytes(modified).All(b=>b==9),"modified file preserved");
        File.WriteAllText(Path.Combine(changed.InstallPath,".playhub-install-owner"),Guid.NewGuid().ToString("D"));
        try{StoreOwnedPartialFiles.Delete(changedReceipt,default);throw new Exception("foreign marker accepted");}catch(StoreOperationException e){Check(e.Code=="partial_cleanup_ownership_lost","changed owner rejected");}
        var old=Job(Guid.NewGuid(),NewRoot(parent));File.WriteAllText(Path.Combine(old.InstallPath,"foreign.dat"),"keep");
        await using(var e=Engine(Seed(parent,old,null))){Check((await e.GetSnapshotAsync()).Jobs.Single().CanRemove,"legacy failed row offers metadata dismissal");Check((await e.CancelCleanupAsync(new(old.JobId,old.Revision,Guid.NewGuid()))).Accepted&&File.Exists(Path.Combine(old.InstallPath,"foreign.dat")),"legacy dismissal preserves unproved payload");}
        var installed=Job(Guid.NewGuid(),NewRoot(parent));var installedReceipt=Receipt(installed);string game=Payload(installed.InstallPath,Full);
        await using(var e=Engine(Seed(parent,installed,installedReceipt,true))){Check((await e.GetSnapshotAsync()).Jobs.Single() is {CanRemove:true,RemovalKind:"history"},"installed game offers history dismissal");Check((await e.CancelCleanupAsync(new(installed.JobId,installed.Revision,Guid.NewGuid()))).Accepted&&File.Exists(game)&&(await e.GetSnapshotAsync()).Jobs.Count==0,"installed game survives removal of failed history");}
        var canceled=Job(Guid.NewGuid(),NewRoot(parent));var canceledReceipt=Receipt(canceled);string untouched=Payload(canceled.InstallPath,[1]);using var stop=new CancellationTokenSource();stop.Cancel();
        try{StoreOwnedPartialFiles.Delete(canceledReceipt,stop.Token);throw new Exception("canceled preflight accepted");}catch(OperationCanceledException){Check(File.Exists(untouched),"canceled preflight never deletes");}
        var linked=Job(Guid.NewGuid(),NewRoot(parent));var linkedReceipt=Receipt(linked);string original=Payload(linked.InstallPath,[1]);string link=Path.Combine(parent,Guid.NewGuid().ToString("N")+".link");
        Check(CreateHardLink(link,original,IntPtr.Zero),"original fixture hardlink created on TEMP volume");
        try{StoreOwnedPartialFiles.Delete(linkedReceipt,default);throw new Exception("multiply linked partial accepted");}catch(StoreOperationException e){Check(e.Code=="partial_cleanup_file_identity_invalid","hardlinked file refused before delete");}
        Check(File.Exists(original)&&File.Exists(link),"both original fixture hardlinks preserved");
        var replaced=Job(Guid.NewGuid(),NewRoot(parent));var replacedReceipt=Receipt(replaced);string previous=replaced.InstallPath+"-previous";
        Directory.Move(replaced.InstallPath,previous);Directory.CreateDirectory(replaced.InstallPath);File.WriteAllText(Path.Combine(replaced.InstallPath,".playhub-install-owner"),replaced.JobId.ToString("D"));string replacement=Payload(replaced.InstallPath,[1]);
        try{StoreOwnedPartialFiles.Delete(replacedReceipt,default);throw new Exception("replaced directory accepted");}catch(StoreOperationException e){Check(e.Code=="partial_cleanup_ownership_lost","same-path replacement rejected by retained directory identity");}
        Check(File.Exists(replacement)&&Directory.Exists(previous),"replacement and original folder preserved");
        var gog=Job(Guid.NewGuid(),NewRoot(parent));var gogReceipt=Receipt(gog);string resume=Path.Combine(gog.InstallPath,".gogdl-resume");string gogFile=Payload(gog.InstallPath,[1]);
        gogReceipt=gogReceipt with{Manifest=Manifest with{StateFile=".gogdl-resume"}};File.WriteAllText(resume,new string('A',32)+":assets/data.bin\n");
        try{StoreOwnedPartialFiles.Delete(gogReceipt,default);throw new Exception("malformed resume accepted");}catch(StoreOperationException e){Check(e.Code=="partial_cleanup_state_unverified","malformed GOG state cannot authorize removal");}
        Check(File.Exists(gogFile)&&File.Exists(resume),"malformed state preflight preserves all files");
        File.WriteAllText(resume,new string('A',64)+"::assets/data.bin\n");StoreOwnedPartialFiles.Delete(gogReceipt,default);Check(!File.Exists(gogFile)&&!File.Exists(resume),"real GOG empty-support/hash64 shape accepted only against bound manifest");
    }
    static async Task LegacyDismiss(string parent)
    {
        foreach(var state in new[]{StoreJobState.Failed,StoreJobState.ExternalHandoff})
        {
            var job=Job(Guid.NewGuid(),NewRoot(parent),state);string foreign=Path.Combine(job.InstallPath,"foreign-game-save.dat");File.WriteAllText(foreign,"never-delete");string file=Seed(parent,job,null);
            await using(var e=Engine(file))
            {
                Field("active").SetValue(e,job.JobId);Check((await e.GetSnapshotAsync()).Jobs.Single().CanRemove,"legacy active worker still allows presentation dismissal");Field("active").SetValue(e,null);
                Field("drainUnconfirmed").SetValue(e,true);Check((await e.GetSnapshotAsync()).Jobs.Single().CanRemove,"unknown drain never blocks presentation dismissal");Field("drainUnconfirmed").SetValue(e,false);
                Check((await e.GetSnapshotAsync()).Jobs.Single().RemovalKind=="history","legacy removal explicitly declares history-only semantics");
                Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid()))).Accepted,"inactive failed/external legacy row can be dismissed without filesystem ownership");
                Check((await e.GetSnapshotAsync()).Jobs.Count==0&&File.ReadAllText(foreign)=="never-delete","metadata-only dismissal neither deletes nor claims foreign file");
            }
            await using(var e=Engine(file))Check((await e.GetSnapshotAsync()).Jobs.Count==0&&File.Exists(foreign),"legacy metadata dismissal persists across restart without file deletion");
            using var disk=JsonDocument.Parse(File.ReadAllText(file));Check(disk.RootElement.GetProperty("Jobs")[0].GetProperty("ReasonCode").GetString()=="download_request_dismissed","legacy provenance explicitly records dismissal rather than cleaned payload");
        }
    }
    static async Task RequeueSamePath(string parent)
    {
        var job=Job(Guid.NewGuid(),NewRoot(parent));var receipt=Receipt(job);Payload(job.InstallPath,[1,2,3]);string file=Seed(parent,job,receipt);
        await using var e=Engine(file);Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid()))).Accepted,"requeue fixture cancellation accepted");Check((await Settled(e)).Jobs.Count==0,"requeue fixture cleanup hides old row");
        var accounts=(Dictionary<StoreKind,StoreAccount>)Field("accounts").GetValue(e)!;accounts[StoreKind.Epic]=accounts[StoreKind.Epic] with{State=StoreConnectionState.Connected};
        string foreign=Path.Combine(job.InstallPath,"manual-save.dat");File.WriteAllText(foreign,"keep");var rejected=await e.PrepareInstallAsync(Key,4,job.InstallPath);
        Check(rejected.ErrorCode=="destination_not_empty_or_unwritable"&&File.ReadAllText(foreign)=="keep","cleaned-root reclaim refuses newly added foreign file");File.Delete(foreign);
        var prepared=await e.PrepareInstallAsync(Key,4,job.InstallPath);Check(prepared.ErrorCode==null&&prepared.SelectionNonce.HasValue&&prepared.ConfirmedPath==job.InstallPath,"prepare accepts exact same previously cleaned path, including empty subdirectories");
        var queued=await e.EnqueueAsync(new(Key,4,job.InstallPath,Guid.NewGuid(),prepared.SelectionNonce!.Value,prepared.Revision));
        Check(queued.Accepted&&queued.JobId.HasValue&&queued.JobId!=job.JobId,"same-path reinstall creates a fresh job GUID");
        Check(File.ReadAllText(Path.Combine(job.InstallPath,".playhub-install-owner"))==queued.JobId!.Value.ToString("D"),"same-path reclaim rotates only exact old marker to new job");
        Check(queued.Snapshot.Jobs.Single().JobId==queued.JobId&&queued.Snapshot.Jobs.Single().CanRemove,"new same-path claim is independently removable, old receipt does not conflict");
    }
    static async Task CorruptReceipt(string parent)
    {
        var job=Job(Guid.NewGuid(),NewRoot(parent));var receipt=Receipt(job);
        foreach(string field in new[]{"DirectoryIdentity","Fingerprint","Files","PartialSuffixes"})
        {
            string file=Seed(parent,job,receipt);var data=JsonNode.Parse(File.ReadAllText(file))!;var entry=data["PartialDownloads"]![0]!;
            if(field=="DirectoryIdentity")entry[field]=null;else entry["Manifest"]![field]=null;File.WriteAllText(file,data.ToJsonString());
            bool typed=false;try{await using var unused=Engine(file);}catch(StoreOperationException error){typed=error.Code=="queue_invalid";}
            Check(typed,"corrupt nullable receipt field fails as queue_invalid: "+field);
        }
    }
    static async Task PipeDispatch(string parent)
    {
        var job=Job(Guid.NewGuid(),NewRoot(parent));var receipt=Receipt(job) with{WorkerStarted=false,Manifest=null};
        await using var e=Engine(Seed(parent,job,receipt));string pipe="Playhub-Cleanup-Inert-"+Guid.NewGuid().ToString("N");using var stop=new CancellationTokenSource();
        var serve=new StorePipeServer(e,_=>true,pipeName:pipe).RunAsync(stop.Token);
        try
        {
            using var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(2000);
            using var writer=new StreamWriter(client,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true};using var reader=new StreamReader(client,Encoding.UTF8,leaveOpen:true);
            Guid id=Guid.NewGuid();await writer.WriteLineAsync(JsonSerializer.Serialize(new{protocol=1,id,method="cancel-cleanup",args=new StoreCancelCleanupRequest(job.JobId,job.Revision,Guid.NewGuid())},StoreLibraryService.JsonOptions));
            using var response=JsonDocument.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);var body=response.RootElement;
            Check(body.GetProperty("id").GetGuid()==id&&body.GetProperty("ok").GetBoolean()&&body.GetProperty("data").GetProperty("accepted").GetBoolean(),"actual local pipe dispatches cancel-cleanup with job revision/request id");
            Check((await Settled(e)).Jobs.Count==0,"pipe acceptance followed by authoritative disappearance");
            Check(typeof(IStoreCancelCleanupService).IsAssignableFrom(typeof(StoreLibraryService)),"production client exposes optional typed cancel-cleanup service");
        }
        finally{stop.Cancel();try{await serve;}catch(OperationCanceledException){}}
    }
    static async Task WindowsRequestOnly(string parent)
    {
        foreach(var state in new[]{StoreJobState.Failed,StoreJobState.ExternalHandoff})
        {
            var key=new StoreGameKey(StoreKind.Xbox,"9AAA00000001");var job=Job(Guid.NewGuid(),"Microsoft Store",state) with{Key=key,ReasonCode="windows_store_managed",ErrorCode=state==StoreJobState.Failed?"windows_store_install_failed":"windows_store_install_unconfirmed"};
            string file=Seed(parent,job,null);var data=JsonNode.Parse(File.ReadAllText(file))!;
            data["WindowsManagedInstalls"]=new JsonArray(JsonSerializer.SerializeToNode(new{JobId=job.JobId,Key=key,AccountRevision=4,PackageFamilyName="Publisher.Game_abcdefghijklm",RequestedAt=DateTimeOffset.UtcNow.AddMinutes(-1)}));File.WriteAllText(file,data.ToJsonString());
            await using(var e=Engine(file,new Provider(StoreKind.Xbox)))
            {
                Check((await e.GetSnapshotAsync()).Jobs.Single() is{CanRemove:true,RemovalKind:"windowsRequest"},"failed/unconfirmed exact OS request declares OS request-only removal");
                ((HashSet<Guid>)Field("windowsActiveCli").GetValue(e)!).Add(job.JobId);
                Check((await e.GetSnapshotAsync()).Jobs.Single().CanRemove,"active CLI keeps its internal lease but allows presentation dismissal");
                ((HashSet<Guid>)Field("windowsActiveCli").GetValue(e)!).Remove(job.JobId);
                Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid()))).Accepted,"failed/unconfirmed request removed through exact new API without filesystem operations");
                Check((await e.GetSnapshotAsync()).Jobs.Count==0,"OS request disappears after metadata-only tombstone");
            }
            await using(var e=Engine(file,new Provider(StoreKind.Xbox)))Check((await e.GetSnapshotAsync()).Jobs.Count==0,"OS request dismissal remains hidden across restart");
            using var retained=JsonDocument.Parse(File.ReadAllText(file));Check(retained.RootElement.GetProperty("WindowsManagedInstalls").GetArrayLength()==1,"OS operation receipt remains diagnostic provenance");
        }
    }
    static async Task DrainAndLateCompleted(string parent)
    {
        foreach(bool complete in new[]{false,true})
        {
            var job=Job(Guid.NewGuid(),NewRoot(parent),StoreJobState.Downloading);var receipt=Receipt(job) with{DrainConfirmed=false};string payload=Payload(job.InstallPath,[1,2,3]);var provider=new Provider();
            await using var e=Engine(Seed(parent,job,receipt),provider);Rows(e)[0]=job;var transfer=new Transfer();Field("active").SetValue(e,job.JobId);Field("transfer").SetValue(e,transfer);
            var finish=(Task)typeof(StoreQueueEngine).GetMethod("FinishAsync",Hidden)!.Invoke(e,[job.JobId,transfer])!;
            Check((await e.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid()))).Accepted,"active cancel accepted without optimistic delete");
            Check((await e.GetSnapshotAsync()).Jobs.Count==0&&Rows(e).Single().State==StoreJobState.Canceling&&File.Exists(payload),"row disappears immediately while worker and payload remain until drain");
            for(int wait=0;wait<100&&transfer.Stops==0;wait++)await Task.Delay(5);
            Check(transfer.Stops>0,"actual transfer checkpoint requested");
            transfer.Done.SetResult(new(complete,!complete,complete,Stopped:!complete,DrainConfirmed:true));await finish;
            var settled=await Settled(e);
            if(complete)Check(settled.Jobs.Count==0&&Rows(e).Single().State==StoreJobState.Completed&&File.Exists(payload),"late verified Completed preserves installed payload without resurrecting row");
            else Check(settled.Jobs.Count==0&&!File.Exists(payload)&&transfer.Disposals==1,"actual drain and disposal precede delete and tombstone");
        }
        var unknown=Job(Guid.NewGuid(),NewRoot(parent));var unknownReceipt=Receipt(unknown) with{DrainConfirmed=false};string kept=Payload(unknown.InstallPath,[1]);
        await using(var e=Engine(Seed(parent,unknown,unknownReceipt)))
        {Check((await e.GetSnapshotAsync()).Jobs.Single() is {CanRemove:true,RemovalKind:"history"},"unconfirmed drain allows dismissal without deletion");var r=await e.CancelCleanupAsync(new(unknown.JobId,unknown.Revision,Guid.NewGuid()));Check(r.Accepted&&(await e.GetSnapshotAsync()).Jobs.Count==0&&File.Exists(kept),"unknown drain retains files and worker provenance while hiding row");}
    }
    static async Task CliDrain()
    {
        foreach(string behavior in new[]{"bad-json","normal","undrained"})
        {
            string executable=Path.ChangeExtension(typeof(Checks).Assembly.Location,".exe");
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--inert-worker");start.ArgumentList.Add(behavior);
            var process=Process.Start(start)??throw new Exception("original fixture failed to start");int released=0;
            var t=new StoreCliProvider.CliTransfer(process,StoreKind.Gog,_=>{},()=>Task.FromResult(false),()=>true,()=>Interlocked.Increment(ref released));
            if(behavior=="bad-json")
            {
                var outcome=await t.Completion.WaitAsync(TimeSpan.FromSeconds(5));Check(!outcome.DrainConfirmed,"parser failure never invents drain");
                Check(released==0&&!process.HasExited,"live owned worker retains provider lease");var disposing=t.DisposeAsync().AsTask();Check(!disposing.IsCompleted,"disposal retains redirected streams until exit");
                await process.StandardInput.WriteLineAsync("finish");await disposing.WaitAsync(TimeSpan.FromSeconds(5));Check(released==1,"late actual exit releases lease once");
            }
            else if(behavior=="normal")
            {await Task.Delay(150);Check(!t.Completion.IsCompleted&&released==0,"exit record alone cannot complete while stdout/actual worker are live");await process.StandardInput.WriteLineAsync("finish");var outcome=await t.Completion.WaitAsync(TimeSpan.FromSeconds(5));Check(outcome.DrainConfirmed&&outcome.Checkpointed,"true supervisor drain plus actual exit/readers confirms stop");await t.DisposeAsync();Check(released==1,"normal native lease released once");}
            else
            {var outcome=await t.Completion.WaitAsync(TimeSpan.FromSeconds(5));Check(!outcome.DrainConfirmed&&outcome.ErrorCode=="worker_drain_unconfirmed","explicit supervisor undrained remains unconfirmed");await t.DisposeAsync();Check(released==1,"finished undrained fixture releases resource lease without granting cleanup authority");}
        }
    }
    sealed class Transfer:IStoreTransfer
    {internal readonly TaskCompletionSource<TransferCompletion> Done=new(TaskCreationOptions.RunContinuationsAsynchronously);internal int Stops,Disposals;public Task<TransferCompletion> Completion=>Done.Task;public Task RequestCheckpointAsync(CancellationToken token){Interlocked.Increment(ref Stops);return Task.CompletedTask;}public ValueTask DisposeAsync(){Disposals++;return ValueTask.CompletedTask;}}
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string file,string existing,IntPtr security);
    sealed class StartProvider:Provider,IStorePartialDownloadSource
    {
        public StartProvider():base(StoreKind.Gog){}
        internal int Starts;public bool HasOperationLease=>false;
        public Task<StorePartialManifest> ReadPartialManifestAsync(string id,CancellationToken token)=>Task.FromResult(Manifest);
        public override Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken token)
        {Starts++;var transfer=new Transfer();transfer.Done.SetResult(new(false,true,false,Stopped:true,DrainConfirmed:true));return Task.FromResult<IStoreTransfer>(transfer);}
    }
    class Provider(StoreKind store=StoreKind.Epic):IStoreProvider
    {
        public StoreKind Store=>store;public StoreCapabilities Capabilities=>Caps;public Uri AuthorizationUri=>throw new Exception("No authentication");
        public Task<ProviderAccount?> GetAccountAsync(CancellationToken token)=>throw new Exception("No authentication");public Task<ProviderAccount> ConnectAsync(string code,CancellationToken token)=>throw new Exception("No authentication");public Task DisconnectAsync(CancellationToken token)=>throw new Exception("No authentication");public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token)=>throw new Exception("No library network");public Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken token)=>Task.FromResult(new ProviderInstallSize(8,8));public Task<ProviderLaunch?> ResolveLaunchAsync(string id,string path,CancellationToken token)=>Task.FromResult<ProviderLaunch?>(null);public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id,string path,CancellationToken token)=>throw new Exception("No launch");public virtual Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken token)=>throw new Exception("No retail execution");
    }
}
