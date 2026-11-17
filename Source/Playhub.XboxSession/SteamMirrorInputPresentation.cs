// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Playhub.Shared;
internal enum SteamMirrorForegroundScope : uint { None=0, Game=1, OwnedOverlay=3 }
internal enum SteamMirrorOwnedForegroundPhase : uint { None=0, AwaitingNativeClose=1, NativeClosed=2 }
internal sealed record SteamMirrorInputPresentation(string SessionId, ulong Generation, ulong GameId,
    long ExpiresAt, long ObservedAt, long ReadAt, nint SourceWindow, nint DestinationWindow,
    ulong SourceCookie, ulong DestinationCookie, long OwnerBirth, long GameBirth, uint OwnerPid, uint GamePid,
    uint State, bool PreparedCurrent, bool ShownCurrent, bool DestinationShown, bool CompositionAttached,
    bool CleanupComplete, SteamMirrorForegroundScope ForegroundScope = SteamMirrorForegroundScope.Game,
    SteamMirrorOwnedForegroundPhase OwnedForegroundPhase = SteamMirrorOwnedForegroundPhase.None)
{
    internal bool Prepared(long now) => PreparedCurrent && CompositionAttached && now < ExpiresAt
        && now >= ObservedAt && now >= ReadAt && State is 2 or 3
        && (ForegroundScope == SteamMirrorForegroundScope.Game
            || ForegroundScope == SteamMirrorForegroundScope.OwnedOverlay && OwnedForegroundPhase == SteamMirrorOwnedForegroundPhase.AwaitingNativeClose
                && State == 3 && ShownCurrent && DestinationShown);
    internal bool Shown(long now) => Prepared(now) && ShownCurrent && DestinationShown && State == 3;
    internal bool SamePresentation(SteamMirrorInputPresentation other) => SessionId == other.SessionId
        && Generation == other.Generation && GameId == other.GameId && SourceWindow == other.SourceWindow
        && DestinationWindow == other.DestinationWindow && SourceCookie == other.SourceCookie
        && DestinationCookie == other.DestinationCookie && OwnerBirth == other.OwnerBirth && GameBirth == other.GameBirth
        && OwnerPid == other.OwnerPid && GamePid == other.GamePid;
    internal const int Bytes = 224;
    internal static SteamMirrorInputPresentation? Parse(byte[] b, uint ownerPid, long ownerBirth,
        uint gamePid, long gameBirth, ulong gameId)
    {
        if (b.Length != Bytes) return null;
        uint U32(int i) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i,4));
        ulong U64(int i) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(i,8));
        // Births/cookies/game IDs remain integer fields; no JS Number roundtrip.
        if (U32(0)!=1 || U32(4)!=Bytes || U32(8)>4 || U32(12)>6 || b[80]!=0) return null;
        for(int i=16;i<80;i++) if(!((b[i]>='0'&&b[i]<='9')||(b[i]>='a'&&b[i]<='f'))) return null;
        for(int i=192;i<224;i+=4) if(U32(i)>1) return null;
        if(U32(168)!=ownerPid || ownerPid==0 || U32(172)!=gamePid || gamePid==0
            || U64(152)!=(ulong)ownerBirth || ownerBirth<=0 || U64(160)!=(ulong)gameBirth || gameBirth<=0
            || U64(96)!=gameId || (uint)gameId!=0x02000000 || gameId>>32<0x80000000
            || U64(88)==0 || U64(104)>long.MaxValue || U64(112)>long.MaxValue || U64(184)>long.MaxValue
            || U64(120)==0 || U64(120)>long.MaxValue || U64(128)>long.MaxValue
            || U64(136)==0 || U32(220)!=0) return null;
        bool prepared=U32(192)==1,shown=U32(196)==1,lease=U32(200)==1,dest=U32(204)==1,composition=U32(208)==1;
        // DestroyOwned clears HWND while retaining exact session/generation.
        // This is an observable retired state, never a live presentation lease.
        bool inactive=U32(8) is 0 or 4;
        if(U32(8)==0&&U32(212)!=1 || U32(8)==4&&U32(212)!=0
            || (U64(128)==0||U64(144)==0)&&(!inactive||prepared||shown||lease||dest||composition||U32(176)!=0))return null;
        uint scope=U32(176),phase=U32(180);
        if(lease!=prepared || prepared && (U32(8) is not (2 or 3) || !composition || scope is not (1 or 3))
            || shown && (!prepared || !dest || U32(8)!=3) || scope is not (0 or 1 or 3)
            || scope==3 && (!prepared || !shown || !lease || !dest || !composition || U32(8)!=3 || phase!=1)
            || phase>2 || phase==1 && U32(8) is not (0 or 3 or 4)
            || phase==2 && (U32(8) is not (0 or 4) || scope!=0)) return null;
        return new(System.Text.Encoding.ASCII.GetString(b.AsSpan(16,64)),U64(88),gameId,(long)U64(104),(long)U64(112),(long)U64(184),
            (nint)U64(120),(nint)U64(128),U64(136),U64(144),ownerBirth,gameBirth,ownerPid,gamePid,U32(8),prepared,shown,dest,composition,U32(212)==1,(SteamMirrorForegroundScope)scope,(SteamMirrorOwnedForegroundPhase)phase);
    }
}
internal static class SteamMirrorInputPresentationBridge
{
    private static readonly object gate=new();
    private sealed record Context(uint GamePid,long GameBirth,ulong GameId,long OwnerBirth,Func<byte[]?> Read,Action Cleanup,Func<byte[],bool>? Queue,Func<byte[]?>? ContinuationRead,Func<byte[]?>? NativeVisibleRead,Func<byte[],bool>? NativeVisibleQueue);
    private static Context? current;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadNative(nint output,uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueueNative(nint input,uint bytes);
    internal static bool Available { get { lock(gate) return current is not null; } }
    internal static IDisposable? Attach(nint library,uint gamePid,long gameBirth,ulong gameId,long ownerBirth)
    {
        if(!NativeLibrary.TryGetExport(library,"ReadMirrorSnapshotV1",out var address))return null;
        var read=Marshal.GetDelegateForFunctionPointer<ReadNative>(address);
        ReadNative? continuation=NativeLibrary.TryGetExport(library,"ReadMirrorContinuationV1",out var continuationAddress)
            ?Marshal.GetDelegateForFunctionPointer<ReadNative>(continuationAddress):null;
        QueueNative? queue=NativeLibrary.TryGetExport(library,"QueueMirrorControlV1",out var queued)
            ? Marshal.GetDelegateForFunctionPointer<QueueNative>(queued):null;
        ReadNative? nativeVisible=NativeLibrary.TryGetExport(library,"ReadNativeVisibleV1",out var nativeVisibleAddress)
            ?Marshal.GetDelegateForFunctionPointer<ReadNative>(nativeVisibleAddress):null;
        QueueNative? nativeVisibleQueue=NativeLibrary.TryGetExport(library,"QueueNativeVisibleControlV1",out var nativeVisibleQueued)
            ?Marshal.GetDelegateForFunctionPointer<QueueNative>(nativeVisibleQueued):null;
        nint buffer=Marshal.AllocHGlobal(SteamMirrorInputPresentation.Bytes);
        try
        {
            return Register(gamePid,gameBirth,gameId,ownerBirth,()=>
            {
                Marshal.WriteInt32(buffer,0,1);Marshal.WriteInt32(buffer,4,SteamMirrorInputPresentation.Bytes);
                if(read(buffer,SteamMirrorInputPresentation.Bytes)!=1)return null;
                byte[] bytes=new byte[SteamMirrorInputPresentation.Bytes];Marshal.Copy(buffer,bytes,0,bytes.Length);return bytes;
            },()=>Marshal.FreeHGlobal(buffer),queue is null?null:control=>
            {
                nint input=Marshal.AllocHGlobal(control.Length);
                try {Marshal.Copy(control,0,input,control.Length);return queue(input,(uint)control.Length)==1;}
                finally {Marshal.FreeHGlobal(input);}
            },continuation is null?null:()=>
            {
                nint continuationBuffer=Marshal.AllocHGlobal(SteamMirrorContinuationWire.Bytes);
                try {Marshal.WriteInt32(continuationBuffer,0,1);Marshal.WriteInt32(continuationBuffer,4,SteamMirrorContinuationWire.Bytes);
                    if(continuation(continuationBuffer,SteamMirrorContinuationWire.Bytes)!=1)return null;
                    byte[] bytes=new byte[SteamMirrorContinuationWire.Bytes];Marshal.Copy(continuationBuffer,bytes,0,bytes.Length);return bytes;}
                finally {Marshal.FreeHGlobal(continuationBuffer);}
            },nativeVisible is null||nativeVisibleQueue is null?null:()=>
            {
                nint nativeBuffer=Marshal.AllocHGlobal(SteamMirrorNativeVisibleWire.Bytes);
                try {Marshal.WriteInt32(nativeBuffer,0,1);Marshal.WriteInt32(nativeBuffer,4,SteamMirrorNativeVisibleWire.Bytes);
                    if(nativeVisible(nativeBuffer,SteamMirrorNativeVisibleWire.Bytes)!=1)return null;
                    byte[] bytes=new byte[SteamMirrorNativeVisibleWire.Bytes];Marshal.Copy(nativeBuffer,bytes,0,bytes.Length);return bytes;}
                finally {Marshal.FreeHGlobal(nativeBuffer);}
            },nativeVisibleQueue is null?null:control=>
            {
                nint nativeInput=Marshal.AllocHGlobal(control.Length);
                try {Marshal.Copy(control,0,nativeInput,control.Length);return nativeVisibleQueue(nativeInput,(uint)control.Length)==1;}
                finally {Marshal.FreeHGlobal(nativeInput);}
            });
        }
        catch {Marshal.FreeHGlobal(buffer);throw;}
    }
    // Inert test seam; the same lock/postcheck/lifetime path is exercised.
    internal static IDisposable Register(uint gamePid,long gameBirth,ulong gameId,long ownerBirth,Func<byte[]?> read,Action cleanup,Func<byte[],bool>? queue=null,Func<byte[]?>? continuationRead=null,Func<byte[]?>? nativeVisibleRead=null,Func<byte[],bool>? nativeVisibleQueue=null)
    {
        lock(gate)
        {
            if(current is not null)throw new InvalidOperationException("A verified native mirror view is already attached.");
            current=new(gamePid,gameBirth,gameId,ownerBirth,read,cleanup,queue,continuationRead,nativeVisibleRead,nativeVisibleQueue);return new Owner(current);
        }
    }
    internal static SteamMirrorInputPresentation? Read()
    {
        lock(gate)
        {
            var held=current;if(held is null)return null;
            byte[]? bytes=held.Read();
            if(!ReferenceEquals(current,held)||bytes is null)return null;
            return SteamMirrorInputPresentation.Parse(bytes,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId);
        }
    }
    internal static byte[]? ReadWire()
    {
        lock(gate)
        {
            var held=current;if(held is null)return null;
            var bytes=held.Read();
            if(!ReferenceEquals(current,held)||bytes is null
                || SteamMirrorInputPresentation.Parse(bytes,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId) is null)return null;
            return (byte[])bytes.Clone();
        }
    }
    // Passive read has the same retained owner lock and post-getter check as the original snapshot.
    // No control/native-input path reads or accepts continuation authority.
    internal static byte[]? ReadContinuationWire(string expectedSession)
    {
        lock(gate)
        {
            var held=current;if(held?.ContinuationRead is null)return null;
            var bytes=held.ContinuationRead();
            if(!ReferenceEquals(current,held)||bytes is null
                ||SteamMirrorContinuationWire.Parse(bytes,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId,expectedSession,Environment.TickCount64) is null)return null;
            return (byte[])bytes.Clone();
        }
    }
    internal static SteamMirrorContinuationDto? ReadContinuation(string expectedSession)
    {
        lock(gate)
        {
            var held=current;if(held?.ContinuationRead is null)return null;
            var bytes=held.ContinuationRead();if(!ReferenceEquals(current,held)||bytes is null)return null;
            var value=SteamMirrorContinuationWire.Parse(bytes,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId,expectedSession,Environment.TickCount64);
            return ReferenceEquals(current,held)?value:null;
        }
    }
    internal static byte[]? ReadNativeVisibleWire(string expectedSession)
    {
        lock(gate)
        {
            var held=current;if(held?.NativeVisibleRead is null)return null;
            var bytes=held.NativeVisibleRead();if(!ReferenceEquals(current,held)||bytes is null
                ||SteamMirrorNativeVisibleWire.Parse(bytes,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId,expectedSession,Environment.TickCount64) is null)return null;
            return ReferenceEquals(current,held)?(byte[])bytes.Clone():null;
        }
    }
    internal static SteamMirrorNativeVisibleDto? ReadNativeVisible(string expectedSession)
    {
        lock(gate)
        {
            var held=current;if(held?.NativeVisibleRead is null)return null;
            var bytes=held.NativeVisibleRead();if(!ReferenceEquals(current,held)||bytes is null)return null;
            var value=SteamMirrorNativeVisibleWire.Parse(bytes,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId,expectedSession,Environment.TickCount64);
            return ReferenceEquals(current,held)?value:null;
        }
    }
    internal static bool QueueNativeVisibleControl(string expectedSession,byte[] control)
    {
        if(control.Length!=SteamMirrorNativeVisibleWire.ControlBytes||BinaryPrimitives.ReadUInt32LittleEndian(control)!=1
            ||BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(4))!=SteamMirrorNativeVisibleWire.ControlBytes
            ||BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(12))!=0)return false;
        uint action=BinaryPrimitives.ReadUInt32LittleEndian(control.AsSpan(8));ulong expiry=BinaryPrimitives.ReadUInt64LittleEndian(control.AsSpan(256));
        if(action is not (1 or 2)||action==2&&expiry!=0||expiry>long.MaxValue)return false;
        lock(gate)
        {
            var held=current;if(held?.NativeVisibleRead is null||held.NativeVisibleQueue is null)return false;
            long now=Environment.TickCount64;var expected=SteamMirrorNativeVisibleWire.Parse(control.AsSpan(16,240).ToArray(),(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId,expectedSession,now);
            if(!SteamMirrorWireProtocol.PositiveNativeVisible(expected,now)||expected is null)return false;
            if(action==1&&(!SteamMirrorWireProtocol.Number(expected.ExpiresAt,out ulong prior)||expiry<prior||expiry<=(ulong)now))return false;
            var raw=held.NativeVisibleRead();if(!ReferenceEquals(current,held)||raw is null)return false;
            now=Environment.TickCount64;var fresh=SteamMirrorNativeVisibleWire.Parse(raw,(uint)Environment.ProcessId,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId,expectedSession,now);
            if(!SteamMirrorWireProtocol.PositiveNativeVisible(fresh,now)||fresh is null||!SteamMirrorWireProtocol.SameNativeVisible(expected,fresh,now))return false;
            if(action==1&&(!SteamMirrorWireProtocol.Number(fresh.ExpiresAt,out ulong freshExpiry)||expiry<freshExpiry||expiry<=(ulong)now))return false;
            bool accepted=held.NativeVisibleQueue((byte[])control.Clone());return accepted&&ReferenceEquals(current,held);
        }
    }
    internal static bool QueueControl(byte[] control)
    {
        if(control.Length!=248)return false;
        lock(gate)
        {
            var held=current;if(held?.Queue is null)return false;
            bool accepted=held.Queue((byte[])control.Clone());
            return accepted&&ReferenceEquals(current,held);
        }
    }
    private sealed class Owner(Context held):IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock(gate)
            {
                if(disposed)return;disposed=true;
                if(!ReferenceEquals(current,held))return;
                current=null;held.Cleanup();
            }
        }
    }
}

