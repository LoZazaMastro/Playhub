// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

internal sealed class TrialBridgeFile : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly FileStream _image;
    internal string Path => _image.Name;

    private TrialBridgeFile(DirectoryInfo directory, FileStream image) => (_directory, _image) = (directory, image);

    internal static SecurityIdentifier PackageSid(string family)
    {
        int result = DeriveAppContainerSidFromAppContainerName(family, out nint sid);
        if (result != 0 || sid == 0) throw new InvalidOperationException("The registered package SID could not be derived.");
        try { return new SecurityIdentifier(sid); }
        finally { FreeSid(sid); }
    }

    internal static TrialBridgeFile Open(SecurityIdentifier sid)
    {
        string? expected = typeof(TrialBridgeFile).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "PlayhubAppContainerBridgeSha256")?.Value;
        return Open(sid, System.IO.Path.Combine(AppContext.BaseDirectory, AppContainerProof.BridgeFileName), expected);
    }

    internal static TrialBridgeFile Open(SecurityIdentifier sid, string source, string? expected)
    {
        if (expected?.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidOperationException("This build has no pinned AppContainer bridge digest.");
        using var pinned = File.OpenRead(source);
        Verify(pinned, expected);
        var directory = Directory.CreateTempSubdirectory("Playhub.XboxBridge.");
        string destination = System.IO.Path.Combine(directory.FullName, AppContainerProof.BridgeFileName);
        try
        {
            using (var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                pinned.CopyTo(copy);
            var file = new FileInfo(destination);
            var fileSecurity = file.GetAccessControl();
            fileSecurity.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            file.SetAccessControl(fileSecurity);
            var directorySecurity = directory.GetAccessControl();
            directorySecurity.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.Traverse | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize, AccessControlType.Allow));
            directory.SetAccessControl(directorySecurity);
            var staged = File.OpenRead(destination);
            try
            {
                Verify(staged, expected);
                return new TrialBridgeFile(directory, staged);
            }
            catch { staged.Dispose(); throw; }
        }
        catch
        {
            Clean(directory, destination);
            throw;
        }
    }

    private static void Verify(FileStream image, string expected)
    {
        if (!Convert.ToHexString(SHA256.HashData(image)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The AppContainer bridge does not match the digest pinned by this build.");
        image.Position = 0;
        AppContainerProof.VerifyBridge(image);
        image.Position = 0;
    }

    public void Dispose()
    {
        _image.Dispose();
        Clean(_directory, Path);
    }

    private static void Clean(DirectoryInfo directory, string file)
    {
        try
        {
            File.Delete(file);
            directory.Delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WSGM.PackagedLaunch.PackagedLaunchLog.Error("Could not remove the private Xbox bridge copy: " + error.Message);
        }
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out nint sid);
    [DllImport("advapi32.dll")] private static extern nint FreeSid(nint sid);
}
