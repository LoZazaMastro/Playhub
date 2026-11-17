using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PlayhubSetup;
using Playhub.Services;

int count = 0;
void Check(bool value,string description) { if(!value)throw new Exception(description);count++;Console.WriteLine("PASS "+description); }
string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
JsonObject Obj(string text)=>JsonNode.Parse(text)!.AsObject();
string current="const actualPlugin = {onDismount(){ /* current code */ }};", old="const stalePlugin = {};";

var calls=new List<string>(); bool changed=false; bool missingLocation=false; bool exception=false;
Task<JsonObject> Cdp(string method,JsonObject? p)
{
    calls.Add(method);
    JsonObject value=method switch {
        "Debugger.enable" or "Runtime.releaseObjectGroup" => new(),
        "Runtime.evaluate" => exception?Obj("{\"exceptionDetails\":{}}"):Obj("{\"result\":{\"type\":\"object\",\"objectId\":\"held\"}}"),
        "Runtime.getProperties" when p?["objectId"]?.GetValue<string>()=="held" => Obj("{\"result\":[{\"name\":\"callback\",\"value\":{\"type\":\"function\",\"objectId\":\"callback\"}}]}"),
        "Runtime.getProperties" => missingLocation?Obj("{\"internalProperties\":[]}"):Obj("{\"internalProperties\":[{\"name\":\"[[FunctionLocation]]\",\"value\":{\"value\":{\"scriptId\":\"active-script\"}}}]}"),
        "Debugger.getScriptSource" => p?["scriptId"]?.GetValue<string>()=="active-script"? new(){["scriptSource"]=current}:throw new Exception("unbound script read"),
        "Runtime.callFunctionOn" => new(){["result"]=new JsonObject{["value"]=!changed}},
        _=>throw new Exception("unexpected CDP command "+method)
    };
    return Task.FromResult(value);
}
Check(await ActiveDeckyPluginProof.ReadAsync(Cdp,CancellationToken.None)==Hash(current),"hash comes from actual held callback script");
Check(calls.IndexOf("Debugger.getScriptSource")<calls.IndexOf("Runtime.callFunctionOn"),"active loader/plugin/callback rechecked after source read");
Check(calls[^1]=="Runtime.releaseObjectGroup","remote objects released on success");
changed=true;Check(await ActiveDeckyPluginProof.ReadAsync(Cdp,CancellationToken.None) is null,"loader/plugin replacement rejects otherwise correct source hash");changed=false;
missingLocation=true;Check(await ActiveDeckyPluginProof.ReadAsync(Cdp,CancellationToken.None) is null,"missing function location is unknown, not last parsed fallback");missingLocation=false;
exception=true;Check(await ActiveDeckyPluginProof.ReadAsync(Cdp,CancellationToken.None) is null,"evaluate exception cannot verify");exception=false;
using(var canceled=new CancellationTokenSource()){canceled.Cancel();Check(await ActiveDeckyPluginProof.ReadAsync(Cdp,canceled.Token) is null,"cancelled proof returns unknown");}