// Separate native ABI. Snapshot224 and Control248 are unchanged.
internal static class SteamMirrorContinuationWire
{
 internal const int Bytes=240;
 internal static SteamMirrorContinuationDto? Parse(byte[] b,uint ownerPid,long ownerBirth,uint gamePid,long gameBirth,ulong gameId,string expectedSession,long now)
 {
  if(b.Length!=Bytes||ownerPid==0||gamePid==0||ownerBirth<=0||gameBirth<=0)return null;
  uint U32(int i)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i,4));
  ulong U64(int i)=>BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(i,8));
  string Text(int i)=>U64(i).ToString(System.Globalization.CultureInfo.InvariantCulture);
  if(U32(0)!=1||U32(4)!=Bytes||U32(12)!=0||U32(200)!=0||U32(204)!=0||U32(208)>1||U32(212)!=0||U64(232)!=0)return null;
  for(int i=80;i<88;i++)if(b[i]!=0)return null;
  for(int i=16;i<80;i++)if(!((b[i]>='0'&&b[i]<='9')||(b[i]>='a'&&b[i]<='f')))return null;
  string session=System.Text.Encoding.ASCII.GetString(b.AsSpan(16,64));
  if(session!=expectedSession||U32(136)!=ownerPid||U32(140)!=gamePid||U64(144)!=(ulong)ownerBirth||U64(152)!=(ulong)gameBirth||U64(160)!=gameId)return null;
  var c=new SteamMirrorContinuationDto{Phase=U32(8),SessionId=session,GuideEpoch=Text(88),ParentGeneration=Text(96),ResumedGeneration=Text(104),GapDeadline=Text(112),ObservedAt=Text(120),ReadAt=Text(128),OwnerPid=ownerPid,GamePid=gamePid,OwnerBirth=Text(144),GameBirth=Text(152),GameId=Text(160),SourceHwnd=Text(168),SourceCookie=Text(176),TicketLow=Text(184),TicketHigh=Text(192),CleanupComplete=U32(208)==1,OriginalLeaseExpiry=Text(216),PreparationExpiresAt=Text(224)};
  return SteamMirrorWireProtocol.Continuation(c,now)?c:null;
 }
}
