// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Playhub.StoreLibrary;

internal static class StoreCredentialVault
{
    internal static void PrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var user = WindowsIdentity.GetCurrent().User ?? throw new StoreOperationException("user_missing");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
    internal static void Write(string path, string value)
    {
        var input = Encoding.UTF8.GetBytes(value); var encrypted = Protect(input, true);
        try { File.WriteAllBytes(path + ".new", encrypted); File.Move(path + ".new", path, true); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(input); }
    }
    internal static string Read(string path)
    {
        var plain = Protect(File.ReadAllBytes(path), false);
        try { return Encoding.UTF8.GetString(plain); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); }
    }
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output;
            var ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new StoreOperationException("credential_protection_failed");
            try { var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob data, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32")] private static extern IntPtr LocalFree(IntPtr value);
}
