using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.FirstContent;
static class Program
{
 static async Task Main()
 {
  int count=0;void Check(bool value){if(!value)throw new Exception("content "+(count+1));++count;}
  using var saved=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"observed-frame-facts.json")));
  var good=saved.RootElement.GetProperty("queries")[0].Clone();
  var target=new ContentTarget(7341982,40576,134390248656109619,69808,@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","SDL_app");
  string id=good.GetProperty("id").GetString()!;long frame=long.Parse(good.GetProperty("frameQpc100ns").GetString()!);
  long now=frame+300000;bool live=true;
  ContentFacts? Read(JsonElement value,Func<bool>? current=null)=>ContentFactsParser.Read(value,id,target,current??(()=>live),()=>now);
  Check(Read(good) is {ObservedNonBlackContent:true});
  foreach(var change in new Action<JsonObject>[] {
    o=>o["id"]="foreign",o=>o["sourcePid"]=1,o=>o["sourcePid"]=true,o=>o["sourceHwnd"]="1",o=>o["sourceBirth"]="1",o=>o["sourceThread"]=1,
    o=>o["sourceCurrent"]=false,o=>o["actualForeground"]=false,o=>o["format"]="RGBA8",o=>o["gameReadyClaimed"]=true,o=>o["inputAuthorityClaimed"]=true,
    o=>o["imagesPersisted"]=true,o=>o["hdrMeaningClaimed"]=true,o=>o["frameQpc100ns"]="900719925474099300000",o=>o["observedQpc100ns"]="1",
    o=>o["samples"]=575,o=>o["samples"]=true,o=>o["opaqueSamples"]=577,o=>o["nonBlackSamples"]=-1,o=>o["nonBlackSamples"]=577,
    o=>o["contentWidth"]=8193,o=>o["clientX"]=-1,o=>o["clientWidth"]=4000,o=>o["meanLuma"]=300,o=>o["varianceLuma"]=-1,o=>o["minLuma"]=200,
    o=>o["ageMs"]=201,o=>o["queryMs"]=201,o=>o["state"]="no-fresh-frame"})
  {var o=JsonNode.Parse(good.GetRawText())!.AsObject();change(o);using var doc=JsonDocument.Parse(o.ToJsonString());Check(Read(doc.RootElement)is null);}
  foreach(var mode in new[]{"black","transparent"})
  {var o=JsonNode.Parse(good.GetRawText())!.AsObject();o["nonBlackSamples"]=0;if(mode=="transparent")o["opaqueSamples"]=0;using var doc=JsonDocument.Parse(o.ToJsonString());Check(Read(doc.RootElement)is {ObservedNonBlackContent:false});}
  now=frame+2000001;Check(Read(good)is null);now=frame+300000;
  live=false;Check(Read(good)is null);live=true;int reads=0;
  Check(Read(good,()=>{if(++reads==2)now=frame+2000001;return true;})is null);now=frame+300000;
  using(var duplicate=JsonDocument.Parse(good.GetRawText().Replace("\"state\": \"frame-facts\"","\"state\":\"frame-facts\",\"state\":\"frame-facts\"")))Check(Read(duplicate.RootElement)is null);
  var transport=new Fake(good);await using(var client=new OwnedWindowContentClient(transport,target,()=>live,()=>now))
  {Check((await client.QueryAsync(default))is {ObservedNonBlackContent:true});Check(transport.writes==1&&transport.reads==2);Check(transport.request!.Split('\t').Length==6);}
  Check(transport.disposed);
  var late=new Fake(good){afterRead=()=>live=false};await using(var client=new OwnedWindowContentClient(late,target,()=>live,()=>now))Check(await client.QueryAsync(default)is null);live=true;
  {var f=new Fake(good);var client=new OwnedWindowContentClient(f,target,()=>live,()=>now);var cover=new Cover();
   var reason=await FirstContentProducer.RunAsync(cover,client,default,_=>Task.CompletedTask);
   Check(reason==CoverRetireReason.FirstObservedContent&&cover.retired==reason&&cover.beats==1&&f.disposed);await client.DisposeAsync();Check(f.disposes==1);}
  {var f=new Fake(good){state="no-fresh-frame"};var client=new OwnedWindowContentClient(f,target,()=>live,()=>now);var cover=new Cover();
   var reason=await FirstContentProducer.RunAsync(cover,client,default,_=>{cover.live=false;return Task.CompletedTask;});
   Check(reason==CoverRetireReason.SourceLost&&cover.retired==reason&&f.disposed);}
  {var f=new Fake(good){state="unsupported"};var client=new OwnedWindowContentClient(f,target,()=>live,()=>now);var cover=new Cover();
   var reason=await FirstContentProducer.RunAsync(cover,client,default,_=>Task.CompletedTask);Check(reason==CoverRetireReason.CaptureUnavailable&&f.disposed);}
  {var f=new Fake(good);var client=new OwnedWindowContentClient(f,target,()=>live,()=>now);var cover=new Cover();using var stop=new CancellationTokenSource();stop.Cancel();
   var reason=await FirstContentProducer.RunAsync(cover,client,stop.Token,_=>Task.CompletedTask);Check(reason==CoverRetireReason.Canceled&&f.writes==0&&f.disposed);}
  {var f=new Fake(good);var client=new OwnedWindowContentClient(f,target,()=>live,()=>now);var cover=new Cover();int getters=0;now=frame+300000;
   cover.read=()=>{if(++getters==4)now=frame+2000001;};
   var reason=await FirstContentProducer.RunAsync(cover,client,default,_=>{cover.live=false;return Task.CompletedTask;});
   Check(getters>=4&&reason==CoverRetireReason.SourceLost&&cover.retired!=CoverRetireReason.FirstObservedContent&&f.disposed);now=frame+300000;}
  int lifecycle=await ControllerChecks.Run(good,frame);
  int channel=PublisherChannelChecks.Run();
  Console.WriteLine($"{count} frame/client + {lifecycle} protocol/ABI/controller/HTTP + {channel} kernel-table parser checks PASS; fake private transport/native/source, no observer or native calls");
 }
 sealed class Fake(JsonElement good):IContentTransport
 {
  internal int reads,writes,disposes;internal bool disposed;internal string? request,state;internal Action? afterRead;
  public Task WriteLineAsync(string line,CancellationToken token){request=line;++writes;return Task.CompletedTask;}
  public Task<string?> ReadLineAsync(CancellationToken token){++reads;if(reads==1)return Task.FromResult<string?>("{\"ready\":true,\"protocol\":1}");
   var o=JsonNode.Parse(good.GetRawText())!.AsObject();o["id"]=request!.Split('\t')[0];if(state is not null)o["state"]=state;afterRead?.Invoke();return Task.FromResult<string?>(o.ToJsonString());}
  public ValueTask DisposeAsync(){disposed=true;++disposes;return ValueTask.CompletedTask;}
 }
 sealed class Cover:IFirstContentCoverPublisher {internal bool live=true;internal int beats;internal CoverRetireReason? retired;internal Action? read;
  public bool Current{get{read?.Invoke();return live;}}public bool PublishHeartbeat(){++beats;return live;}public void RetireOwned(CoverRetireReason reason){retired=reason;}}
}
