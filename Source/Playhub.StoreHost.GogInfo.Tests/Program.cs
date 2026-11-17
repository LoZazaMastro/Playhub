// SPDX-License-Identifier: MIT. Original inert checks against pinned gogdl 1.4.0 info protocol.
using System.Text.Json;
using Playhub.StoreLibrary;

var count=0;
void Check(bool condition,string name){count++;if(!condition)throw new Exception("FAIL GOG info "+name);}
var root=Path.Combine(Path.GetTempPath(),"Playhub-GogInfo-Inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var bundle=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Playhub.StoreHost/Runtime"));
var state=Path.Combine(root,"state");
var response="";var responseCode=0;var calls=0;
Task<(int,string)> Query(string exe,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> env,CancellationToken token)
{
    calls++;Check(Path.GetFileName(exe)=="gogdl_windows_x86_64.exe","pinned GOG runtime selected");
    Check(args.Count==7&&args[0]=="--auth-config-path"&&args[2]=="info"&&args[3]=="1207658901"&&args.Skip(4).SequenceEqual(new[]{"--platform","windows","--skip-dlcs"}),"exact Windows/base-game info arguments and default English selection");
    Check(File.Exists(args[1])&&env.ContainsKey("GOGDL_CONFIG_PATH"),"only own synthetic credential scope is materialized");
    return Task.FromResult((responseCode,response));
}
var provider=new StoreCliProvider(StoreKind.Gog,bundle,state,Path.Combine(root,"unexecuted-host.exe"),new HttpClient(new NoNetwork()),Query);
Check(provider.Capabilities.CanRefresh,"actual pinned runtime permits metadata query");
StoreCredentialVault.Write(Path.Combine(state,"gog","credentials.dpapi"),"""{"46899977096215655":{"user_id":"inert-only","access_token":"synthetic-only"}}""");
response="""{"size":{"*":{"disk_size":100,"download_size":80},"en-US":{"disk_size":20,"download_size":15},"fr-FR":{"disk_size":1000,"download_size":900}},"dlcs":[{"size":{"*":{"disk_size":9999,"download_size":9999}}}],"buildId":"fixture-build","languages":["en-US","fr-FR"],"folder_name":"Fixture","dependencies":[],"versionEtag":"fixture-etag","versionName":"fixture-version","available_branches":[null],"builds":{}}""";
var size=await provider.GetInstallSizeAsync("1207658901",default);
Check(size.RequiredBytes==120&&size.DownloadBytes==95,"real root.size sums neutral plus en-US only, no DLC or other language");
var french=await ((IStoreInstallLanguageSource)provider).GetInstallOptionsAsync("1207658901","fr-FR",default);
Check(french.Language=="fr-FR"&&french.Languages.SequenceEqual(new[]{"en-US","fr-FR"})&&french.Size.RequiredBytes==1100&&french.Size.DownloadBytes==980,"actual provider offers only manifest languages and includes chosen plus shared depots");
foreach(var item in new[]{("""{"size":{"*":{"disk_size":0,"download_size":0}}}""",0L),
    ("""{"size":{"en-US":{"disk_size":10,"download_size":10}}}""",10L),
    ("""{"size":{"*":{"disk_size":15,"download_size":15}}}""",15L)})
{
    response=item.Item1;size=await provider.GetInstallSizeAsync("1207658901",default);
    Check(size.RequiredBytes==item.Item2&&size.DownloadBytes==item.Item2,"explicit selected/neutral depot value including legitimate zero");
}
var invalid=new[]{
    "{}","""{"1207658901":{"*":{"disk_size":10,"download_size":10}}}""",
    """{"size":[]}""","""{"size":{}}""","""{"size":{"fr-FR":{"disk_size":10,"download_size":10}}}""",
    """{"size":{"*":{"disk_size":-1,"download_size":10}}}""","""{"size":{"*":{"disk_size":10}}}""",
    """{"size":{"*":{"disk_size":"10","download_size":10}}}""","""{"size":{"*":{"disk_size":10.5,"download_size":10}}}""",
    """{"size":{"*":{"disk_size":true,"download_size":10}}}""","""{"size":{"*":{"disk_size":9223372036854775807,"download_size":0},"en-US":{"disk_size":1,"download_size":0}}}""",
    """{"size":{"*":{"disk_size":0,"download_size":0},"*":{"disk_size":1,"download_size":1}}}"""
};
foreach(var json in invalid)
{
    response=json;var rejected=false;
    try{await provider.GetInstallSizeAsync("1207658901",default);}catch(StoreOperationException e){rejected=e.Code=="install_size_invalid";}
    Check(rejected,"invalid or missing selected depot never becomes fabricated zero");
}
responseCode=1;response="{}";
try{await provider.GetInstallSizeAsync("1207658901",default);throw new Exception("accepted failed info");}
catch(StoreOperationException e){Check(e.Code=="install_size_unavailable","failed gogdl command remains unavailable");}
Check(!File.Exists(Path.Combine(state,"gog","native","auth.json")),"synthetic auth scope sealed after successful and refused queries");
Console.WriteLine($"PASS {count} GOG info/provider checks with {calls} inert queries; no network, CLI, account or game execution");

sealed class NoNetwork:HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("No real network in GOG size tests");}
