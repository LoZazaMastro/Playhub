using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class SteamCompanionChecks
{
    internal static int CheckArtifact(string path)
    {
        path = Path.GetFullPath(path);
        using var file = File.OpenRead(path);
        string digest = Convert.ToHexString(SHA256.HashData(file));
        file.Position = 0;
        SteamCompanionTrial.VerifyDigest(file, digest);
        file.Position = 0;
        try
        {
            SteamCompanionTrial.VerifyDigest(file, new string('0', 64));
            throw new Exception("The wrong companion digest was accepted.");
        }
        catch (InvalidDataException) { }

        nint library = NativeLibrary.Load(path, typeof(SteamCompanionTrial).Assembly, DllImportSearchPath.System32);
        try
        {
            var run = Marshal.GetDelegateForFunctionPointer<Probe>(NativeLibrary.GetExport(library, "RunSteamCompanion"));
            if (run(0, 0, 0, 0, 0, 0, 0, 0, 0, null) != 87)
                throw new InvalidOperationException("The companion did not refuse an invalid caller and target.");
        }
        finally { NativeLibrary.Free(library); }
        Console.WriteLine("PASS exact companion hash gate, native DLL/export ABI and foreign-target rejection.");
        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Probe(nint game, nint owner, uint pid, long birth, nint renderer, nint report, nint takeGuideRequest,
        nint steam, nint browser, [MarshalAs(UnmanagedType.LPWStr)] string? expectedAumid);
}
