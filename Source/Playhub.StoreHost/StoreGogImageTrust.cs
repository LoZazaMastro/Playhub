// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace Playhub.StoreLibrary;
internal static class StoreGogImageTrust
{
    internal static bool Verify(string path)
    {
        using var image=StoreOwnedInstallFiles.OpenReadFile(path);
        var info=new NativeFile{Size=(uint)Marshal.SizeOf<NativeFile>(),Path=path};
        var file=Marshal.AllocHGlobal(Marshal.SizeOf<NativeFile>());
        var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),Ui=2,Union=1,File=file,Action=1,Flags=0x1000};
        Guid action=new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(info,file,false);
            if(WinVerifyTrust(new nint(-1),ref action,ref data)!=0)return false;
            using var signer=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return Publisher(signer.GetNameInfo(X509NameType.SimpleName,false),signer.Subject);
        }
        catch(CryptographicException){return false;}
        finally{data.Action=2;WinVerifyTrust(new nint(-1),ref action,ref data);Marshal.DestroyStructure<NativeFile>(file);Marshal.FreeHGlobal(file);}
    }
    internal static bool Publisher(string name,string subject)
    {
        // GOG's valid certificates include both "GOG  sp. z o.o" and
        // "GOG Sp. z o.o.". Compare the full legal name, allowing its spacing
        // and punctuation variants; require the organization to agree too.
        static string Canonical(string value)=>new(value.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        string publisher=Canonical(name);
        return new[]{"GOGSPZOO","GOGCOM","GOGLTD"}.Contains(publisher,StringComparer.Ordinal)
            &&subject.Split(',').Any(part=>part.Trim().StartsWith("O=",StringComparison.Ordinal)&&Canonical(part.Trim()[2..])==publisher);
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NativeFile
    {internal uint Size;[MarshalAs(UnmanagedType.LPWStr)]internal string Path;internal nint File,Subject;}
    [StructLayout(LayoutKind.Sequential)]private struct TrustData
    {internal uint Size;internal nint Callback,Client;internal uint Ui,Revocation,Union;internal nint File;internal uint Action;internal nint State,Url;internal uint Flags,Context;internal nint Signature;}
    [DllImport("wintrust.dll",ExactSpelling=true)]private static extern int WinVerifyTrust(nint window,ref Guid action,ref TrustData data);
}
