using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.FirstContent;
internal static class StaleFrameRetryChecks {
 internal static async Task<int> Run(JsonElement good,long origin){
  int checks=0;void Check(bool value,string reason){checks++;if(!value)throw new Exception("stale frame "+reason);}
  var target=new ContentTarget(7341982,40576,134390248656109619,69808,@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","SDL_app");
  (JsonObject Body,long Now) Frame(long offset,long receiverAge,bool content){
   var o=JsonNode.Parse(good.GetRawText())!.AsObject();long frame=origin+offset;
   o["frameQpc100ns"]=frame.ToString();o["observedQpc100ns"]=(frame+200000).ToString();o["ageMs"]=20;o["queryMs"]=20;
   if(!content){o["opaqueSamples"]=0;o["nonBlackSamples"]=0;o["minLuma"]=0;o["maxLuma"]=0;o["meanLuma"]=0;o["varianceLuma"]=0;}
   return(o,frame+receiverAge);
  }
  var black=Frame(0,540000,false);var stale=Frame(1000000,2600000,true);var fresh=Frame(4000000,400000,true);
  // Primary RED before: the actual producer retired CaptureUnavailable after
  // two responses; after it requests a third independently timestamped frame.
  foreach(long lateAge in new[]{2240000L,2600000L}){
   var late=(stale.Body,Now:origin+1000000+lateAge);var pipe=new Pipe([black,late,fresh]);var cover=new Cover();var observer=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>pipe.Now);
   var reason=await FirstContentProducer.RunAsync(cover,observer,default,_=>Task.CompletedTask);
   Check(reason==CoverRetireReason.FirstObservedContent&&pipe.Writes==3,"transparent -> expired sample -> NEW fresh content");
   Check(cover.Retired==reason&&pipe.Disposed&&cover.Beats==3,"same lease heartbeats until actual new frame, owned cleanup");
   Check(pipe.FrameValues.SequenceEqual(new[]{origin,origin+1000000,origin+4000000}),"no relabel/replay of old frame timestamps");
  }
  {var pipe=new Pipe([stale,fresh]);int clocks=0,reads=0;await using var observer=new OwnedWindowContentClient(pipe,target,()=>{reads++;return true;},()=>{clocks++;return pipe.Now;});
   Check(await observer.QueryAsync(default)is null&&observer.LastState==ContentObservationState.Pending,"expired frame remains null/Pending");
   var next=await observer.QueryAsync(default);Check(next is not null&&next.FrameQpc==origin+4000000&&observer.LastState==ContentObservationState.Facts,"only new frame is usable");
   Check(clocks==2&&reads==10,"no extra current or QPC calls per query");
  }
  foreach(string stop in new[]{"lease-expired","request-replaced","game-exit"}){
   var pipe=new Pipe([black,stale,fresh]);var cover=new Cover();int pauses=0;var observer=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>pipe.Now);
   var reason=await FirstContentProducer.RunAsync(cover,observer,default,_=>{if(++pauses==2)cover.Live=false;return Task.CompletedTask;});
   Check(reason==CoverRetireReason.SourceLost&&pipe.Writes==2&&pipe.Disposed,stop+" cannot gain new frame/ready after Pending");
  }
  {var pipe=new Pipe([black,stale,fresh]);var cover=new Cover();int pauses=0;using var stop=new CancellationTokenSource();var observer=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>pipe.Now);
   var reason=await FirstContentProducer.RunAsync(cover,observer,stop.Token,_=>{if(++pauses==2)stop.Cancel();return Task.CompletedTask;});
   Check(reason==CoverRetireReason.Canceled&&pipe.Writes==2&&pipe.Disposed,"explicit cancellation during retry remains terminal");
  }
  foreach((string label,Action<JsonObject> mutate) in new(string,Action<JsonObject>)[]{
   ("future-observed",o=>o["observedQpc100ns"]=(stale.Now+1).ToString()),
   ("frame-after-observed",o=>o["frameQpc100ns"]=(stale.Now+1).ToString()),
   ("native-age-invalid",o=>o["ageMs"]=201),
   ("foreign-PID",o=>o["sourcePid"]=1),
   ("foreground-false",o=>o["actualForeground"]=false),
   ("bad-geometry",o=>o["clientWidth"]=4000),
   ("geometry-unknown",o=>{o["state"]="unknown";o["stage"]="client-crop";}),
   ("identity-changed",o=>o["state"]="identity-changed")}){
   var body=(JsonObject)stale.Body.DeepClone();mutate(body);var pipe=new Pipe([black,(body,stale.Now),fresh]);var cover=new Cover();var observer=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>pipe.Now);
   var reason=await FirstContentProducer.RunAsync(cover,observer,default,_=>Task.CompletedTask);
   Check(reason==CoverRetireReason.CaptureUnavailable&&pipe.Writes==2&&pipe.Disposed,label+" remains terminal, not retryable");
  }
  {var pipe=new Pipe([black,stale,fresh]);var cover=new Cover();pipe.AfterResponse=n=>{if(n==2)cover.Live=false;};var observer=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>pipe.Now);
   var reason=await FirstContentProducer.RunAsync(cover,observer,default,_=>Task.CompletedTask);
   Check(reason==CoverRetireReason.SourceLost&&pipe.Writes==2,"source loss after read cannot become Pending");
  }
  {var boundary=Frame(0,2000000,true);var pipe=new Pipe([boundary]);var cover=new Cover();var observer=new OwnedWindowContentClient(pipe,target,()=>cover.Live,()=>pipe.Now);
   var reason=await FirstContentProducer.RunAsync(cover,observer,default,_=>Task.CompletedTask);Check(reason==CoverRetireReason.FirstObservedContent&&pipe.Writes==1,"existing exact200ms boundary unchanged");
  }
  {var boundary=Frame(0,2000001,true);var pipe=new Pipe([boundary,fresh]);await using var observer=new OwnedWindowContentClient(pipe,target,()=>true,()=>pipe.Now);
   Check(await observer.QueryAsync(default)is null&&observer.LastState==ContentObservationState.Pending,"200ms+100ns cannot authorize handoff");
  }
  return checks;
 }
 sealed class Pipe(IReadOnlyList<(JsonObject Body,long Now)> rows):IContentTransport {
  internal long Now;internal int Writes;internal bool Disposed;internal Action<int>? AfterResponse;internal List<long> FrameValues=new();bool hello;string? request;
  public Task WriteLineAsync(string line,CancellationToken token){request=line;Writes++;return Task.CompletedTask;}
  public Task<string?> ReadLineAsync(CancellationToken token){if(!hello){hello=true;return Task.FromResult<string?>("{\"ready\":true,\"protocol\":1}");}
   if(Writes>rows.Count)throw new IOException("unexpected extra native query");var row=rows[Writes-1];Now=row.Now;var o=(JsonObject)row.Body.DeepClone();o["id"]=request!.Split('\t')[0];
   FrameValues.Add(long.Parse(o["frameQpc100ns"]!.GetValue<string>()));AfterResponse?.Invoke(Writes);return Task.FromResult<string?>(o.ToJsonString());}
  public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
 }
 sealed class Cover:IFirstContentCoverPublisher {
  internal bool Live=true;internal int Beats;internal CoverRetireReason? Retired;
  public bool Current=>Live;public bool PublishHeartbeat(){Beats++;return Live;}public void RetireOwned(CoverRetireReason reason)=>Retired=reason;
 }
}