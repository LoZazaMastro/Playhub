using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.FirstContent;
internal static class CaptureDiagnosticsChecks {
 internal static async Task<int> Run(JsonElement good,long frame){
  int checks=0;
  void Check(bool value,string label){if(!value)throw new Exception("capture diagnostic "+label);checks++;}
  var target=new ContentTarget(7341982,40576,134390248656109619,69808,@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","SDL_app");
  async Task Case(string label,Action<JsonObject> change,ContentObservationState state,string rejection,string observerState,string stage,int expectedCurrent,int expectedClock,bool aged=false){
   int reads=0,clocks=0;long now=frame+300000;
   await using var client=new OwnedWindowContentClient(new Pipe(good,change),target,()=>{reads++;if(aged&&reads==5)now=frame+2000001;return true;},()=>{clocks++;return now;});
   await client.QueryAsync(default);
   Check(client.LastState==state,label+" original state");
   Check(reads==expectedCurrent&&clocks==expectedClock,label+" exact getter/QPC count");
   object? diagnostic=typeof(OwnedWindowContentClient).GetProperty("LastDiagnostic",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(client);
   Check(diagnostic is not null,label+" diagnostic available");
   var report=JsonSerializer.SerializeToElement(diagnostic);
   Check(report.GetProperty("Rejection").GetString()==rejection&&report.GetProperty("State").GetString()==observerState&&report.GetProperty("Stage").GetString()==stage,label+" exact bounded labels");
   if(aged)Check(report.GetProperty("ReceiverFrameAgeMs").GetInt64()==200,label+" existing QPC age only");
  }
  await Case("valid",_=>{},ContentObservationState.Facts,"none","frame-facts","none",5,1);
  await Case("geometry",o=>{o["state"]="unknown";o["stage"]="geometry";},ContentObservationState.Invalid,"native-state","unknown","geometry",3,0);
  await Case("crop",o=>{o["state"]="unknown";o["stage"]="client-crop";},ContentObservationState.Invalid,"native-state","unknown","client-crop",3,0);
  await Case("identity",o=>{o["state"]="identity-changed";o["stage"]="geometry-changed";},ContentObservationState.IdentityChanged,"native-state","identity-changed","geometry-changed",3,0);
  await Case("unsupported",o=>{o["state"]="unsupported";o["stage"]="format-or-size";},ContentObservationState.Unavailable,"native-state","unsupported","format-or-size",3,0);
  await Case("foreground",o=>o["actualForeground"]=false,ContentObservationState.Invalid,"foreground-flag","frame-facts","none",4,0);
  await Case("target",o=>o["sourceHwnd"]="1",ContentObservationState.Invalid,"target-identity","frame-facts","none",4,0);
  await Case("scalar",o=>o["clientWidth"]=4000,ContentObservationState.Invalid,"geometry-or-statistics","frame-facts","none",4,0);
  await Case("age",_=>{},ContentObservationState.Pending,"receiver-frame-time","frame-facts","none",5,1,true);
  await Case("pending",o=>o["state"]="no-fresh-frame",ContentObservationState.Pending,"native-state","no-fresh-frame","none",3,0);
  int calls=0;await using(var client=new OwnedWindowContentClient(new Pipe(good,_=>{}),target,()=>++calls<5,()=>throw new Exception("must not read clock"))){
   await client.QueryAsync(default);Check(client.LastState==ContentObservationState.Invalid&&calls==5,"final current failure has unchanged semantics");
   var d=JsonSerializer.SerializeToElement(typeof(OwnedWindowContentClient).GetProperty("LastDiagnostic",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(client));
   Check(d.GetProperty("Rejection").GetString()=="source-after-frame","final current precise rejection");
  }
  await using(var client=new OwnedWindowContentClient(new Pipe(good,o=>{o["state"]="unknown";o["stage"]="C:/private";o["hresult"]=true;o["winError"]=1L<<40;}),target,()=>true,()=>frame+300000)){
   await client.QueryAsync(default);var d=JsonSerializer.SerializeToElement(typeof(OwnedWindowContentClient).GetProperty("LastDiagnostic",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(client));
   Check(d.GetProperty("Stage").GetString()=="none"&&d.GetProperty("HResult").ValueKind==JsonValueKind.Null&&d.GetProperty("WinError").ValueKind==JsonValueKind.Null,"private labels and unbounded numbers refused");
  }
  await using(var client=new OwnedWindowContentClient(new Pipe(good,o=>{o["state"]="unknown";o["stage"]="client-crop";o["hresult"]=-2147024809;o["winError"]=5;}),target,()=>true,()=>frame+300000)){
   await client.QueryAsync(default);var d=JsonSerializer.SerializeToElement(typeof(OwnedWindowContentClient).GetProperty("LastDiagnostic",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(client));
   Check(d.GetProperty("HResult").GetInt32()==-2147024809&&d.GetProperty("WinError").GetUInt32()==5,"HRESULT and WinError retain scalar meaning");
  }
  return checks;
 }
 sealed class Pipe(JsonElement good,Action<JsonObject> change):IContentTransport {
  bool hello;string? request;
  public Task WriteLineAsync(string line,CancellationToken token){request=line;return Task.CompletedTask;}
  public Task<string?> ReadLineAsync(CancellationToken token){if(!hello){hello=true;return Task.FromResult<string?>("{\"ready\":true,\"protocol\":1}");}
   var o=JsonNode.Parse(good.GetRawText())!.AsObject();o["id"]=request!.Split('\t')[0];change(o);return Task.FromResult<string?>(o.ToJsonString());}
  public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
}