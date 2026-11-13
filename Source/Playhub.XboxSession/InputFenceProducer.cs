// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
internal interface IFenceSink { void Write(byte[] frame); }
internal sealed class MappingSink:IFenceSink,IDisposable
{
 private readonly MemoryMappedFile mapping;private readonly MemoryMappedViewAccessor view;private uint sequence;
 // Anonymous mapping. An authenticated installed-companion handoff must
 // duplicate a read-only section handle into the exact target. No public name,
 // RPC boolean, installer, injector or target-memory write is provided here.
 internal MappingSink(){mapping=MemoryMappedFile.CreateNew(null,128,MemoryMappedFileAccess.ReadWrite);view=mapping.CreateViewAccessor(0,128,MemoryMappedFileAccess.ReadWrite);}
 internal Microsoft.Win32.SafeHandles.SafeMemoryMappedFileHandle Handle=>mapping.SafeMemoryMappedFileHandle;
 public void Write(byte[] frame){if(frame.Length!=128)throw new ArgumentException();sequence=checked(sequence+2);
  view.Write(12,sequence-1);Thread.MemoryBarrier();view.WriteArray(0,frame,0,12);view.WriteArray(16,frame,16,112);
  Thread.MemoryBarrier();view.Write(12,sequence);}
 public void Dispose(){view.Dispose();mapping.Dispose();}
}
internal sealed class FenceProducer:IDisposable
{
 internal sealed record Identity(uint GamePid,long GameBirth,uint ProducerPid,long ProducerBirth,uint WindowsSession,ulong GameWindow,string AuthoritySession,ulong GameId);
 readonly Identity target;readonly IFenceSink sink;readonly Func<SteamMirrorInputPresentation?> read;
 readonly Func<bool> current;readonly Func<long> clock;
 readonly Func<SteamNativeOverlayInputPresentationBridge.Snapshot> nativeRead;
 readonly Func<SteamNativeOverlayInputPresentationBridge.Command,bool> nativeCurrent,nativeClosedCurrent;
 readonly Func<SteamMirrorInputPresentation,bool> mirrorSourceCurrent;
 SteamMirrorInputPresentation? admitted;long drainUntil,mirrorReleaseUntil;ulong retired,publicationGeneration=1;
 internal ulong PublicationGeneration=>publicationGeneration;
 internal uint PublicationKind {get;private set;}
 SteamNativeOverlayInputPresentationBridge.Command? nativeOwner;long nativeDrainUntil,nativeReleaseUntil;bool nativeRevoked;
 internal FenceProducer(Identity i,IFenceSink s,Func<bool> c,Func<long> n)
  :this(i,s,c,n,SteamMirrorInputPresentationBridge.Read,sourceCurrent:NativeMirrorSourceCurrent,closedNativeCurrent:SteamNativeOverlayInputPresentationBridge.FenceClosedSourceCurrent){}
 // Inert seam, same whole producer. No HTTP method exposes this constructor.
 internal FenceProducer(Identity i,IFenceSink s,Func<bool> c,Func<long> n,Func<SteamMirrorInputPresentation?> r,
  Func<SteamNativeOverlayInputPresentationBridge.Snapshot>? nr=null,Func<SteamNativeOverlayInputPresentationBridge.Command,bool>? nc=null,Func<SteamMirrorInputPresentation,bool>? sourceCurrent=null,Func<SteamNativeOverlayInputPresentationBridge.Command,bool>? closedNativeCurrent=null)
 {if(i.GamePid==0||i.ProducerPid==0||i.GamePid==i.ProducerPid||i.GameBirth<=0||i.ProducerBirth<=0||i.GameWindow==0
   ||(uint)i.GameId!=0x02000000||i.GameId>>32<0x80000000||i.AuthoritySession.Length!=64||!i.AuthoritySession.All(ch=>ch is >= '0' and <= '9' or >= 'a' and <= 'f'))throw new ArgumentException();
  target=i;sink=s;current=c;clock=n;read=r;nativeRead=nr??SteamNativeOverlayInputPresentationBridge.Read;
  nativeCurrent=nc??SteamNativeOverlayInputPresentationBridge.AuthorityCurrent;mirrorSourceCurrent=sourceCurrent??(_=>false);nativeClosedCurrent=closedNativeCurrent??(_=>false);}
 bool Exact(SteamMirrorInputPresentation m)=>m.GameId==target.GameId&&m.GamePid==target.GamePid&&m.GameBirth==target.GameBirth
  &&m.OwnerPid==target.ProducerPid&&m.OwnerBirth==target.ProducerBirth&&m.Generation>0
  &&m.SessionId.Length==64&&m.SessionId.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 bool Fresh(SteamMirrorInputPresentation m,long now)=>m.ReadAt<=now&&now-m.ReadAt<=250;
 static bool SameClosed(SteamMirrorInputPresentation a,SteamMirrorInputPresentation b)=>a.SessionId==b.SessionId&&a.Generation==b.Generation
  &&a.GameId==b.GameId&&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth&&a.OwnerPid==b.OwnerPid&&a.OwnerBirth==b.OwnerBirth
  &&a.SourceWindow==b.SourceWindow&&a.SourceCookie==b.SourceCookie
  &&(b.DestinationWindow==0||b.DestinationWindow==a.DestinationWindow)
  &&(b.DestinationCookie==0||b.DestinationCookie==a.DestinationCookie);
 internal uint Update()
 {
  var first=read();long now=clock();uint phase=0;
  if(TryNative(first,out uint nativePhase))return nativePhase;
  if(first is null){if(current()){Publish(null,4,now,now+250);return 4;}Publish(admitted,0,now,now);return 0;}
  if(!Exact(first)||!Fresh(first,now)||!current()){Publish(admitted,0,now,now);return 0;}
  var last=read();now=clock();
  if(last is null||!Exact(last)||!Fresh(last,now)||!first.SamePresentation(last)||!current()||!Fresh(last,clock()))
  {Publish(admitted,0,now,now);return 0;}
  now=clock();
  if(last.Shown(now)&&last.Generation>retired)
  {
   if(admitted is null||!admitted.SamePresentation(last)||nativeOwner is not null)
   {admitted=last;drainUntil=mirrorReleaseUntil=0;nativeOwner=null;nativeDrainUntil=nativeReleaseUntil=0;nativeRevoked=false;++publicationGeneration;}
   if(drainUntil==0)phase=1;
  }
  else if(admitted is not null&&last.Generation>retired&&SameClosed(admitted,last)&&last.State==0&&last.CleanupComplete)
  {
   // Distinct closing receipt from the native Mirror snapshot. This is NOT a
   // Valve GameOverlayActivated FALSE and is never published as Shown.
   if(!mirrorSourceCurrent(last)||!current()){Publish(admitted,0,now,now);return 0;}
   var closedAfter=read();now=clock();
   if(closedAfter is null||!Exact(closedAfter)||!SameClosed(admitted,closedAfter)||closedAfter.State!=0||!closedAfter.CleanupComplete||!Fresh(closedAfter,now)||!current()||!mirrorSourceCurrent(closedAfter)||!current()||!Fresh(closedAfter,clock()))
   {Publish(admitted,0,now,now);return 0;}
   last=closedAfter;now=clock();
   if(drainUntil==0){drainUntil=now+250;mirrorReleaseUntil=now+5000;}
   if(now<mirrorReleaseUntil)phase=now<drainUntil?2u:5u;
   else retired=Math.Max(retired,last.Generation);
  }
  else if(admitted is not null)retired=Math.Max(retired,admitted.Generation);
  if(phase==0&&(last.State is 1 or 2||last.State==0&&last.CleanupComplete&&(admitted is null||drainUntil!=0&&now>=drainUntil)))phase=4;
  long expiry=phase==1?Math.Min(last.ExpiresAt,last.ReadAt+1500):phase is 2 or 5?Math.Min(mirrorReleaseUntil,now+250):phase==4?now+250:now;
  // Final context/clock postcheck occurs after both snapshot getters.
  if(!current()||!Fresh(last,clock())||clock()>=expiry)phase=0;
  Publish(admitted,phase,last.ReadAt,phase==0?now:expiry);return phase;
 }
 static bool SameNative(SteamNativeOverlayInputPresentationBridge.Command a,SteamNativeOverlayInputPresentationBridge.Command b)
  =>a.OwnerToken==b.OwnerToken&&a.Generation==b.Generation&&a.SessionId==b.SessionId&&a.FullGameId==b.FullGameId
   &&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth&&a.WrapperPid==b.WrapperPid&&a.WrapperBirth==b.WrapperBirth
   &&a.SteamPid==b.SteamPid&&a.SteamBirth==b.SteamBirth&&a.HostPid==b.HostPid&&a.HostBirth==b.HostBirth
   &&a.BrowserId==b.BrowserId&&a.NativeAppId==b.NativeAppId&&a.ObservedGameId==b.ObservedGameId;
 bool TryNative(SteamMirrorInputPresentation? mirror,out uint phase)
 {
  phase=0;var first=nativeRead();var c=first.Command;
  if(c is null||c.FullGameId!=target.GameId||c.GamePid!=target.GamePid||c.GameBirth!=target.GameBirth)return false;
  long now=clock();
  if(nativeOwner is not null&&SameNative(nativeOwner,c)&&c.Action is "cancel" or "abort" or "revoked")
  {
   // An explicit private cleanup command is a revocation, never a positive
   // closed/neutral receipt. Do not later revive ClosingDrain for this owner.
   Publish(admitted,0,now,now);return true;
  }
  if(c.Action is "active" or "renew"&&first.NativeActiveSeen&&c.ExpiresAt>now&&nativeCurrent(c)&&current())
  {
   var final=nativeRead();long at=clock();
   if(final.Command!=c||!final.NativeActiveSeen||c.ExpiresAt<=at||!nativeCurrent(c)||!current()||clock()>=c.ExpiresAt)
   {Publish(admitted,0,at,at);return true;}
   if(nativeOwner is null||!SameNative(nativeOwner,c))
   {nativeOwner=c;nativeDrainUntil=nativeReleaseUntil=0;nativeRevoked=false;++publicationGeneration;}
   else nativeOwner=c;
   if(nativeDrainUntil!=0||nativeRevoked){Publish(admitted,0,at,at);return true;}
   // Fresh AuthorityCurrent is the existing full private native-game proof.
   // MenuReady/InputReady/layout ACK alone cannot manufacture an active UI.
   phase=1;PublishNative(phase,at,Math.Min(c.ExpiresAt,at+250));return true;
  }
  if(nativeOwner is not null&&!nativeRevoked&&SameNative(nativeOwner,c)&&c.Action=="closed"&&first.NativeActiveSeen
    &&current()&&mirror?.Shown(now)!=true)
  {
   var final=nativeRead();now=clock();
   if(final.Command!=c||!final.NativeActiveSeen||!current()){Publish(admitted,0,now,now);return true;}
   var closedLast=nativeRead();
   if(closedLast.Command!=c||!closedLast.NativeActiveSeen||!nativeClosedCurrent(c)||!current()){Publish(admitted,0,now,now);return true;}
   now=clock();if(nativeDrainUntil==0){nativeDrainUntil=now+250;nativeReleaseUntil=now+5000;}
   phase=now>=nativeReleaseUntil?4u:now<nativeDrainUntil?2u:5u;
   PublishNative(phase,now,phase==4?now+250:Math.Min(now+250,nativeReleaseUntil));return true;
  }
  return false;
 }
 void PublishNative(uint phase,long at,long expiry)
 {
  // kind2 carries the REAL native owner token. It is not represented as a
  // Mirror presentation/HWND cookie, SC source, physical device or PDO.
  byte[] owner=Guid.ParseExact(nativeOwner!.OwnerToken,"N").ToByteArray();
  Write(phase==4?0u:2u,phase,at,expiry,BinaryPrimitives.ReadUInt64LittleEndian(owner),BinaryPrimitives.ReadUInt64LittleEndian(owner.AsSpan(8)));
 }
 void Publish(SteamMirrorInputPresentation? m,uint phase,long readAt,long expiry)
  =>Write(phase is 1 or 2 or 5?1u:0u,phase,readAt,expiry,m?.SourceCookie??0,m?.DestinationCookie??0);
 void Write(uint kind,uint phase,long readAt,long expiry,ulong sourceMarker,ulong destinationMarker)
 {
  PublicationKind=kind;
  if(phase==0)
  {
   // Explicit negative publication retires THIS UI capability. It is neither
   // Shown nor a fabricated native FALSE. A new actual UI owner/generation
   // must be admitted before another positive capture publication.
   if(admitted is not null)retired=Math.Max(retired,admitted.Generation);
   if(nativeOwner is not null)nativeRevoked=true;
   readAt=clock();expiry=readAt+250;
  }
  byte[] b=new byte[128];void U32(int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o,4),v);
  void U64(int o,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o,8),v);
  U32(0,0x46494850);U32(4,1|kind<<16);U32(8,128);
  Convert.FromHexString(target.AuthoritySession).CopyTo(b,16);U64(48,publicationGeneration);U64(88,target.GameId);
  U64(112,sourceMarker);U64(120,destinationMarker);
  U64(56,(ulong)target.GameBirth);U64(64,(ulong)target.ProducerBirth);U64(72,(ulong)Math.Max(0,readAt));U64(80,(ulong)Math.Max(0,expiry));
  U32(96,target.GamePid);U32(100,target.ProducerPid);U32(104,target.WindowsSession);U32(108,phase);sink.Write(b);
 }
 static bool NativeMirrorSourceCurrent(SteamMirrorInputPresentation m)
 {
  nint h=m.SourceWindow;uint t=SourceThread(h,out uint pid);
  return h!=0&&t!=0&&pid==m.OwnerPid&&pid==(uint)Environment.ProcessId&&SourceAncestor(h,2)==h&&SourceOwner(h,4)==0
   &&unchecked((ulong)SourceCookie(h,-21))==m.SourceCookie&&SourceEnabled(h)
   &&SourceThread(h,out uint after)==t&&after==pid&&unchecked((ulong)SourceCookie(h,-21))==m.SourceCookie;
 }
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]static extern uint SourceThread(nint h,out uint pid);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetAncestor")]static extern nint SourceAncestor(nint h,uint flags);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindow")]static extern nint SourceOwner(nint h,uint command);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint SourceCookie(nint h,int index);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="IsWindowEnabled")]static extern bool SourceEnabled(nint h);
 public void Dispose(){long now=clock();Publish(admitted,0,now,now);}
}
