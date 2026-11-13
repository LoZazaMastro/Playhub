using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace GamingMode.Services;

internal static class ValveImageTrust
{
    private sealed record Proof(long Length, long Modified, long CheckedAt);
    private static readonly ConcurrentDictionary<string, Proof> Cache = new(StringComparer.OrdinalIgnoreCase);
    internal static bool Verify(string path)
    {
        try
        {
            // Pin the image during verification; the cache is short-lived and
            // invalidated by an updater changing either file metadata value.
            using var image = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long modified = File.GetLastWriteTimeUtc(path).Ticks;
            long now = Environment.TickCount64;
            if (Cache.TryGetValue(path, out var proof) && proof.Length == image.Length && proof.Modified == modified
                && now - proof.CheckedAt is >= 0 and < 30000) return true;
            if (!CheckSignature(path)) { Cache.TryRemove(path, out _); return false; }
            Cache[path] = new(image.Length, modified, now);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { return false; }
    }

    private static bool CheckSignature(string path)
    {
        var info = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), FilePath = path };
        nint file = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            File = file, StateAction = 1, Flags = 0x1000 };
        Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(info, file, false);
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return false;
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return signer.GetNameInfo(X509NameType.SimpleName, false) == "Valve Corp."
                && signer.Subject.Split(',').Any(part => part.Trim() == "O=Valve Corp.");
        }
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
