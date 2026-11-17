using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

static string Library(int padding=42000) => JsonSerializer.Serialize(Enumerable.Range(0,212).Select(index => new {
    app_name="synthetic-"+index, app_title="Synthetic metadata "+index,
    asset_infos=new {Windows=new{}},metadata=new{description=new string('x',padding),keyImages=Array.Empty<object>()}}));
if(args.Length!=0)
{
    Console.OutputEncoding=new UTF8Encoding(false);
    var mode=Environment.GetEnvironmentVariable("PLAYHUB_QUERY_TEST_MODE");
    if(mode=="stderr") {Console.Error.Write(new string('e',2*1024*1024+1));return;}
    if(mode=="utf8") {Console.Write(new string((char)0x20AC,3*1024*1024));return;}
    if(mode=="large") {Console.Write(Library(330000));return;}
    if(mode=="small") {Console.Error.Write("synthetic benign warning");Console.Write("[]");return;}
    Console.Write(Library());return;
}
try {await RunChecks();}
catch(Exception error)
{
    Console.WriteLine("FAIL "+(error is StoreOperationException refusal?refusal.Code:error.GetType().Name));
    Environment.ExitCode=1;
}
static async Task RunChecks()
{
var executable=Environment.ProcessPath??throw new Exception("test_apphost_missing");
if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))throw new Exception("run_with_apphost");
var library=new[]{"list-games","--json","--include-non-installable","--platform","Windows"};
Dictionary<string,string> Env(string mode)=>new(){{"PLAYHUB_QUERY_TEST_MODE",mode}};
int checks=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
async Task Refuses(IReadOnlyList<string> arguments,string mode,string code)
{
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var rejected=false;
    try {await StoreToolProcess.QueryAsync(executable,arguments,Env(mode),timeout.Token);}
    catch(StoreOperationException error){Check(error.Code==code,"specific bounded response refusal");rejected=true;}
    Check(rejected,"response must not be accepted or truncated");
}
var expected=Library();
Check(Encoding.UTF8.GetByteCount(expected)>8*1024*1024,"actual serialized fixture exceeds old 8 MiB cap");
using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
{
    var result=await StoreToolProcess.QueryAsync(executable,library,Env("normal"),timeout.Token);
    Check(result.Code==0,"successful owned metadata query");
    Check(result.Json==expected,"full JSON output byte exact without partial truncation");
    using var document=JsonDocument.Parse(result.Json);
    Check(document.RootElement.GetArrayLength()==212,"all owned game rows survive");
    Check(document.RootElement.EnumerateArray().All(game=>game.GetProperty("asset_infos").TryGetProperty("Windows",out _)),"real platform metadata retained");
}
await Refuses(new[]{"info","synthetic","--json"},"normal","tool_response_too_large");
await Refuses(new[]{"list-games","--json"},"normal","tool_response_too_large");
await Refuses(new[]{"info","synthetic","--json"},"utf8","tool_response_too_large");
await Refuses(library,"large","tool_response_too_large");
await Refuses(library,"stderr","tool_error_response_too_large");
using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
{
    var result=await StoreToolProcess.QueryAsync(executable,new[]{"info","synthetic","--json"},Env("small"),timeout.Token);
    Check(result.Code==0&&result.Json=="[]","ordinary query works; stderr is never mixed into JSON");
}
Console.WriteLine("PASS "+checks+" inert CLI query assertions; no provider login, network, game, or installation executed.");
}
