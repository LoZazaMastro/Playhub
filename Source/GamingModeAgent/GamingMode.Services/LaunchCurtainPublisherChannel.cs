// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.AspNetCore.Http;
using Playhub.DeckyHost;
namespace GamingMode.Services;
// This endpoint trusts the installed LC component in the configured Decky loader.
// It does not claim isolation from other plugins executing inside that same process.
internal sealed record CurtainPublisher(uint Pid,long Birth,string Image,string Sid,uint Session);
internal static class LaunchCurtainPublisherChannel
{
 internal static CurtainPublisher? Read(HttpContext context,string? configuredLoader,string[] fallbackLoaders)
 {
  try{
   var connection=context.Connection;
   if(connection.RemoteIpAddress is not {} remote||connection.LocalIpAddress is not {} local
      ||!IPAddress.IsLoopback(remote)||!IPAddress.IsLoopback(local)||remote.IsIPv4MappedToIPv6||local.IsIPv4MappedToIPv6
      ||remote.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||local.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)return null;
   uint pid=ReadOwner(connection.RemotePort,connection.LocalPort);
   if(pid==0||pid==Environment.ProcessId)return null;
   var identity=DeckyProcessTree.Read(checked((int)pid));using var current=WindowsIdentity.GetCurrent();
   if(identity is null||identity.Sid!=current.User?.Value||identity.Session!=(uint)System.Diagnostics.Process.GetCurrentProcess().SessionId)return null;
   string? expected=new[]{configuredLoader}.Concat(fallbackLoaders).FirstOrDefault(path=>!string.IsNullOrWhiteSpace(path)&&System.IO.File.Exists(path));
   if(expected is null||!DeckyProcessTree.IsLoaderPath(identity.Path)||!System.IO.Path.GetFullPath(expected).Equals(identity.Path,StringComparison.OrdinalIgnoreCase)
     ||(System.IO.File.GetAttributes(identity.Path)&System.IO.FileAttributes.ReparsePoint)!=0)return null;
   // Re-read both the kernel connection owner and process birth after path/SID work.
   if(ReadOwner(connection.RemotePort,connection.LocalPort)!=pid||DeckyProcessTree.Read((int)pid)!=identity)return null;
   return new(pid,identity.Birth,identity.Path,identity.Sid,identity.Session);
  }catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or OverflowException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
 }
 internal static uint ParseOwner(ReadOnlySpan<byte> table,int clientPort,int serverPort)
 {
  if(clientPort is <1 or >65535||serverPort is <1 or >65535||table.Length<4)return 0;
  uint count=BinaryPrimitives.ReadUInt32LittleEndian(table);if(count>65536||table.Length!=4L+24L*count)return 0;
  uint result=0;
  for(int i=0;i<count;i++){
   var row=table.Slice(4+24*i,24);
   // MIB_TCPROW_OWNER_PID: ESTABLISHED, exact 127.0.0.1 client->server tuple.
   if(BinaryPrimitives.ReadUInt32LittleEndian(row)!=5||!row.Slice(4,4).SequenceEqual(new byte[]{127,0,0,1})
      ||!row.Slice(12,4).SequenceEqual(new byte[]{127,0,0,1})||BinaryPrimitives.ReadUInt16BigEndian(row.Slice(8,2))!=clientPort
      ||BinaryPrimitives.ReadUInt16BigEndian(row.Slice(16,2))!=serverPort)continue;
   uint pid=BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(20,4));if(pid==0||result!=0)return 0;result=pid;
  }
  return result;
 }
 static uint ReadOwner(int clientPort,int serverPort)
 {
  uint size=0;uint code=GetExtendedTcpTable(0,ref size,false,2,5,0);
  if(code!=122||size<4||size>1572868)return 0;
  nint buffer=Marshal.AllocHGlobal(checked((int)size));
  try{uint capacity=size;if(GetExtendedTcpTable(buffer,ref size,false,2,5,0)!=0||size>capacity)return 0;
   uint count=unchecked((uint)Marshal.ReadInt32(buffer));long used=4L+24L*count;if(count>65536||used>size)return 0;
   byte[] bytes=new byte[checked((int)used)];Marshal.Copy(buffer,bytes,0,bytes.Length);return ParseOwner(bytes,clientPort,serverPort);
  }finally{Marshal.FreeHGlobal(buffer);}
 }
 [DllImport("iphlpapi.dll",SetLastError=true)]static extern uint GetExtendedTcpTable(nint table,ref uint bytes,[MarshalAs(UnmanagedType.Bool)]bool order,uint family,uint tableClass,uint reserved);
}
