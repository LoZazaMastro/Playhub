// Playhub Xbox Session changes, 2026-10-03. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using WSGM.PackagedLaunch;

internal sealed class TrustedSteamFiles : IDisposable
{
    private readonly string _root;
    private readonly List<FileStream> _files = [];
    private TrustedSteamFiles(string root) => _root = root;
    internal bool UnchangedRoot() => Path.GetFullPath(SteamInstallation.Root).Equals(_root, StringComparison.OrdinalIgnoreCase);

    internal static TrustedSteamFiles Open()
    {
        var root = Path.GetFullPath(SteamInstallation.Root);
        if (!Path.IsPathFullyQualified(root)) throw new InvalidOperationException("Steam's installation path is invalid.");
        using var own = Process.GetCurrentProcess();
        bool running = false;
        foreach (var client in Process.GetProcessesByName("steam"))
        {
            using (client)
            {
                try { running |= client.SessionId == own.SessionId && Path.GetFullPath(client.MainModule!.FileName).Equals(Path.Combine(root, "steam.exe"), StringComparison.OrdinalIgnoreCase); }
                catch { }
            }
        }
        if (!running) throw new InvalidOperationException("The running Steam client does not match its registered installation.");
        var trusted = new TrustedSteamFiles(root);
        try
        {
            foreach (var name in new[] { "tier0_s64.dll", "vstdlib_s64.dll", "steamclient64.dll", "GameOverlayRenderer64.dll" })
            {
                var path = Path.Combine(root, name);
                var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                trusted._files.Add(file);
                VerifyX64(file);
                if (!ValveSignature(path)) throw new InvalidOperationException("Steam component has no trusted Valve signature: " + name);
            }
            return trusted;
        }
        catch { trusted.Dispose(); throw; }
    }

    internal static void VerifyX64(Stream file)
    {
        using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, leaveOpen: true);
        file.Position = 0;
        if (reader.ReadUInt16() != 0x5A4D || file.Length < 64) throw new InvalidOperationException("Invalid Steam PE component.");
        file.Position = 60;
        int header = reader.ReadInt32();
        if (header < 64 || header > file.Length - 6) throw new InvalidOperationException("Invalid Steam PE header.");
        file.Position = header;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidOperationException("Steam component is not native x64.");
    }

    internal static bool ValveSignature(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer, StateAction = 1, ProviderFlags = 0x1000 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return false;
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return signer.GetNameInfo(X509NameType.SimpleName, false).Equals("Valve Corp.", StringComparison.Ordinal)
                && signer.Subject.Split(',').Any(part => part.Trim().Equals("O=Valve Corp.", StringComparison.Ordinal));
        }
        catch { return false; }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Dispose() { foreach (var file in _files) file.Dispose(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string Path; internal nint File; internal nint KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size; internal nint Policy; internal nint Sip; internal uint UiChoice; internal uint Revocation;
        internal uint UnionChoice; internal nint File; internal uint StateAction; internal nint State; internal nint Url;
        internal uint ProviderFlags; internal uint UiContext; internal nint SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
