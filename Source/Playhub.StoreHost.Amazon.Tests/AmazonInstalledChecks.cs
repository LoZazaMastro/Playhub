// SPDX-License-Identifier: MIT. Original inert Amazon manifest/ownership uninstall checks.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

internal static class AmazonInstalledChecks
{
    private static int count;
    private static void Check(bool condition,string name){count++;if(!condition)throw new Exception("FAIL Amazon uninstall "+name);}
    private static async Task Reject(Func<Task> action,string name)
    {try{await action();}catch(StoreOperationException){Check(true,name);return;}throw new Exception("FAIL Amazon uninstall accepted "+name);}
    internal static async Task Run(string bundle)
    {
        var root=Path.Combine(Path.GetTempPath(),"Playhub-Amazon-Uninstall-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var state=Path.Combine(root,"state");var native=Path.Combine(state,"amazon","native","nile");
        var game=Path.Combine(root,"owned-game");var other=Path.Combine(root,"other-game");Directory.CreateDirectory(other);
        var id="uninstall-fixture";var job=Guid.NewGuid();
        using var signer=RSA.Create(2048);
        var files=new Dictionary<string,byte[]> {
            ["fuel.json"]=Encoding.UTF8.GetBytes("{SchemaVersion:1,Main:{Command:'bin/game.exe'}}"),
            ["bin/game.exe"]=Encoding.UTF8.GetBytes("inert executable; never run"),
            ["data.bin"]=Encoding.UTF8.GetBytes("inert signed game data")
        };
        var signed=Sign(files,signer,false);
        var library=JsonSerializer.Serialize(new[]{new {id="entitlement-fixture",product=new {id,sku="fixture-sku",title="Inert installed game"}}});
        Task<(int,string)> Query(string exe,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> env,CancellationToken token)
        {
            if(args.SequenceEqual(new[]{"library","sync","--force"}))return Task.FromResult((0,""));
            if(args.SequenceEqual(new[]{"library","list","--json"}))return Task.FromResult((0,library));
            throw new Exception("No CLI execution is allowed in Amazon uninstall checks");
        }
        var provider=new StoreAmazonProvider(bundle,state,Path.Combine(root,"unexecuted-host.exe"),new HttpClient(new NoNetwork()),Query,
            (_,_,_,_,_,_,_)=>throw new Exception("No worker execution is allowed"),bytes=>AmazonManifest.Parse(bytes,signer));
        StoreCredentialVault.Write(Path.Combine(state,"amazon","credentials.dpapi"),"""{"extensions":{"customer_info":{"user_id":"inert-owner","given_name":"Fixture"}},"tokens":{"bearer":{"access_token":"inert-only","refresh_token":"inert-only","expires_in":3600}},"NILE":{"token_obtain_time":9999999999}}""");
        var manifest=Path.Combine(native,"manifests",id+".raw");Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        var installed=Path.Combine(native,"installed.json");
        void Records(string? otherPath=null,string version="fixture-version")=>File.WriteAllText(installed,JsonSerializer.Serialize(new[]{
            new {id,path=game,version,size=files.Values.Sum(bytes=>bytes.Length)},new {id="other-product",path=otherPath??other,version="unrelated-version",size=99}}));
        void Reset()
        {
            Directory.CreateDirectory(game);
            foreach(var file in files){var path=Path.Combine(game,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,file.Value);}
            File.WriteAllText(Path.Combine(game,".playhub-install-owner"),job.ToString("D"));File.WriteAllBytes(manifest,signed);Records();
        }
        void Preserved(string name)=>Check(files.All(file=>File.Exists(Path.Combine(game,file.Key))&&File.ReadAllBytes(Path.Combine(game,file.Key)).SequenceEqual(file.Value)),name);
        Reset();await provider.ListAsync(default);
        var extra=Path.Combine(game,"user-extra.txt");File.WriteAllText(extra,"preserve extra");
        var save=Path.Combine(game,"Saves","save.dat");Directory.CreateDirectory(Path.GetDirectoryName(save)!);File.WriteAllText(save,"preserve save");
        Check(await provider.CanUninstallAsync(id,game,job,default),"valid exact completed ownership exposes removal");
        var plan=await provider.PrepareUninstallAsync(id,game,job,default);
        Check(plan.Files.Count==3&&plan.Files.All(file=>file.HashAlgorithm is null&&file.Chunks is null)&&plan.Files[^1].RelativePath=="bin/game.exe","signed manifest binds exact removal paths and current sizes, with image last");
        Preserved("preparation does not mutate payload");
        var originalRecords=File.ReadAllText(installed);var unrelatedRecords=System.Text.Json.Nodes.JsonNode.Parse(originalRecords)!.AsArray();
        unrelatedRecords[1]!["version"]="unrelated-library-refresh";File.WriteAllText(installed,unrelatedRecords.ToJsonString());
        Check((await provider.PrepareUninstallAsync(id,game,job,default)).ManifestHash==plan.ManifestHash,"another product registration update cannot invalidate this game's confirmation");
        File.WriteAllText(installed,originalRecords);
        var publicLibrary=Path.Combine(native,"library.json");var originalLibrary=File.ReadAllText(publicLibrary);
        var updatedLibrary=System.Text.Json.Nodes.JsonNode.Parse(originalLibrary)!.AsArray();updatedLibrary.Add(System.Text.Json.Nodes.JsonNode.Parse("{\"id\":\"unrelated-entitlement\",\"product\":{\"id\":\"unrelated-product\",\"title\":\"Background refresh\"}}"));
        File.WriteAllText(publicLibrary,updatedLibrary.ToJsonString());
        Check((await provider.PrepareUninstallAsync(id,game,job,default)).ManifestHash==plan.ManifestHash,"unrelated catalog additions cannot invalidate the selected signed manifest and install receipt");
        File.WriteAllText(publicLibrary,originalLibrary);
        var removalGate=(StoreNativeOperationGate)typeof(StoreAmazonProvider).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(provider)!;
        foreach(bool legacyRecovery in new[]{false,true})
        {
            Check(removalGate.TryWait(),"fixture owns short background provider operation");
            Task<StoreOwnedUninstallPlan?> waiting=legacyRecovery?
                ((IStoreLegacyInstalledGameProvider)provider).VerifyLegacyInstallationAsync(id,game,default):
                PrepareNullable();
            async Task<StoreOwnedUninstallPlan?> PrepareNullable()=>await provider.PrepareUninstallAsync(id,game,job,default);
            try{await Task.Delay(30);Check(!waiting.IsCompleted,"one explicit removal action waits for transient provider contention rather than requiring another click");}
            finally{removalGate.Release();}
            Check((await waiting)?.Files.Count==3,"waiting removal preparation completes after same provider background work drains");
            Preserved("waiting preparation remains read-only");
        }
        Check(removalGate.TryWait(),"fixture reserves gate for canceled waiter");
        using(var canceledWait=new CancellationTokenSource())
        {
            var waiting=provider.PrepareUninstallAsync(id,game,job,canceledWait.Token);canceledWait.Cancel();
            try{await waiting;throw new Exception("canceled provider wait was accepted");}catch(OperationCanceledException){Check(true,"cancellation stops pending preparation without taking another operation's lock");}
            finally{removalGate.Release();}
        }
        Check(await provider.CanUninstallAsync(id,game,job,default),"canceled waiter leaves the provider gate usable");
        File.WriteAllText(Path.Combine(game,"data.bin"),"modified user data");
        Check(await provider.CanUninstallAsync(id,game,job,default),"status exposes capability without hashing modified bulk asset");
        var modifiedPlan=await provider.PrepareUninstallAsync(id,game,job,default);
        Check(modifiedPlan.Files.Single(f=>f.RelativePath=="data.bin").Size==new FileInfo(Path.Combine(game,"data.bin")).Length,"modified asset can be explicitly removed using its current retained size");
        await Reject(()=>provider.UninstallAsync(plan,default),"modified data cannot authorize deletion");Reset();
        File.Delete(Path.Combine(game,"data.bin"));
        Check(await provider.CanUninstallAsync(id,game,job,default),"lightweight status does not scan missing unrelated assets");
        Check((await provider.PrepareUninstallAsync(id,game,job,default)).Files.Count==3,"already missing payload does not prevent removing the remaining game");Reset();
        File.Delete(Path.Combine(game,"bin/game.exe"));
        Check(await provider.CanUninstallAsync(id,game,job,default)&& (await provider.PrepareUninstallAsync(id,game,job,default)).Files.Count==3,"a missing executable does not make an installed game impossible to remove");Reset();
        var vault=Path.Combine(state,"amazon","credentials.dpapi");var protectedAccount=File.ReadAllBytes(vault);File.Delete(vault);
        Check((await provider.PrepareUninstallAsync(id,game,job,default)).Files.Count==3,"local removal does not depend on a connected store account");File.WriteAllBytes(vault,protectedAccount);
        using(var heldAsset=new FileStream(Path.Combine(game,"data.bin"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Check(await provider.CanUninstallAsync(id,game,job,default),"status never opens bulk payload files");
        File.WriteAllText(Path.Combine(game,".playhub-install-owner"),Guid.NewGuid().ToString("D"));
        await Reject(()=>provider.UninstallAsync(plan,default),"owner changed after prepare");Preserved("wrong owner deletes zero files");Reset();
        plan=await provider.PrepareUninstallAsync(id,game,job,default);
        await Reject(()=>provider.UninstallAsync(plan with {Files=Array.AsReadOnly(plan.Files.Append(new StoreOwnedInstallFile("user-extra.txt",14,"SHA256",new string('0',64))).ToArray())},default),"caller cannot expand retained deletion manifest");
        Preserved("forged file list deletes zero files");Check(File.ReadAllText(extra)=="preserve extra","forged extra target remains");
        Records(version:"changed-version");await Reject(()=>provider.UninstallAsync(plan,default),"installed receipt changed after prepare");Preserved("metadata change deletes zero files");Reset();
        foreach(var shared in new[]{game,Path.Combine(game,"DLC"),root})
        {Records(shared);await Reject(()=>provider.PrepareUninstallAsync(id,game,job,default),"shared or overlapping installation");Preserved("shared installation untouched");}
        Reset();plan=await provider.PrepareUninstallAsync(id,game,job,default);
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();
            try{await provider.UninstallAsync(plan,canceled.Token);throw new Exception("Accepted canceled uninstall");}
            catch(OperationCanceledException){Check(true,"canceled execution refuses before first deletion");}
        }
        Preserved("canceled execution deletes zero files");
        using(var held=new FileStream(Path.Combine(game,"bin","game.exe"),FileMode.Open,FileAccess.Read,FileShare.Read))
            await Reject(()=>provider.UninstallAsync(plan,default),"Windows image handle without DELETE sharing refuses whole preflight");
        Preserved("locked final image causes no earlier payload deletion");
        var damaged=signed.ToArray();damaged[^1]^=1;File.WriteAllBytes(manifest,damaged);
        await Reject(()=>provider.UninstallAsync(plan,default),"tampered signed manifest");Preserved("tampered manifest deletes zero files");Reset();
        if(Shake128.IsSupported)
        {
            signed=Sign(files,signer,true);Reset();
            var shake=await provider.PrepareUninstallAsync(id,game,job,default);
            Check(shake.Files.All(file=>file.HashAlgorithm is null&&file.Size==files[file.RelativePath].Length),"signed SHAKE128 manifest authorizes removal paths without re-reading all game contents");
        }
        plan=await provider.PrepareUninstallAsync(id,game,job,default);await provider.UninstallAsync(plan,default);
        Check(files.Keys.All(file=>!File.Exists(Path.Combine(game,file))),"only bound game payload files removed");
        Check(File.ReadAllText(extra)=="preserve extra"&&File.ReadAllText(save)=="preserve save","extra user file and save preserved byte for byte");
        Check(Directory.Exists(game)&&!File.Exists(Path.Combine(game,".playhub-install-owner")),"retained user files keep directory without stale owner marker");
        using(var records=JsonDocument.Parse(File.ReadAllText(installed)))
            Check(records.RootElement.GetArrayLength()==1&&records.RootElement[0].GetProperty("id").GetString()=="other-product","only exact Amazon registration removed");
        Check(await provider.GetAccountAsync(default)!=null&&await provider.ResolveLaunchAsync(id,game,default)==null,"account remains connected while removed install is unlaunchable");
        Check(!await provider.CanUninstallAsync(id,game,job,default),"removed registration cannot be removed again");
        Console.WriteLine($"PASS {count} Amazon owned-file uninstall checks; synthetic signed files only, no network/CLI/game execution");
    }
    private static byte[] V(ulong value){var result=new List<byte>();do{var part=(byte)(value&127);value>>=7;result.Add((byte)(part|(value>0?128:0)));}while(value>0);return result.ToArray();}
    private static byte[] N(int tag,ulong value)=>V((ulong)(tag<<3)).Concat(V(value)).ToArray();
    private static byte[] B(int tag,byte[] value)=>V((ulong)(tag<<3|2)).Concat(V((ulong)value.Length)).Concat(value).ToArray();
    private static byte[] Sign(Dictionary<string,byte[]> files,RSA key,bool shake)
    {
        var entries=files.SelectMany(file=>B(2,B(1,Encoding.UTF8.GetBytes(file.Key)).Concat(N(3,(ulong)file.Value.Length))
            .Concat(B(5,N(1,shake?1UL:0UL).Concat(B(2,shake?Shake128.HashData(file.Value,32):SHA256.HashData(file.Value))).ToArray())).ToArray())).ToArray();
        var plain=B(1,entries);var header=B(1,N(1,0)).Concat(B(3,N(1,0).Concat(B(2,key.SignData(plain,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))).ToArray())).ToArray();
        var size=new byte[4];System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(size,(uint)header.Length);return size.Concat(header).Concat(plain).ToArray();
    }
    private sealed class NoNetwork:HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("No real network in uninstall tests");}
}
