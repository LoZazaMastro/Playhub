// SPDX-License-Identifier: MIT. Original inert Playhub Amazon integration tests.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.StoreLibrary;
class Checks
{
    static int count;
    internal static void Check(bool value,string name){count++;if(!value)throw new Exception("FAIL "+name);}
    internal static async Task Refuses(Func<Task> call,string name){try{await call();}catch(StoreOperationException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
    static async Task Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"Playhub-Amazon-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var referenceBundle=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Playhub.StoreHost/Runtime"));
        var bundle=Path.Combine(root,"bundle");Directory.CreateDirectory(bundle);File.Copy(Path.Combine(referenceBundle,"nile_windows_x86_64.exe"),Path.Combine(bundle,"nile_windows_x86_64.exe"));
        await AmazonLaunchChecks.Run(referenceBundle);
        await AmazonInstalledChecks.Run(referenceBundle);
        using var key=RSA.Create(2048);var plain=ManifestBytes("game.exe",Encoding.UTF8.GetBytes("synthetic executable"));var signed=Sign(plain,key);
        var xz=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","manifest.xz"));
        var compressed=Sign(plain,key,xz,1);Check(AmazonManifest.Parse(compressed,key).Files.Count==1,"actual Python XZ compressed signed manifest");
        await Refuses(()=>Task.Run(()=>AmazonManifest.Parse(Sign(plain,key,xz,2),key)),"unknown manifest compression");
        await Refuses(()=>Task.Run(()=>AmazonManifest.Parse(Sign(plain,key,xz[..^4],1),key)),"truncated XZ rejected before allocation");
        var oversized=xz.ToArray();oversized[16]=40;await Refuses(()=>Task.Run(()=>AmazonManifest.Parse(Sign(plain,key,oversized,1),key)),"oversized dictionary rejected");
        var manifest=AmazonManifest.Parse(signed,key);Check(manifest.Files.Count==1,"signed manifest parsed");
        await AuthenticationResponses(bundle,root,signed,key);
        await AuthenticationLineage(bundle,root);
        var dir=Path.Combine(root,"verify");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"game.exe"),"synthetic executable");Check(manifest.Verify(dir,default),"signed file hash verifies");
        File.WriteAllText(Path.Combine(dir,"game.exe"),"corrupt");Check(!manifest.Verify(dir,default),"different file rejected");
        var bad=signed.ToArray();bad[^1]^=1;await Refuses(()=>Task.Run(()=>AmazonManifest.Parse(bad,key)),"signature tampering");
        await Refuses(()=>Task.Run(()=>AmazonManifest.Parse(signed)),"test signer never trusted by production");
        foreach(var path in new[]{"../escape.exe","/root.exe","C:/escape.exe","NUL.txt","sub/CON","good/../evil","file:stream","file.","file ","a//b","a\n.exe",".playhub-install-owner"})
            await Refuses(()=>Task.Run(()=>AmazonManifest.Parse(Sign(ManifestBytes(path,[1]),key),key)),"manifest path "+path.Replace('\n','?'));
        Check(StoreAmazonProvider.AuthorizationCode("https://www.amazon.com/?openid.oa2.authorization_code=valid-code")=="valid-code","exact callback parsed");
        Check(StoreAmazonProvider.AuthorizationCode("https://www.amazon.com/ap/maplanding?openid.oa2.authorization_code=valid%2Bcode")=="valid+code","Amazon device maplanding callback preserves encoded plus");
        foreach(var code in new[]{"https://www.amazon.com.evil/?openid.oa2.authorization_code=x","https://amazon.com/ap/signin?openid.oa2.authorization_code=x","https://amazon.com/ap/maplanding/extra?openid.oa2.authorization_code=x","http://www.amazon.com/ap/maplanding?openid.oa2.authorization_code=x","https://amazon.com/?openid.oa2.authorization_code=x&openid.oa2.authorization_code=y","https://amazon.com/?other=x","https://user@amazon.com/?openid.oa2.authorization_code=x","x\r\ny"})
            await Refuses(()=>Task.Run(()=>StoreAmazonProvider.AuthorizationCode(code)),"foreign/malformed auth redirect");
        var fake=new FakeNetwork(signed);var workers=new List<FakeWorker>();var queries=new List<string[]>();string? native=null;
        Task<(int,string)> Query(string exe,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> env,CancellationToken token)
        {
            Check(exe==Path.Combine(bundle,"nile_windows_x86_64.exe"),"pinned runtime exact path");Check(env.ContainsKey("NILE_CONFIG_PATH"),"isolated native state");
            native=Path.Combine(env["NILE_CONFIG_PATH"],"nile");Check(File.Exists(Path.Combine(native,"current_user.json")),"native auth materialized only while owned");
            Check(!args.Any(a=>a.Contains("secret-code")||a.Contains("access-secret")||a.Contains("refresh-secret")),"no secrets in argv");queries.Add(args.ToArray());
            if(args.SequenceEqual(new[]{"library","sync","--force"}))return Task.FromResult((0,""));
            if(args.SequenceEqual(new[]{"library","list","--json"}))return Task.FromResult((0,fake.Library));
            throw new Exception("unexpected query");
        }
        Task<IAmazonWorker> Start(string host,string exe,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> env,string cwd,Action<TransferProgress> progress,CancellationToken token)
        {
            Check(args.Count==4&&args[0]=="install"&&args[1]=="amazon-test"&&args[2]=="--path"&&Path.IsPathFullyQualified(args[3]),"exact --path no title appending");
            Check(!args.Contains("--base-path"),"no base-path ambiguity");
            var worker=new FakeWorker(args[3],native!,signed,progress,File.Exists(Path.Combine(native!,"manifests","amazon-test.raw")));workers.Add(worker);return Task.FromResult<IAmazonWorker>(worker);
        }
        var provider=new StoreAmazonProvider(bundle,Path.Combine(root,"state"),Path.Combine(root,"host.exe"),new HttpClient(fake),Query,Start,bytes=>AmazonManifest.Parse(bytes,key));
        Check(provider.Capabilities.InstallKind==StoreInstallKind.Direct&&provider.Capabilities.CanConnect&&provider.Capabilities.CanRefresh,"actual pinned runtime enables direct capability");
        Check(!provider.Capabilities.CanPause&&!provider.Capabilities.CanResume&&provider.Capabilities.CanCancel,"pause and resume explicitly unsupported");
        Check(await provider.GetAccountAsync(default)==null,"initial disconnected");
        var uri=provider.AuthorizationUri;Check(uri.Host=="amazon.com"&&uri.AbsolutePath=="/ap/signin"&&uri.Query.Contains("S256")&&!uri.Query.Contains("code_verifier"),"public PKCE auth URL only challenge");
        var account=await provider.ConnectAsync("secret-code",default);Check(account.AccountIdentity.Length==64&&account.DisplayName=="Test name","auth account identity hash no credentials");
        Check(fake.Registers==1&&fake.SecretSeen,"registration HTTP keeps code/verifier in body");Check(await provider.GetAccountAsync(default)==account,"DPAPI account roundtrip");
        Check(!Directory.EnumerateFiles(Path.Combine(root,"state"),"*",SearchOption.AllDirectories).Any(f=>File.ReadAllBytes(f).AsSpan().IndexOf(Encoding.UTF8.GetBytes("refresh-secret"))>=0),"refresh token absent from plaintext at rest");
        var library=await provider.ListAsync(default);Check(library.Count==1&&library[0].Id=="amazon-test"&&!library[0].Launchable,"claimed library stable IDs not launch fiction");
        Check(library[0].ArtworkUrl=="https://m.media-amazon.com/images/I/Fixture._SX480_.jpg","owned Amazon illustrated cover reaches the real provider library row instead of wordmark");
        Check(queries.Count==2&&queries[0].SequenceEqual(new[]{"library","sync","--force"}),"fresh sync precedes cached JSON read");
        Check(!File.Exists(Path.Combine(native!,"current_user.json"))&&!Directory.EnumerateFiles(native!,"*.enc").Any(),"materialized credentials sealed and removed");
        var size=await provider.GetInstallSizeAsync("amazon-test",default);Check(size.RequiredBytes==20&&size.DownloadBytes==20,"size from signed manifest all file bytes");
        await Refuses(()=>provider.GetInstallSizeAsync("foreign-game",default),"foreign game missing owned entitlement");
        var job=Guid.NewGuid();var destination=Path.Combine(root,"chosen folder","title-less-final");Directory.CreateDirectory(destination);File.WriteAllText(Path.Combine(destination,".playhub-install-owner"),job.ToString("D"));
        fake.BeforeManifest=()=>File.WriteAllText(Path.Combine(destination,".playhub-install-owner"),Guid.NewGuid().ToString("D"));
        await Refuses(()=>provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),destination,null),_=>{},default),"owner changed during manifest HTTP must refuse before worker");
        Check(workers.Count==0,"owner changed during manifest HTTP starts ZERO workers");
        fake.BeforeManifest=null;File.WriteAllText(Path.Combine(destination,".playhub-install-owner"),job.ToString("D"));
        var runtimeFile=Path.Combine(bundle,"nile_windows_x86_64.exe");var originalRuntime=File.ReadAllBytes(runtimeFile);
        fake.BeforeManifest=()=>{var modified=originalRuntime.ToArray();modified[32]^=1;File.WriteAllBytes(runtimeFile,modified);};
        try { await Refuses(()=>provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),destination,null),_=>{},default),"runtime changed during manifest HTTP refused before process creation");Check(workers.Count==0,"runtime changed during manifest HTTP starts ZERO workers"); }
        finally { fake.BeforeManifest=null;File.WriteAllBytes(runtimeFile,originalRuntime); }
        var transfer=await provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),destination,null),_=>{},default);
        var markerLocked=false;try{File.WriteAllText(Path.Combine(destination,".playhub-install-owner"),Guid.NewGuid().ToString("D"));}catch(IOException){markerLocked=true;}Check(markerLocked,"actual retained marker handle blocks ownership replacement while worker alive");
        var runtimeLocked=false;try{File.WriteAllBytes(runtimeFile,[1]);}catch(IOException){runtimeLocked=true;}Check(runtimeLocked,"actual retained runtime handle blocks replacement while worker alive");
        workers[^1].Complete(0,files:false);var failed=await transfer.Completion;Check(!failed.Completed&&!failed.InstallVerified,"exit zero without actual files never complete");await transfer.DisposeAsync();
        var cachedManifest=Path.Combine(native!,"manifests","amazon-test.raw");Directory.CreateDirectory(Path.GetDirectoryName(cachedManifest)!);
        File.WriteAllBytes(cachedManifest,signed);
        var unrelatedManifest=Path.Combine(native!,"manifests","another-owned-game.raw");File.WriteAllBytes(unrelatedManifest,[1,2,3]);
        transfer=await provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),destination,null),_=>{},default);
        Check(!workers[^1].SkipPayloadLikeNile,"uninstalled cached manifest must not skip a fresh destination in the actual Nile cache model");
        workers[^1].Complete(0,files:true);var done=await transfer.Completion;Check(done.Completed&&done.InstallVerified&&!done.Checkpointed,"installed metadata plus same signed manifest and all hashes");await transfer.DisposeAsync();
        Check(File.ReadAllBytes(unrelatedManifest).SequenceEqual(new byte[]{1,2,3}),"fresh install preserves another game's cached manifest");
        File.WriteAllText(Path.Combine(destination,"game.exe"),"incomplete");
        transfer=await provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),destination,null),_=>{},default);
        workers[^1].Complete(0,files:true);var repaired=await transfer.Completion;
        Check(repaired.Completed&&repaired.InstallVerified,"same-path incomplete CLI receipt redownloads payload instead of accepting cached up-to-date result");await transfer.DisposeAsync();
        Check(await provider.IsInstalledAsync("amazon-test",destination,default),"installed independent of launcher capability");Check(await provider.ResolveLaunchAsync("amazon-test",destination,default)==null,"installation without signed fuel remains nonlaunchable");
        await Refuses(()=>provider.PrepareLaunchAsync("amazon-test",destination,default),"no invented Amazon launch arguments without signed fuel");
        await Refuses(()=>provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),Path.Combine(root,"foreign destination"),null),_=>{},default),"missing exact destination owner");
        var changed=Path.Combine(root,"relocation");Directory.CreateDirectory(changed);File.WriteAllText(Path.Combine(changed,".playhub-install-owner"),job.ToString("D"));
        await Refuses(()=>provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),changed,null),_=>{},default),"installed path cannot silently override chosen path");
        transfer=await provider.StartAsync(new(job,new(StoreKind.Amazon,"amazon-test"),destination,null),_=>{},default);var current=workers[^1];
        Check(current.SkipPayloadLikeNile&&File.Exists(cachedManifest),"verified exact existing payload retains delta manifest for normal updates");
        await transfer.RequestCheckpointAsync(default);Check(current.Stops==1&&!transfer.Completion.IsCompleted,"cancel request not cancellation acknowledgement");
        current.Complete(1,false,stop:true);var canceled=await transfer.Completion;Check(canceled.Stopped&&!canceled.Checkpointed&&!canceled.Completed,"actual drain stop never becomes pause/checkpoint");await transfer.DisposeAsync();
        await Queue(provider,root,workers,fake);
        await provider.DisconnectAsync(default);Check(await provider.GetAccountAsync(default)==null,"local signout clears credentials");Check(File.Exists(Path.Combine(destination,"game.exe")),"signout preserves installed user files");
        await Refuses(()=>provider.ListAsync(default),"signed out library cannot use cached ownership");
        fake.Library="[{\"id\":\"entitlement-id\",\"product\":{\"id\":\"amazon-test\",\"title\":\"Synthetic owned title\"}}]";
        var authProvider=new StoreAmazonProvider(bundle,Path.Combine(root,"auth-state"),Path.Combine(root,"host.exe"),new HttpClient(fake),Query,Start,bytes=>AmazonManifest.Parse(bytes,key));
        await using(var authQueue=new StoreQueueEngine([authProvider],new TestFiles(),Path.Combine(root,"auth-queue.json")))
        {
            await authQueue.InitializeAsync();var connection=await authQueue.BeginConnectAsync(StoreKind.Amazon);Check(connection.RequiresAuthorizationCode&&connection.Store==StoreKind.Amazon,"normal StoreDigital BeginConnect contract");
            var connected=await authQueue.CompleteConnectAsync(connection.SessionId,"secret-code");Check(connected.Accepted,"normal StoreDigital CompleteConnect actual provider");var currentAccount=connected.Snapshot.Accounts.Single(a=>a.Store==StoreKind.Amazon);
            Check(!(await authQueue.DisconnectAsync(StoreKind.Amazon,currentAccount.Revision-1)).Accepted,"stale account revision cannot sign out replacement");
            Check((await authQueue.RefreshAsync(StoreKind.Amazon,currentAccount.Revision)).Accepted,"normal StoreDigital library refresh");
            Check((await authQueue.DisconnectAsync(StoreKind.Amazon,currentAccount.Revision)).Accepted,"normal StoreDigital Disconnect contract");
        }
        var missing=new StoreAmazonProvider(Path.Combine(root,"missing-runtime"),Path.Combine(root,"missing-state"),Path.Combine(root,"host.exe"));Check(!missing.Capabilities.CanConnect&&missing.Capabilities.InstallKind==StoreInstallKind.Unavailable,"missing runtime honest capability");
        await Refuses(()=>Task.Run(()=>missing.AuthorizationUri),"missing runtime cannot start auth");
        var waiting=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var observed=new List<TransferProgress>();
        var protocol=AmazonWorker.ReadAsync(new StringReader("{bad-json}\n"),new StringReader(""),_=>waiting.Task,observed.Add);
        await Task.Delay(10);Check(!protocol.IsCompleted,"bad protocol cannot release live worker ownership");waiting.SetResult();Check(!(await protocol).Drained,"bad protocol never invents drain");
        var validRows="{\"kind\":\"stderr\",\"line\":\"= Progress: 50.00 10/20, Running for: 00:00:01\"}\n{\"kind\":\"exit\",\"exitCode\":0,\"stopRequested\":false,\"drained\":true,\"forced\":false}\n";
        var workerResult=await AmazonWorker.ReadAsync(new StringReader(validRows),new StringReader("discarded raw errors"),_=>Task.CompletedTask,observed.Add);Check(workerResult.Drained&&workerResult.ExitCode==0&&observed.Single().DownloadedBytes==10&&observed.Single().ProgressPercent==50,"actual worker framing and real byte progress");
        var missingExit=await AmazonWorker.ReadAsync(new StringReader(""),new StringReader(""),_=>Task.CompletedTask,_=>{});Check(!missingExit.Drained,"EOF alone not owned drain receipt");
        fake.Library="[{\"id\":\"entitlement-id\",\"product\":{\"id\":\"amazon-test\",\"title\":\"Synthetic owned title\"}}]";
        await using(var unknownQueue=new StoreQueueEngine([authProvider],new TestFiles(),Path.Combine(root,"unknown-queue.json")))
        {
            await unknownQueue.InitializeAsync();var connect=await unknownQueue.BeginConnectAsync(StoreKind.Amazon);var connected=await unknownQueue.CompleteConnectAsync(connect.SessionId,"secret-code");var revision=connected.Snapshot.Accounts.Single(a=>a.Store==StoreKind.Amazon).Revision;
            Check((await unknownQueue.RefreshAsync(StoreKind.Amazon,revision)).Accepted,"unknown-drain fixture owns genuine fresh provider account");var owner=Guid.NewGuid();var now=DateTimeOffset.UtcNow;await unknownQueue.ObserveSteamAsync(new(owner,1,now,false),true);
            var unknownKey=new StoreGameKey(StoreKind.Amazon,"amazon-test");var destination3=Path.Combine(root,"queue unknown-drain destination");var prep3=await unknownQueue.PrepareInstallAsync(unknownKey,revision,destination3);var workerCount=workers.Count;
            var unknown=await unknownQueue.EnqueueAsync(new(unknownKey,revision,destination3,Guid.NewGuid(),prep3.SelectionNonce!.Value,prep3.Revision));await Wait(()=>workers.Count>workerCount);workers[^1].Complete(3,false,drained:false);
            await Wait(async()=> (await unknownQueue.GetSnapshotAsync()).Jobs.Single().ErrorCode=="worker_drain_unconfirmed");var blocked=(await unknownQueue.GetSnapshotAsync()).Jobs.Single();
            Check(blocked.State==StoreJobState.Interrupted&&!blocked.CanCancel&&!blocked.CanResume&&!blocked.CanDownloadNow,"unknown drain retains ownership and denies further commands");
            using(var timeout=new CancellationTokenSource(20))Check(!await unknownQueue.QuiesceAsync(timeout.Token),"unknown drain reports blocked quiescence");
            workerCount=workers.Count;await unknownQueue.ObserveSteamAsync(new(owner,2,DateTimeOffset.UtcNow,false),true);Check(workers.Count==workerCount,"unknown drain cannot start second worker");
            await Refuses(()=>authProvider.ListAsync(default),"unknown drain retains actual native credential gate");
        }
        Console.WriteLine($"PASS {count} actual Amazon/provider/queue checks; no network, OAuth, third-party runtime or game execution");
    }
    static async Task AuthenticationResponses(string bundle,string root,byte[] manifest,RSA signer)
    {
        var cases=new (string Name,object? Expiry,bool Missing,bool Accepted)[] {
            ("number",3600,false,true),("numeric-string","3600",false,true),("numeric-string-maplanding","3600",false,true),("refresh-string",1,false,true),
            ("zero",0,false,false),("negative",-1,false,false),("zero-string","0",false,false),
            ("negative-string","-1",false,false),("nan","NaN",false,false),("infinite","Infinity",false,false),
            ("malformed","invalid",false,false),("boolean",true,false,false),("null",null,false,false),
            ("object",new {},false,false),("missing",null,true,false)
        };
        foreach(var item in cases)
        {
            var network=new FakeNetwork(manifest){Expiry=item.Expiry,MissingExpiry=item.Missing,RefreshExpiry="3600"};
            var state=Path.Combine(root,"auth-response-"+item.Name);
            Task<(int,string)> Query(string exe,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> env,CancellationToken token)
            {
                if(args.SequenceEqual(new[]{"library","sync","--force"}))return Task.FromResult((0,""));
                if(args.SequenceEqual(new[]{"library","list","--json"}))return Task.FromResult((0,network.Library));
                throw new Exception("No real Nile process is allowed in auth tests");
            }
            var provider=new StoreAmazonProvider(bundle,state,Path.Combine(root,"unexecuted-host.exe"),new HttpClient(network),Query,null,bytes=>AmazonManifest.Parse(bytes,signer));
            var uri=provider.AuthorizationUri;
            network.ExpectedChallenge=Parameter(uri,"openid.oa2.code_challenge");
            network.ExpectedClient=Parameter(uri,"openid.oa2.client_id")[7..];
            ProviderAccount? account=null;StoreOperationException? error=null;
            var callback=item.Name=="numeric-string"?"https://www.amazon.com/?openid.oa2.authorization_code=secret-code":
                item.Name=="numeric-string-maplanding"?"https://www.amazon.com/ap/maplanding?openid.oa2.authorization_code=secret-code":"secret-code";
            try{account=await provider.ConnectAsync(callback,default);}catch(StoreOperationException e){error=e;}
            Check((account!=null)==item.Accepted&&((error==null)==item.Accepted),"actual provider validates bearer expiry "+item.Name);
            Check(network.Registers==1&&network.SecretSeen&&network.PkceMatches&&network.ClientMatches,"same login PKCE and device binding "+item.Name);
            Check(network.UserAgentSeen,"Nile-compatible registration user agent "+item.Name);
            Check((await provider.GetAccountAsync(default)!=null)==item.Accepted,"only accepted bearer response publishes DPAPI account "+item.Name);
            if(item.Accepted)
            {
                Check((await provider.ListAsync(default)).Single().Id=="amazon-test","accepted registration survives real provider library flow "+item.Name);
                var size=await provider.GetInstallSizeAsync("amazon-test",default);
                Check(size.DownloadBytes==20&&size.RequiredBytes==20,"accepted expiry survives real manifest planning "+item.Name);
                if(item.Name=="refresh-string")
                {
                    Check(network.Refreshes==1&&network.RefreshAgentSeen,"expiring login renews with numeric-string token expiry and Nile agent");
                    size=await provider.GetInstallSizeAsync("amazon-test",default);
                    Check(network.Refreshes==1&&size.DownloadBytes==20,"renewed string expiry persists without immediate refresh loop");
                    var reopened=new StoreAmazonProvider(bundle,state,Path.Combine(root,"unexecuted-host.exe"),new HttpClient(network),Query,null,bytes=>AmazonManifest.Parse(bytes,signer));
                    size=await reopened.GetInstallSizeAsync("amazon-test",default);
                    Check(network.Refreshes==1&&size.RequiredBytes==20,"renewed string expiry survives provider restart and DPAPI roundtrip");
                }
            }
            var calls=network.Registers;
            await Refuses(()=>provider.ConnectAsync("secret-code",default),"completed or refused login is one shot "+item.Name);
            Check(network.Registers==calls,"replayed callback sends no second registration "+item.Name);
        }
    }
    static string Parameter(Uri uri,string name)=>uri.Query.TrimStart('?').Split('&').Select(value=>value.Split('=',2))
        .Where(value=>value.Length==2&&Uri.UnescapeDataString(value[0])==name).Select(value=>Uri.UnescapeDataString(value[1])).Single();
    static async Task AuthenticationLineage(string bundle,string root)
    {
        void Expect(FakeNetwork network,StoreConnectSession session)
        {
            network.ExpectedChallenge=Parameter(session.AuthorizationUri,"openid.oa2.code_challenge");
            network.ExpectedClient=Parameter(session.AuthorizationUri,"openid.oa2.client_id")[7..];
        }
        var network=new FakeNetwork([]);
        var provider=new StoreAmazonProvider(bundle,Path.Combine(root,"auth-retry-state"),Path.Combine(root,"unexecuted-host.exe"),new HttpClient(network));
        await using(var queue=new StoreQueueEngine([provider],new TestFiles(),Path.Combine(root,"auth-retry-queue.json")))
        {
            await queue.InitializeAsync();
            var retired=await queue.BeginConnectAsync(StoreKind.Amazon);
            var current=await queue.BeginConnectAsync(StoreKind.Amazon);Expect(network,current);
            var stale=await queue.CompleteConnectAsync(retired.SessionId,"secret-code");
            Check(!stale.Accepted&&stale.ErrorCode=="connect_expired_or_invalid"&&network.Registers==0,"retired Amazon window cannot consume current verifier or send HTTP");
            var connected=await queue.CompleteConnectAsync(current.SessionId,"secret-code");
            Check(connected.Accepted&&network.Registers==1&&network.PkceMatches&&network.ClientMatches,"fresh Amazon retry retains its own verifier and connects");
            var replay=await queue.CompleteConnectAsync(current.SessionId,"secret-code");
            Check(!replay.Accepted&&network.Registers==1,"fresh successful callback remains one shot");
        }
        network=new FakeNetwork([]){RegistrationEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),RegistrationRelease=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        provider=new StoreAmazonProvider(bundle,Path.Combine(root,"auth-inflight-state"),Path.Combine(root,"unexecuted-host.exe"),new HttpClient(network));
        await using(var queue=new StoreQueueEngine([provider],new TestFiles(),Path.Combine(root,"auth-inflight-queue.json")))
        {
            await queue.InitializeAsync();var session=await queue.BeginConnectAsync(StoreKind.Amazon);Expect(network,session);
            var connecting=queue.CompleteConnectAsync(session.SessionId,"secret-code");
            await network.RegistrationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            string? denied=null;try{await queue.BeginConnectAsync(StoreKind.Amazon);}catch(StoreOperationException e){denied=e.Code;}
            network.RegistrationRelease.SetResult();var connected=await connecting;
            Check(denied=="account_busy"&&connected.Accepted&&network.Registers==1,"in-flight Amazon registration rejects a second Begin without replacing verifier");
        }
        network=new FakeNetwork([]){RegistrationEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),RegistrationRelease=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        provider=new StoreAmazonProvider(bundle,Path.Combine(root,"auth-cancel-state"),Path.Combine(root,"unexecuted-host.exe"),new HttpClient(network));
        await using(var queue=new StoreQueueEngine([provider],new TestFiles(),Path.Combine(root,"auth-cancel-queue.json")))
        {
            await queue.InitializeAsync();var session=await queue.BeginConnectAsync(StoreKind.Amazon);Expect(network,session);
            using var stop=new CancellationTokenSource();var connecting=queue.CompleteConnectAsync(session.SessionId,"secret-code",stop.Token);
            await network.RegistrationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));stop.Cancel();var canceled=await connecting;
            Check(!canceled.Accepted&&canceled.ErrorCode=="authentication_canceled","canceled Amazon exchange releases Connecting reservation");
            network.RegistrationRelease=null;var retry=await queue.BeginConnectAsync(StoreKind.Amazon);Expect(network,retry);
            Check((await queue.CompleteConnectAsync(retry.SessionId,"secret-code")).Accepted&&network.Registers==2,"canceled exchange can retry with new own verifier");
        }
        network=new FakeNetwork([]);provider=new StoreAmazonProvider(bundle,Path.Combine(root,"auth-begin-fault-state"),Path.Combine(root,"unexecuted-host.exe"),new HttpClient(network));
        var wrapper=new BeginFault(provider){Fail=true};
        await using(var queue=new StoreQueueEngine([wrapper],new TestFiles(),Path.Combine(root,"auth-begin-fault-queue.json")))
        {
            int Sessions()=>((System.Collections.IDictionary)typeof(StoreQueueEngine).GetField("connections",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(queue)!).Count;
            await queue.InitializeAsync();await Refuses(()=>queue.BeginConnectAsync(StoreKind.Amazon),"authorization URL failure propagates");
            Check(Sessions()==0,"failed authorization URL publishes no orphan session");
            wrapper.Fail=false;var current=await queue.BeginConnectAsync(StoreKind.Amazon);Expect(network,current);
            wrapper.Fail=true;await Refuses(()=>queue.BeginConnectAsync(StoreKind.Amazon),"retry URL failure propagates");
            Check(Sessions()==1&&(await queue.CompleteConnectAsync(current.SessionId,"secret-code")).Accepted,"failed new authorization URL preserves the prior valid session");
        }
    }
    static async Task Queue(StoreAmazonProvider provider,string root,List<FakeWorker> workers,FakeNetwork network)
    {
        var clock=DateTimeOffset.UtcNow;await using var queue=new StoreQueueEngine([provider],new TestFiles(),Path.Combine(root,"queue.json"),()=>clock);
        await queue.InitializeAsync();var snapshot=await queue.GetSnapshotAsync();var account=snapshot.Accounts.Single(a=>a.Store==StoreKind.Amazon);Check(account.State==StoreConnectionState.Connected,"queue initializes actual Amazon account");
        Check((await queue.RefreshAsync(StoreKind.Amazon,account.Revision)).Accepted,"queue actual Amazon refresh");
        var key=new StoreGameKey(StoreKind.Amazon,"amazon-test");var destination=Path.Combine(root,"queue exact destination");Directory.CreateDirectory(destination);
        // Clear only this fixture's provider-installed receipt; no user state.
        var native=Path.Combine(root,"state","amazon","native","nile");File.WriteAllText(Path.Combine(native,"installed.json"),"[]");
        var prepare=await queue.PrepareInstallAsync(key,account.Revision,destination);Check(prepare.SelectionNonce!=null&&prepare.ConfirmedPath==destination,"queue exact destination nonce and size");
        var owner=Guid.NewGuid();Check(await queue.ObserveSteamAsync(new(owner,1,clock,false),true),"authenticated Steam idle admission");
        var beforeWorkers=workers.Count;
        var enqueue=await queue.EnqueueAsync(new(key,account.Revision,destination,Guid.NewGuid(),prepare.SelectionNonce!.Value,prepare.Revision));Check(enqueue.Accepted&&enqueue.JobId!=null,"queue enqueue actual Amazon adapter");
        await Wait(()=>workers.Count>beforeWorkers);snapshot=await queue.GetSnapshotAsync();var j=snapshot.Jobs.Single();Check(!j.CanPause&&!j.CanResume&&j.CanCancel,"job DTO respects real provider capabilities");
        Check(!(await queue.ControlAsync(new(j.JobId,j.Revision,StoreJobAction.Pause,Guid.NewGuid()))).Accepted,"API cannot force unsupported pause");
        Check(await queue.ObserveSteamAsync(new(owner,2,clock,true),true),"real Steam priority starts stop");
        await Wait(()=>workers[^1].Stops==1);Check((await queue.GetSnapshotAsync()).Jobs.Single().State!=StoreJobState.PausedBySteam,"request stop no fake paused state");
        workers[^1].Complete(1,false,stop:true);await Wait(async()=> (await queue.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Interrupted);
        j=(await queue.GetSnapshotAsync()).Jobs.Single();Check(!j.CanResume&&!j.CanDownloadNow,"unsupported checkpoint cannot restart via dropdown");
        await queue.ObserveSteamAsync(new(owner,3,clock,false),true);Check((await queue.GetSnapshotAsync()).Jobs.Single().State==StoreJobState.Interrupted,"Steam idle no automatic retry unsupported transfer");
        Check(!(await queue.ControlAsync(new(j.JobId,j.Revision,StoreJobAction.Resume,Guid.NewGuid()))).Accepted,"unsupported resume API refused");
        var cancel=await queue.ControlAsync(new(j.JobId,j.Revision,StoreJobAction.Cancel,Guid.NewGuid()));Check(cancel.Accepted&&cancel.Snapshot.Jobs.Single().State==StoreJobState.Canceled,"owned interrupted job may be canceled without deleting files");
        var destination2=Path.Combine(root,"queue cancel destination");var prep2=await queue.PrepareInstallAsync(key,account.Revision,destination2);
        beforeWorkers=workers.Count;
        var next=await queue.EnqueueAsync(new(key,account.Revision,destination2,Guid.NewGuid(),prep2.SelectionNonce!.Value,prep2.Revision));Check(next.Accepted,"second owned job enqueued after first fully drained");
        await Wait(()=>workers.Count>beforeWorkers);var active=(await queue.GetSnapshotAsync()).Jobs.Single(x=>x.JobId==next.JobId);
        var stopping=await queue.ControlAsync(new(active.JobId,active.Revision,StoreJobAction.Cancel,Guid.NewGuid()));Check(stopping.Accepted&&stopping.Snapshot.Jobs.Single(x=>x.JobId==active.JobId).State==StoreJobState.Canceling,"active cancel acknowledgement remains Canceling");
        await Wait(()=>workers[^1].Stops==1);workers[^1].Complete(1,false,stop:true);await Wait(async()=> (await queue.GetSnapshotAsync()).Jobs.Single(x=>x.JobId==active.JobId).State==StoreJobState.Canceled);
        Check((await queue.GetSnapshotAsync()).Jobs.Single(x=>x.JobId==active.JobId).State==StoreJobState.Canceled,"actual drained receipt authorizes canceled");
        var before=(await queue.GetSnapshotAsync()).Games.Count;network.Library="[{\"id\":\"e\",\"product\":{\"id\":\"bad\"}}]";
        Check(!(await queue.RefreshAsync(StoreKind.Amazon,account.Revision)).Accepted&&(await queue.GetSnapshotAsync()).Games.Count==before,"partial invalid library preserves last complete snapshot");
    }
    static async Task Wait(Func<bool> predicate){for(var i=0;i<1000;i++){if(predicate())return;await Task.Delay(2);}throw new Exception("fixture timeout");}
    static async Task Wait(Func<Task<bool>> predicate){for(var i=0;i<1000;i++){if(await predicate())return;await Task.Delay(2);}throw new Exception("fixture timeout");}
    static byte[] V(ulong value){var data=new List<byte>();do{var b=(byte)(value&127);value>>=7;data.Add((byte)(b|(value>0?128:0)));}while(value>0);return data.ToArray();}
    static byte[] N(int tag,ulong value)=>V((ulong)(tag<<3)).Concat(V(value)).ToArray();
    static byte[] B(int tag,byte[] bytes)=>V((ulong)(tag<<3|2)).Concat(V((ulong)bytes.Length)).Concat(bytes).ToArray();
    static byte[] ManifestBytes(string path,byte[] bytes)=>B(1,B(2,B(1,Encoding.UTF8.GetBytes(path)).Concat(N(3,(ulong)bytes.Length)).Concat(B(5,N(1,0).Concat(B(2,SHA256.HashData(bytes))).ToArray())).ToArray()));
    static byte[] Sign(byte[] plain,RSA key,byte[]? compressed=null,ulong compression=0){var header=B(1,N(1,compression)).Concat(B(3,N(1,0).Concat(B(2,key.SignData(plain,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))).ToArray())).ToArray();var size=new byte[4];System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(size,(uint)header.Length);return size.Concat(header).Concat(compressed??plain).ToArray();}
    sealed class FakeNetwork(byte[] manifest):HttpMessageHandler
    {
        internal string Library="[{\"id\":\"entitlement-id\",\"product\":{\"id\":\"amazon-test\",\"title\":\"Synthetic owned title\",\"productDetail\":{\"iconUrl\":\"https://m.media-amazon.com/images/I/Fixture.jpg\",\"details\":{\"logoUrl\":\"https://m.media-amazon.com/images/fixture.jpg\"}}}}]";internal int Registers;internal bool SecretSeen;internal Action? BeforeManifest;
        internal object? Expiry=3600,RefreshExpiry=3600;internal bool MissingExpiry,UserAgentSeen,RefreshAgentSeen,PkceMatches,ClientMatches;internal string? ExpectedChallenge,ExpectedClient;
        internal int Refreshes;internal TaskCompletionSource? RegistrationEntered,RegistrationRelease;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.AbsoluteUri=="https://api.amazon.com/auth/register")
            {
                Registers++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var data=body.RootElement;SecretSeen=data.GetProperty("auth_data").GetProperty("authorization_code").GetString()=="secret-code"&&data.GetProperty("auth_data").GetProperty("code_verifier").GetString()!.Length==43;
                UserAgentSeen=request.Headers.UserAgent.ToString()=="AGSLauncher/1.0.0";
                var verifier=data.GetProperty("auth_data").GetProperty("code_verifier").GetString()!;
                var challenge=Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
                PkceMatches=ExpectedChallenge==null||ExpectedChallenge==challenge;
                ClientMatches=ExpectedClient==null||ExpectedClient==data.GetProperty("auth_data").GetProperty("client_id").GetString();
                Check(PkceMatches&&ClientMatches,"registration binds selected authorization URL and verifier");
                RegistrationEntered?.TrySetResult();if(RegistrationRelease!=null)await RegistrationRelease.Task.WaitAsync(token);
                var reply=JsonSerializer.SerializeToNode(new {response=new {success=new {extensions=new {customer_info=new {user_id="test-user",given_name="Test name"},device_info=new {device_serial_number=data.GetProperty("registration_data").GetProperty("device_serial").GetString()}},tokens=new {bearer=new {access_token="access-secret",refresh_token="refresh-secret",expires_in=Expiry}}}}})!;
                if(MissingExpiry)reply["response"]!["success"]!["tokens"]!["bearer"]!.AsObject().Remove("expires_in");
                return Json(reply);
            }
            if(request.RequestUri.AbsoluteUri=="https://api.amazon.com/auth/token")
            {
                Refreshes++;RefreshAgentSeen=request.Headers.UserAgent.ToString()=="AGSLauncher/1.0.0";
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Check(body.RootElement.GetProperty("source_token").GetString()=="refresh-secret","refresh token stays inside actual token request body");
                return Json(new {access_token="access-secret",expires_in=RefreshExpiry});
            }
            if(request.RequestUri.AbsoluteUri=="https://gaming.amazon.com/api/distribution/v2/public")
            {
                Check(request.Headers.GetValues("x-amzn-token").Single()=="access-secret","access token only HTTPS header");return Json(new {versionId="version-1",downloadUrl="https://fixture.s3.amazonaws.com/title?signature=test"});
            }
            if(request.RequestUri.Host=="fixture.s3.amazonaws.com"&&request.RequestUri.AbsolutePath=="/title/manifest.proto"){BeforeManifest?.Invoke();return new(HttpStatusCode.OK){Content=new ByteArrayContent(manifest)};}
            throw new Exception("unexpected network "+request.RequestUri.Host);
        }
        static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
    }
    sealed class BeginFault(IStoreProvider inner):IStoreProvider
    {
        internal bool Fail;
        public StoreKind Store=>inner.Store;public StoreCapabilities Capabilities=>inner.Capabilities;
        public Uri AuthorizationUri=>Fail?throw new StoreOperationException("synthetic_begin_failure"):inner.AuthorizationUri;
        public Task<ProviderAccount?> GetAccountAsync(CancellationToken t)=>inner.GetAccountAsync(t);
        public Task<ProviderAccount> ConnectAsync(string code,CancellationToken t)=>inner.ConnectAsync(code,t);
        public Task DisconnectAsync(CancellationToken t)=>inner.DisconnectAsync(t);
        public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken t)=>inner.ListAsync(t);
        public Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken t)=>inner.GetInstallSizeAsync(id,t);
        public Task<ProviderLaunch?> ResolveLaunchAsync(string id,string path,CancellationToken t)=>inner.ResolveLaunchAsync(id,path,t);
        public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id,string path,CancellationToken t)=>inner.PrepareLaunchAsync(id,path,t);
        public Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken t)=>inner.StartAsync(request,progress,t);
    }
    sealed class FakeWorker(string path,string native,byte[] manifest,Action<TransferProgress> progress,bool skipPayloadLikeNile=false):IAmazonWorker
    {
        // Pinned Nile 1.2.0 DownloadManager compares the cached raw manifest
        // without consulting destination files; zero changes skips all payload.
        internal bool SkipPayloadLikeNile=>skipPayloadLikeNile;
        readonly TaskCompletionSource<AmazonWorkerResult> result=new(TaskCreationOptions.RunContinuationsAsynchronously);internal int Stops;public Task<AmazonWorkerResult> Completion=>result.Task;
        public Task StopAsync(CancellationToken token){Stops++;return Task.CompletedTask;}
        internal void Complete(int code,bool files,bool stop=false,bool drained=true)
        {
            if(files){Directory.CreateDirectory(path);if(!skipPayloadLikeNile)File.WriteAllText(Path.Combine(path,"game.exe"),"synthetic executable");Directory.CreateDirectory(Path.Combine(native,"manifests"));File.WriteAllBytes(Path.Combine(native,"manifests","amazon-test.raw"),manifest);File.WriteAllText(Path.Combine(native,"installed.json"),JsonSerializer.Serialize(new[]{new {id="amazon-test",path,version="version-1",size=20}}));progress(new(20,20,null,"verifying"));}
            result.TrySetResult(new(code,stop,drained,false));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    sealed class TestFiles:IStoreInstallFileSystem
    {
        public IReadOnlyList<StoreInstallLocation> Locations()=>[new("C:\\","test",1000000,true)];public string CanonicalDestination(string path)=>Path.GetFullPath(path);public long FreeBytes(string path)=>1000000;
        public bool CanClaim(string path,Guid? job)=>!File.Exists(Path.Combine(path,".playhub-install-owner"))||job!=null&&File.ReadAllText(Path.Combine(path,".playhub-install-owner"))==job.ToString();
        public bool Claim(string path,Guid job){Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,".playhub-install-owner"),job.ToString("D"));return true;}
    }
}


