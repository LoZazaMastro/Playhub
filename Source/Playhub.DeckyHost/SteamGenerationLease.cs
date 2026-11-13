using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Playhub.DeckyHost;

// One retained process handle makes PID reuse and a temporarily absent registry
// entry distinct from a confirmed exit. No Steam or debugger command is issued.
internal interface ISteamGenerationLease : IDisposable
{
    SteamGenerationIdentity Identity { get; }
    bool Exited { get; }
    bool Alive { get; }
}

internal sealed class SteamGenerationLease : ISteamGenerationLease
{
    private readonly SafeProcessHandle _process;
    public SteamGenerationIdentity Identity { get; }
    private SteamGenerationLease(SafeProcessHandle process, DeckyProcessTree.Identity identity)
    { _process = process; Identity = new(identity.Pid, identity.Birth, identity.Path, identity.Sid, identity.Session); }
    public bool Exited => !_process.IsClosed && WaitForSingleObject(_process, 0) == 0;
    public bool Alive => !_process.IsClosed && WaitForSingleObject(_process, 0) == 258;

    // Process image, owner, birth and session cannot change on a retained live
    // handle. Recheck only registration plus that handle on the idle fast path.
    // PID reuse, changed registration and exit all require the full reader.
    internal bool MatchesRegistration(string sid, uint session)
    {
        try
        {
            var registration = ReadRegistration();
            return registration is { } value && SteamGenerationPolicy.CanReuse(Identity, value.Pid, value.Path, sid, session, Alive);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException or ObjectDisposedException) { return false; }
    }

    internal static SteamGenerationLease? Read(string sid, uint session)
    {
        SafeProcessHandle? handle = null;
        try
        {
            if (ReadRegistration() is not { } registration) return null;
            var (pid, path) = registration;
            handle = OpenProcess(0x101000, false, pid); // query limited information and synchronize
            var identity = DeckyProcessTree.Read(handle, pid);
            if (!SteamGenerationPolicy.Accept(identity, path, sid, session)) return null;
            var lease = new SteamGenerationLease(handle, identity!); handle = null;
            return lease;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException or ObjectDisposedException) { return null; }
        finally { handle?.Dispose(); }
    }

    private static (int Pid, string Path)? ReadRegistration()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var active = steam?.OpenSubKey("ActiveProcess");
        if (active?.GetValue("pid") is not int pid || pid <= 0) return null;
        string? path = steam?.GetValue("SteamExe") as string;
        if (string.IsNullOrWhiteSpace(path) && steam?.GetValue("SteamPath") is string directory)
            path = Path.Combine(directory, "steam.exe");
        if (path is null || path.Length > 32768 || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\")
            || !Path.GetFileName(path).Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return (pid, Path.GetFullPath(path));
    }

    public void Dispose() => _process.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
