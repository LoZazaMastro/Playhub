using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PlayhubSetup;

internal static class ControllerDriverPayloadTrust
{
    internal static bool Verify(string path)
    {
        var info = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), FilePath = path };
        nint file = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            File = file, StateAction = 1, Flags = 0x1000 };
        Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(info, file, false);
            // Verify the timestamped signature with the Windows offline cache.
            // The exact release digest is independently required by the caller.
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return false;
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return signer.GetNameInfo(X509NameType.SimpleName, false) == "Nefarius Software Solutions e.U.";
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfoNative>(file);
            Marshal.FreeHGlobal(file);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative { internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string FilePath; internal nint File, Subject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size;
        internal nint Callback, Client;
        internal uint UiChoice, Revocation, UnionChoice;
        internal nint File;
        internal uint StateAction;
        internal nint State, Url;
        internal uint Flags, Context;
        internal nint SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
