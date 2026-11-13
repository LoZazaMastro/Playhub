// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
namespace Playhub.FirstContent;
internal enum CoverRetireReason {FirstObservedContent,SourceLost,CaptureUnavailable,Canceled}
// Implemented only by the authenticated native display owner. Heartbeat/retire
// publish to its existing GUI worker; they do not activate any window here.
internal interface IFirstContentCoverPublisher
{
 bool Current{get;}
 bool PublishHeartbeat();
 void RetireOwned(CoverRetireReason reason);
}
internal static class FirstContentProducer
{
 internal static async Task<CoverRetireReason> RunAsync(IFirstContentCoverPublisher cover,OwnedWindowContentClient observer,
    CancellationToken cancellation,Func<CancellationToken,Task>? pause=null)
 {
  pause??=token=>Task.Delay(25,token);using var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
  Task<ContentFacts?>? query=null;var reason=CoverRetireReason.SourceLost;
  try{
   while(true){
    cancellation.ThrowIfCancellationRequested();
    // No capture response authorizes a heartbeat. Only the retained lease does.
    if(!cover.Current||!cover.PublishHeartbeat()||!cover.Current){reason=CoverRetireReason.SourceLost;break;}
    query??=observer.QueryAsync(stop.Token);
    if(query.IsCompleted){
     var facts=await query.ConfigureAwait(false);query=null;
     if(!cover.Current){reason=CoverRetireReason.SourceLost;break;}
     if(facts?.ObservedNonBlackContent==true&&cover.Current&&observer.FreshForRetirement(facts)){reason=CoverRetireReason.FirstObservedContent;break;}
     if(observer.LastState is not(ContentObservationState.Pending or ContentObservationState.Facts)){
      reason=observer.LastState==ContentObservationState.SourceLost?CoverRetireReason.SourceLost:CoverRetireReason.CaptureUnavailable;break;
     }
    }
    await pause(stop.Token).ConfigureAwait(false);
   }
  }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){reason=CoverRetireReason.Canceled;}
   catch(Exception error)when(error is IOException or InvalidOperationException or System.Text.Json.JsonException){reason=CoverRetireReason.CaptureUnavailable;}
  finally{
   stop.Cancel();try{cover.RetireOwned(reason);}finally{
    // Query has an independent bounded wait; disposing owns only this child.
    try{if(query is not null)try{await query.ConfigureAwait(false);}catch(OperationCanceledException){}catch(IOException){}}
    finally{await observer.DisposeAsync().ConfigureAwait(false);}
   }
  }
  return reason;
 }
}
