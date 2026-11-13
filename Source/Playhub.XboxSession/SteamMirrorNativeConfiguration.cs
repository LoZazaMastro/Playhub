// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Playhub.Shared;
internal static class SteamMirrorNativeConfiguration
{
 internal const int LeaseDuration=1500;
 [UnmanagedFunctionPointer(CallingConvention.Winapi,CharSet=CharSet.Unicode)]delegate int Configure(nint config,uint bytes,[MarshalAs(UnmanagedType.LPWStr)]string callback,[MarshalAs(UnmanagedType.LPWStr)]string runtime);
 internal static bool TryConfigure(nint library,uint wrapperPid,long wrapperBirth,ulong gameId,uint gamePid,long gameBirth,long ownerBirth)
 {
  if(!NativeLibrary.TryGetExport(library,"ConfigureMirrorV1",out var entry))return false;
  string callback=Path.Combine(AppContext.BaseDirectory,"PlayhubExactBand.dll");using var pinned=File.Open(callback,FileMode.Open,FileAccess.Read,FileShare.Read);string? digest=typeof(SteamCompanionTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(x=>x.Key=="PlayhubSteamBandCallbackSha256")?.Value;SteamCompanionTrial.VerifyDigest(pinned,digest);
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Playhub","Runtime","OverlayMirror");Directory.CreateDirectory(root);if(File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Mirror runtime directory is not plain.");
  byte[] config=new byte[96];BinaryPrimitives.WriteUInt32LittleEndian(config,1);BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(4),96);BinaryPrimitives.WriteUInt64LittleEndian(config.AsSpan(8),gameId);Encoding.ASCII.GetBytes(SteamMirrorWireProtocol.Session(gameId,wrapperPid,wrapperBirth,gamePid,gameBirth,(uint)Environment.ProcessId,ownerBirth)).CopyTo(config,16);BinaryPrimitives.WriteUInt64LittleEndian(config.AsSpan(88),LeaseDuration);
  nint memory=Marshal.AllocHGlobal(config.Length);try{Marshal.Copy(config,0,memory,config.Length);if(Marshal.GetDelegateForFunctionPointer<Configure>(entry)(memory,(uint)config.Length,callback,root)!=1)throw new InvalidOperationException("Authenticated mirror configuration was refused.");return true;}finally{Marshal.FreeHGlobal(memory);}
 }
}