string root=Path.Combine(Path.GetTempPath(),"Playhub-Frontend-Contract-Checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try {
    Fixture Make(string name) {
        string install=Path.Combine(root,name,"install"),plugins=Path.Combine(root,name,"plugins"),src=Path.Combine(install,"Assets","GamingModeDeckyPlugin","gaming-mode"),dest=Path.Combine(plugins,"gaming-mode");
        Directory.CreateDirectory(Path.Combine(src,"dist"));Directory.CreateDirectory(Path.Combine(dest,"dist"));
        File.WriteAllText(Path.Combine(src,"plugin.json"),"{\"name\":\"Playhub\"}");File.WriteAllText(Path.Combine(dest,"plugin.json"),"{\"name\":\"Playhub\"}");
        File.WriteAllText(Path.Combine(src,"dist","index.js"),current);File.WriteAllText(Path.Combine(dest,"dist","index.js"),old);
        File.WriteAllText(Path.Combine(src,"main.py"),"current backend");
        return new(install,plugins,dest,Path.Combine(src,"dist","index.js"));
    }
    Task<JsonNode?> Ack(string _,CancellationToken ct)=>Task.FromResult<JsonNode?>(JsonValue.Create("reloaded"));
    if(args.Contains("--before")) {
        var f=Make("parent");DeckyPluginReload.MarkPending(f.Dest);
        bool accepted=await DeckyPluginReload.TryPendingAsync(f.Dest,Ack);
        Check(!accepted && File.Exists(Path.Combine(f.Dest,DeckyPluginReload.MarkerName)),"stale active callback must remain pending despite accepted reload request");
        return;
    }
    var stale=Make("stale");int reloads=0;
    var result=await BundledPlayhubMaintenance.RunAsync(stale.Install,stale.Plugins,(s,t)=>{reloads++;return Ack(s,t);},t=>Task.FromResult<string?>(Hash(old)),()=>BundledProcessSafety.Clear);
    Check(!result.Verified&&result.Status=="pending-active-proof","accepted reload with stale callback stays pending");
    Check(reloads==1,"stale callback never causes reload loop");
    Check(File.Exists(Path.Combine(stale.Dest,BundledPlayhubMaintenance.PendingName)),"pending survives failed active proof");
    Check(!File.Exists(Path.Combine(stale.Dest,BundledPlayhubMaintenance.VerifiedName)),"no verified receipt on stale callback");
    Check(File.ReadAllText(Path.Combine(stale.Dest,"dist","index.js"))==current,"new bytes on disk alone are insufficient");
    int reads=0;reloads=0;
    result=await BundledPlayhubMaintenance.RunAsync(stale.Install,stale.Plugins,(s,t)=>{reloads++;return Ack(s,t);},t=>Task.FromResult<string?>(++reads>=3?Hash(current):Hash(old)),()=>BundledProcessSafety.Clear);
    Check(result.Verified&&result.Status=="verified"&&result.ActiveSha256==Hash(current),"asynchronous loader replacement verified only after real callback hash changes");
    Check(reloads==1&&!File.Exists(Path.Combine(stale.Dest,BundledPlayhubMaintenance.PendingName)),"matching active code acknowledges exactly current generation");
    Check(File.Exists(Path.Combine(stale.Dest,BundledPlayhubMaintenance.VerifiedName)),"verified receipt persists exact code hash");
    reloads=0;
    result=await BundledPlayhubMaintenance.RunAsync(stale.Install,stale.Plugins,(s,t)=>{reloads++;return Ack(s,t);},t=>Task.FromResult<string?>(Hash(current)),()=>BundledProcessSafety.Clear);
    Check(result.Verified&&reloads==0,"already active exact code needs no reload");
    foreach(var safety in new[]{BundledProcessSafety.Active,BundledProcessSafety.Unknown}){
        var f=Make(safety.ToString());reloads=0;reads=0;
        result=await BundledPlayhubMaintenance.RunAsync(f.Install,f.Plugins,(s,t)=>{reloads++;return Ack(s,t);},t=>{reads++;return Task.FromResult<string?>(Hash(current));},()=>safety);
        Check(!result.Verified&&reloads==0&&reads==0&&File.ReadAllText(Path.Combine(f.Dest,"dist","index.js"))==old,"busy/unknown preserves running payload without loader or CDP proof "+safety);
        Check(File.Exists(Path.Combine(f.Dest,BundledPlayhubMaintenance.PendingName)),"busy/unknown retains pending "+safety);
    }
    var disabled=Make("disabled");
    var copyRace=Make("copy-race");int safetyReads=0;
    result=await BundledPlayhubMaintenance.RunAsync(copyRace.Install,copyRace.Plugins,Ack,t=>throw new Exception("must not read while game starts"),
        ()=>++safetyReads<=2?BundledProcessSafety.Clear:BundledProcessSafety.Active);
    Check(!result.Verified&&result.Status=="pending-session"&&result.ChangedFiles==1,
        "game starts after first payload write: no second overwrite or reload");
    result=await BundledPlayhubMaintenance.RunAsync(disabled.Install,disabled.Plugins,(s,t)=>Task.FromResult<JsonNode?>(JsonValue.Create("disabled")),t=>Task.FromResult<string?>(Hash(old)),()=>BundledProcessSafety.Clear);
    Check(!result.Verified&&File.Exists(Path.Combine(disabled.Dest,BundledPlayhubMaintenance.PendingName)),"disabled plugin remains pending without fabricated ready");
    var raced=Make("generation-race");
    result=await BundledPlayhubMaintenance.RunAsync(raced.Install,raced.Plugins,Ack,t=>{File.WriteAllText(Path.Combine(raced.Dest,BundledPlayhubMaintenance.PendingName),"newer-generation");return Task.FromResult<string?>(Hash(current));},()=>BundledProcessSafety.Clear);
    Check(!result.Verified&&File.ReadAllText(Path.Combine(raced.Dest,BundledPlayhubMaintenance.PendingName))=="newer-generation","newer pending generation cannot be acknowledged by old proof");
    var payloadRace=Make("payload-race");
    result=await BundledPlayhubMaintenance.RunAsync(payloadRace.Install,payloadRace.Plugins,Ack,t=>{File.WriteAllText(Path.Combine(payloadRace.Dest,"main.py"),"external change");return Task.FromResult<string?>(Hash(current));},()=>BundledProcessSafety.Clear);
    Check(!result.Verified,"matching JS cannot hide changed backend payload");
    var gameRace=Make("game-race");bool game=false;
    result=await BundledPlayhubMaintenance.RunAsync(gameRace.Install,gameRace.Plugins,Ack,t=>{game=true;return Task.FromResult<string?>(Hash(current));},()=>game?BundledProcessSafety.Active:BundledProcessSafety.Clear);
    Check(!result.Verified,"game starts during proof: no ready receipt");
    var foreign=Make("foreign");File.WriteAllText(Path.Combine(foreign.Dest,"plugin.json"),"{\"name\":\"Other\"}");
    result=await BundledPlayhubMaintenance.RunAsync(foreign.Install,foreign.Plugins,Ack,t=>throw new Exception("must not read foreign"),()=>BundledProcessSafety.Clear);
    Check(!result.Verified&&result.Status=="identity-preserved"&&File.ReadAllText(Path.Combine(foreign.Dest,"dist","index.js"))==old,"other plugin identity is preserved");
    var missing=Make("missing");Directory.Move(missing.Dest,missing.Dest+"-away");
    result=await BundledPlayhubMaintenance.RunAsync(missing.Install,missing.Plugins,Ack,t=>throw new Exception("must not read absent"),()=>BundledProcessSafety.Clear);
    Check(result.Status=="not-installed"&&!Directory.Exists(missing.Dest),"optional absent plugin is not installed");
    var unreachable=Make("unreachable");
    result=await BundledPlayhubMaintenance.RunAsync(unreachable.Install,unreachable.Plugins,Ack,t=>throw new IOException("unreachable"),()=>BundledProcessSafety.Clear);
    Check(!result.Verified&&File.Exists(Path.Combine(unreachable.Dest,BundledPlayhubMaintenance.PendingName)),"CDP unavailable is pending");
    Console.WriteLine($"{count} actual contract checks PASS");
} finally { Directory.Delete(root,true); }
record Fixture(string Install,string Plugins,string Dest,string SourceJs);
