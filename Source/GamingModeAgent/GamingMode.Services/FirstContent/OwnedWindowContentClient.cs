// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Playhub.FirstContent;
internal interface IContentTransport: IAsyncDisposable
{
 Task<string?> ReadLineAsync(CancellationToken cancellation);
 Task WriteLineAsync(string line,CancellationToken cancellation);
}
internal sealed record ContentObservationDiagnostic(string State,string Stage,string Rejection,int? HResult,uint? WinError,long? ReceiverFrameAgeMs);
internal sealed record ContentRetirementDiagnostic(ContentFacts Facts,string Check,long? FrameAgeMs,long? PostReadElapsedMs);
internal enum ContentObservationState {Pending,Facts,SourceLost,Unavailable,IdentityChanged,Invalid}
// Owns only its child and private redirected pipes. No public server/name/port,
// external token, target process write, desktop capture or alternate executable.
internal sealed class OwnedWindowContentClient:IAsyncDisposable
{
 internal const string WorkerSha256="1F9728D6C2EA3AF8E46095484F7E0723C6038C7BE981777CAF99EFDF21316105";
 readonly IContentTransport transport;readonly ContentTarget target;readonly Func<bool> current;readonly Func<long> qpc;
 bool ready,retired,disposed;readonly SemaphoreSlim single=new(1,1);
 internal ContentObservationState LastState{get;private set;}=ContentObservationState.Pending;
 internal ContentObservationDiagnostic? LastDiagnostic{get;private set;}
 internal ContentFacts? LastFacts{get;private set;}
 internal ContentRetirementDiagnostic? LastRetirement{get;private set;}
 static readonly string[] States=["frame-facts","no-fresh-frame","identity-changed","unsupported","unavailable","access-denied","canceled","unknown"];
 static readonly string[] Stages=["bind","qpc","native-guard-open","native-guard-current","geometry","capture-item-factory","create-for-window","item-size","frame-pool","create-session","cursor-disable","cursor-readback","native-guard-before-start","start-capture","frame","different-target","geometry-changed","client-crop","surface","format-or-size","readback","final-currentness"];
 static string Label(JsonElement root,string key,string[] allowed)=>root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString() is {} text&&allowed.Contains(text,StringComparer.Ordinal)?text:"none";
 static ContentObservationDiagnostic Diagnostic(JsonElement root,string rejection,long? age=null)=>new(Label(root,"state",States),Label(root,"stage",Stages),rejection,
  root.TryGetProperty("hresult",out var hr)&&hr.ValueKind==JsonValueKind.Number&&hr.TryGetInt32(out int h)?h:null,
  root.TryGetProperty("winError",out var error)&&error.ValueKind==JsonValueKind.Number&&error.TryGetUInt32(out uint e)?e:null,age);
 void Diagnostic(string rejection)=>LastDiagnostic=new("none","none",rejection,null,null,null);
 internal OwnedWindowContentClient(IContentTransport t,ContentTarget source,Func<bool> sourceCurrent,Func<long> clock)
 {if(!ContentFactsParser.ValidTarget(source))throw new ArgumentException("Exact retained source required");transport=t;target=source;current=sourceCurrent;qpc=clock;}
 // Caller is an authenticated retained source adapter, not a UI/RPC handler.
 internal static OwnedWindowContentClient Start(ContentTarget target,Func<bool> sourceCurrent)
 {
  if(!ContentFactsParser.ValidTarget(target)||!sourceCurrent())throw new InvalidOperationException("Source not current");
  string path=Path.Combine(AppContext.BaseDirectory,"PlayhubWindowContentRead.exe");
  var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  try{
   if(file.Length is <4096 or >1048576||Convert.ToHexString(SHA256.HashData(file))!=WorkerSha256||!sourceCurrent())throw new InvalidDataException("Pinned observer unavailable");
   var start=new ProcessStartInfo(path,"--server"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=Encoding.ASCII,StandardOutputEncoding=Encoding.ASCII};
   var process=Process.Start(start)??throw new IOException("Observer did not start");
   return new(new ProcessTransport(process,file),target,sourceCurrent,Qpc100ns);
  }catch{file.Dispose();throw;}
 }
 static long Qpc100ns(){long value=Stopwatch.GetTimestamp(),frequency=Stopwatch.Frequency;return checked(value/frequency*10000000+(value%frequency)*10000000/frequency);}
 internal async Task<ContentFacts?> QueryAsync(CancellationToken cancellation)
 {
  await single.WaitAsync(cancellation).ConfigureAwait(false);
  try{
   if(retired)return null;if(!current()){LastState=ContentObservationState.SourceLost;Diagnostic("source-not-current");return null;}
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(ready?1000:3000);
   if(!ready){string? hello=await transport.ReadLineAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);if(hello is null){LastState=ContentObservationState.Unavailable;Diagnostic("hello-unavailable");retired=true;return null;}
    using var doc=JsonDocument.Parse(hello);var root=doc.RootElement;
    if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Count()!=2||!root.TryGetProperty("ready",out var value)||value.ValueKind!=JsonValueKind.True
      ||!root.TryGetProperty("protocol",out var protocol)||protocol.ValueKind!=JsonValueKind.Number||!protocol.TryGetInt32(out int p)||p!=1){LastState=ContentObservationState.Unavailable;Diagnostic("hello-contract");retired=true;return null;}
    ready=true;}
   if(!current()||cancellation.IsCancellationRequested){LastState=ContentObservationState.SourceLost;Diagnostic("source-not-current");return null;}
   string id=Guid.NewGuid().ToString("N");string encoded=string.Join('\t',id,target.Window.ToString(System.Globalization.CultureInfo.InvariantCulture),target.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
     target.Birth.ToString(System.Globalization.CultureInfo.InvariantCulture),Convert.ToBase64String(Encoding.Unicode.GetBytes(target.Image)),Convert.ToBase64String(Encoding.Unicode.GetBytes(target.WindowClass)));
   await transport.WriteLineAsync(encoded,timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
   string? response=await transport.ReadLineAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
   if(!current()||cancellation.IsCancellationRequested){LastState=ContentObservationState.SourceLost;Diagnostic("source-not-current");return null;}
   if(response is null){LastState=ContentObservationState.Unavailable;Diagnostic("response-unavailable");retired=true;return null;}
   using var parsed=JsonDocument.Parse(response);var rootReport=parsed.RootElement;
   if(!ContentFactsParser.Envelope(rootReport,id)){LastState=ContentObservationState.Invalid;Diagnostic("envelope");retired=true;return null;}
   if(!rootReport.TryGetProperty("state",out var state)||state.ValueKind!=JsonValueKind.String){LastState=ContentObservationState.Invalid;LastDiagnostic=Diagnostic(rootReport,"state-contract");retired=true;return null;}
   string? name=state.GetString();if(name!="frame-facts"){
    LastState=name switch {"no-fresh-frame"=>ContentObservationState.Pending,"identity-changed"=>ContentObservationState.IdentityChanged,
      "unsupported" or "unavailable" or "access-denied" or "canceled"=>ContentObservationState.Unavailable,_=>ContentObservationState.Invalid};
    LastDiagnostic=Diagnostic(rootReport,"native-state");retired=LastState!=ContentObservationState.Pending;return null;
   }
   var result=ContentFactsParser.Read(rootReport,id,target,current,qpc,out var diagnostic);
   LastDiagnostic=Diagnostic(rootReport,diagnostic.Rejection,diagnostic.ReceiverFrameAgeMs);
   LastFacts=result; // Already-parsed aggregate facts only; never pixels or a new observation.
   LastState=result is null?(diagnostic.RetryableExpiredFrame?ContentObservationState.Pending:ContentObservationState.Invalid):ContentObservationState.Facts;
   if(result is null&&LastState!=ContentObservationState.Pending)retired=true;
   return !cancellation.IsCancellationRequested?result:null;
  }catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){LastState=ContentObservationState.Unavailable;Diagnostic("transport-timeout");retired=true;return null;}
   catch(JsonException){LastState=ContentObservationState.Invalid;Diagnostic("json");retired=true;return null;}catch(IOException){LastState=ContentObservationState.Unavailable;Diagnostic("transport-io");retired=true;return null;}
  finally{single.Release();}
 }
 internal bool FreshForRetirement(ContentFacts facts){
  if(retired){LastRetirement=new(facts,"observer-retired",null,null);return false;}
  if(!current()){LastRetirement=new(facts,"source-not-current",null,null);return false;}
  long now=qpc();bool fresh=now>=facts.ObservedQpc&&now>=facts.FrameQpc&&now-facts.FrameQpc<=2000000;
  long? age=now>=facts.FrameQpc?(now-facts.FrameQpc)/10000:null;
  long? elapsed=ReferenceEquals(LastFacts,facts)&&age is {} a&&LastDiagnostic?.ReceiverFrameAgeMs is {} readAge&&a>=readAge?a-readAge:null;
  LastRetirement=new(facts,fresh?"fresh":now<facts.ObservedQpc||now<facts.FrameQpc?"future-frame":"expired-frame",age,elapsed);
  return fresh;
 }
 public async ValueTask DisposeAsync(){retired=true;await single.WaitAsync().ConfigureAwait(false);try{if(disposed)return;disposed=true;await transport.DisposeAsync().ConfigureAwait(false);}finally{single.Release();}}
 sealed class ProcessTransport: IContentTransport
 {
  readonly Process process;readonly FileStream pin;readonly Task drain;
  internal ProcessTransport(Process p,FileStream file){process=p;pin=file;drain=Drain();}
  async Task Drain(){try{char[] b=new char[512];while(await process.StandardError.ReadAsync(b).ConfigureAwait(false)>0){}}catch(IOException){}catch(ObjectDisposedException){}}
  public async Task<string?> ReadLineAsync(CancellationToken token){var text=new StringBuilder();char[] c=new char[1];while(text.Length<=32768){int read=await process.StandardOutput.ReadAsync(c,token).ConfigureAwait(false);if(read==0)return text.Length==0?null:throw new IOException("Incomplete observer line");if(c[0]=='\n')return text.ToString().TrimEnd('\r');text.Append(c[0]);}throw new IOException("Observer line cap");}
  public async Task WriteLineAsync(string line,CancellationToken token){if(line.Length>16384||line.Contains('\n')||line.Contains('\r'))throw new IOException("Fixed observer request cap");await process.StandardInput.WriteLineAsync(line.AsMemory(),token).ConfigureAwait(false);await process.StandardInput.FlushAsync(token).ConfigureAwait(false);}
  public async ValueTask DisposeAsync(){try{process.StandardInput.Close();using var stop=new CancellationTokenSource(500);try{await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);}catch(OperationCanceledException){if(!process.HasExited)process.Kill();using var killed=new CancellationTokenSource(1000);await process.WaitForExitAsync(killed.Token).ConfigureAwait(false);}}finally{process.StandardOutput.Dispose();process.StandardError.Dispose();pin.Dispose();process.Dispose();}}
 }
}
