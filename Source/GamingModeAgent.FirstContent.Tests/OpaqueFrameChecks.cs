using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.FirstContent;
internal static class OpaqueFrameChecks {
 internal static async Task<int> Run(JsonElement good,long frame){
  int checks=0;void Check(bool yes,string label){checks++;if(!yes)throw new Exception("opaque frame "+label);}
  var target=new ContentTarget(7341982,40576,134390248656109619,69808,@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","SDL_app");
  foreach(string kind in new[]{"black","transparent","stale","foreign","source-lost"}){
   var body=JsonNode.Parse(good.GetRawText())!.AsObject();body["nonBlackSamples"]=0;body["minLuma"]=0;body["maxLuma"]=0;body["meanLuma"]=0;body["varianceLuma"]=0;
   if(kind=="transparent")body["opaqueSamples"]=0;if(kind=="foreign")body["actualForeground"]=false;
   var pipe=new Pipe(body);var cover=new Cover();if(kind=="source-lost")cover.Live=false;
   var client=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>frame+(kind=="stale"?2000001:300000));
   var reason=await FirstContentProducer.RunAsync(cover,client,default,_=>{cover.Live=false;return Task.CompletedTask;});
   Check(reason==(kind=="black"?CoverRetireReason.FirstObservedContent:kind=="foreign"?CoverRetireReason.CaptureUnavailable:CoverRetireReason.SourceLost),kind+" exact lifecycle");
   Check(pipe.Disposed&&cover.Reason==reason,kind+" no orphan observer/cover");
   if(kind=="black")Check(pipe.Writes==1&&cover.Beats==1,"opaque black needs a real fresh frame, no invented color/timestamp");
  }
  return checks;
 }
 sealed class Pipe(JsonObject body):IContentTransport {
  internal int Writes;internal bool Disposed;bool hello;string? request;
  public Task WriteLineAsync(string line,CancellationToken token){request=line;Writes++;return Task.CompletedTask;}
  public Task<string?> ReadLineAsync(CancellationToken token){if(!hello){hello=true;return Task.FromResult<string?>("{\"ready\":true,\"protocol\":1}");}var o=(JsonObject)body.DeepClone();o["id"]=request!.Split('\t')[0];return Task.FromResult<string?>(o.ToJsonString());}
  public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
 }
 sealed class Cover:IFirstContentCoverPublisher {
  internal bool Live=true;internal int Beats;internal CoverRetireReason? Reason;
  public bool Current=>Live;public bool PublishHeartbeat(){Beats++;return Live;}public void RetireOwned(CoverRetireReason reason)=>Reason=reason;
 }
}