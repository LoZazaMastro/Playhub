using B=SteamNativeOverlayInputPresentationBridge;
using W=SteamNativeOverlayInputWire;
internal static class SteamNativeOverlayAbortStatusChecks
{
 sealed class Authority(W.Target expected):SteamNativeOverlayInputStatus.IAuthority
 {
  public bool Matches(W.Target t)=>t==expected;
  public bool Matches(B.Command c)=>c.GamePid==expected.GamePid&&c.GameBirth==expected.GameBirth&&c.SessionId==expected.SessionId;
  public bool Current()=>true;
  public void Dispose(){}
 }
 internal static int Run(Action<bool,string> check)
 {
  ulong game=(2979426017UL<<32)|0x02000000;
  var target=new W.Target(game,2979426017,new string('c',64),200,134389623500283156,300,134389623572428537,
   100,134389561211987069,400,134389623572428538,500,134389623572428539,600);
  using var status=new SteamNativeOverlayInputStatus(target,t=>new Authority(t),()=>1000);
  status.MarkNoLayoutWorker();
  var ready=status.Apply(new(1,"prepare",target,7,9858785),default).GetAwaiter().GetResult();
  check(ready.Ok&&ready.OrdinaryReady&&ready.NoOwnedOverride,"actual Status prepare explicit ordinary readiness");
  var bad=status.Apply(new(1,"abort",target,7,9858785,new string('a',32),1),default).GetAwaiter().GetResult();
  check(!bad.Ok&&!bad.Aborted,"actual Status abort rejects foreign token");
  var result=status.Apply(new(1,"abort",target,7,9858785,ready.OwnerToken,ulong.Parse(ready.Generation!)),default).GetAwaiter().GetResult();
  check(result.Ok&&result.Aborted&&result.CleanupComplete&&!result.Closed,"actual Status abort receipt distinct from NativeClosed");
  check(result.ResumeAllowed&&!result.InputReady&&!result.OrdinaryReady,"aborted cannot grant UI while existing ordinary context can resume");
  check(result.OwnerToken==ready.OwnerToken&&result.Generation==ready.Generation,"actual Status abort echoes exact server owner/generation");
  var retry=status.Apply(new(1,"abort",target,7,9858785,ready.OwnerToken,ulong.Parse(ready.Generation!)),default).GetAwaiter().GetResult();
  check(retry.Ok&&retry.Aborted&&retry.CleanupComplete,"actual Status abort retry stays cleanup-only");
  return 6;
 }
}
