// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Playhub.Shared;

// Separate optional ABI. Never parsed as Snapshot224, Prepared or Shown.
internal static class SteamMirrorNativeVisibleWire
{
 internal const int Bytes=240,ControlBytes=264;
 internal static SteamMirrorNativeVisibleDto? Parse(byte[] b,uint ownerPid,long ownerBirth,uint gamePid,long gameBirth,ulong gameId,string expectedSession,long now)
 {
  if(b.Length!=Bytes||ownerPid==0||ownerBirth<=0||gamePid==0||gameBirth<=0||now<0)return null;
  uint U32(int i)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i,4));
  ulong U64(int i)=>BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(i,8));
  string Text(int i)=>U64(i).ToString(CultureInfo.InvariantCulture);
  if(U32(0)!=1||U32(4)!=Bytes||U32(12)!=0||U32(8) is not (1 or 2)||U32(208)>1||U32(212)!=1||U32(216)!=0||U32(220)!=0)return null;
  for(int i=80;i<88;i++)if(b[i]!=0)return null;
  for(int i=16;i<80;i++)if(!((b[i]>='0'&&b[i]<='9')||(b[i]>='a'&&b[i]<='f')))return null;
  string session=Encoding.ASCII.GetString(b.AsSpan(16,64));
  if(session!=expectedSession||U32(160)!=ownerPid||U32(164)!=gamePid||U64(144)!=(ulong)ownerBirth||U64(152)!=(ulong)gameBirth||U64(96)!=gameId)return null;
  var n=new SteamMirrorNativeVisibleDto{Phase=U32(8),SessionId=session,Generation=Text(88),GameId=Text(96),ExpiresAt=Text(104),ObservedAt=Text(112),ReadAt=Text(120),SourceHwnd=Text(128),SourceCookie=Text(136),OwnerPid=ownerPid,OwnerBirth=Text(144),GamePid=gamePid,GameBirth=Text(152),PhysicalObservedAt=Text(168),PhysicalExpiresAt=Text(176),GuideEpoch=Text(184),ReceiptFrame=Text(192),SourceFrame=Text(200),ValveOpen=U32(208)==1,OwnCleanupComplete=true,BaselineSamples=U32(224),BaselineMatched=U32(228),OutputSamples=U32(232),OutputMatched=U32(236)};
  return SteamMirrorWireProtocol.NativeVisibleMatchesOwner(n,expectedSession,ownerPid,ownerBirth,gamePid,gameBirth,gameId,now)?n:null;
 }
 internal static byte[] Control(byte[] expected,uint action,long requestedExpiry=0)
 {
  if(expected.Length!=Bytes||action is not (1 or 2)||requestedExpiry<0||action==2&&requestedExpiry!=0)throw new ArgumentException("Invalid native-visible control");
  byte[] b=new byte[ControlBytes];BinaryPrimitives.WriteUInt32LittleEndian(b,1);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4),ControlBytes);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8),action);expected.CopyTo(b,16);BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(256),checked((ulong)requestedExpiry));return b;
 }
 internal static bool TryRenew(byte[] expected,long now,int duration,out byte[]? control)
 {
  control=null;if(expected.Length!=Bytes||now<0||duration<=0)throw new ArgumentException("Invalid native-visible renewal");
  if(BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(8))!=1)return false;
  long requested=checked(now+duration);if((ulong)requested<=BinaryPrimitives.ReadUInt64LittleEndian(expected.AsSpan(104)))return false;control=Control(expected,1,requested);return true;
 }
}
