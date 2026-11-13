// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed record RegisteredDesktopIdentity(uint Pid, long Birth, uint Session, string Image,
    string Package, string Family, string Aumid, bool NativeX64, bool AppContainer, bool SameUser)
{
    internal bool Matches(XboxCommand command, RegisteredDesktopPlan plan, UwpCompanionTarget handoff,
        uint session, long wrapperBirth, long now) => Pid != 0 && Session == session && NativeX64 && !AppContainer && SameUser
        && handoff.Matches(Pid, Birth, wrapperBirth, now)
        && Package.Equals(plan.PackageFullName, StringComparison.OrdinalIgnoreCase)
        && Family.Equals(command.Aumid.Split('!')[0], StringComparison.OrdinalIgnoreCase)
        && (Aumid.Length == 0 || Aumid.Equals(command.Aumid, StringComparison.Ordinal))
        && Image.Equals(plan.Executable, StringComparison.OrdinalIgnoreCase);
}

// COM/Gaming Services may parent a desktop game. Its registered package,
// activation and retained process are the authority, rather than ancestry.
internal sealed class RegisteredDesktopTarget : IDisposable, IRetainedSteamGameTarget
{
    private readonly SafeProcessHandle _process;
    private readonly RegisteredDesktopIdentity _identity;
    private readonly string _aumid;
    public uint Pid => _identity.Pid;
    public long Creation => _identity.Birth;
    public string Aumid => _aumid;
    public SafeProcessHandle ProcessHandle => _process;
    public bool HasExited => WaitForSingleObject(_process, 0) == 0;
    public bool IsCurrent() => !_process.IsClosed && !_process.IsInvalid && WaitForSingleObject(_process, 0) == 258
        && GetProcessId(_process) == Pid && GetProcessTimes(_process, out long birth, out _, out _, out _)
        && birth == Creation && ProcessIdToSessionId(Pid, out uint session) && session == _identity.Session;
    private RegisteredDesktopTarget(SafeProcessHandle process, RegisteredDesktopIdentity identity, string aumid) =>
        (_process, _identity, _aumid) = (process, identity, aumid);

    internal static RegisteredDesktopTarget? Inspect(UwpCompanionTarget handoff, XboxCommand command,
        RegisteredDesktopPreparation plan, uint session, long wrapperBirth)
    {
        var process = OpenProcess(0x00101000, false, handoff.GamePid);
        bool retained = false;
        try
        {
            if (process.IsInvalid || WaitForSingleObject(process, 0) != 258 || GetProcessId(process) != handoff.GamePid
                || !GetProcessTimes(process, out long birth, out _, out _, out _)
                || !ProcessIdToSessionId(handoff.GamePid, out uint actualSession) || actualSession != session
                || !IsWow64Process2(process, out ushort emulated, out ushort native) || emulated != 0 || native != 0x8664) return null;
            uint length = 32768;
            var image = new StringBuilder((int)length);
            if (!QueryFullProcessImageName(process, 0, image, ref length)) return null;
            string path = Path.GetFullPath(image.ToString());
            string physical = Path.Combine(Playhub.GameSession.RegisteredPackageCatalog.PhysicalDirectory(Path.GetDirectoryName(path)!), Path.GetFileName(path));
            if (!OpenProcessToken(process, 0x0008, out var token)) return null;
            using (token)
            using (var user = new WindowsIdentity(token.DangerousGetHandle()))
            using (var own = WindowsIdentity.GetCurrent())
            {
                if (!GetTokenInformation(token, 29, out int container, sizeof(int), out uint returned) || returned != sizeof(int)) return null;
                var identity = new RegisteredDesktopIdentity(handoff.GamePid, birth, actualSession, physical,
                    ReadAppModel(process, GetPackageFullName, false), ReadAppModel(process, GetPackageFamilyName, false),
                    ReadAppModel(process, GetApplicationUserModelId, true), true, container != 0,
                    user.User is not null && own.User is not null && user.User.Equals(own.User));
                if (!identity.Matches(command, plan.Plan, handoff, session, wrapperBirth, DateTime.UtcNow.ToFileTimeUtc())) return null;
                var result = new RegisteredDesktopTarget(process, identity, command.Aumid);
                if (!result.IsCurrent()) return null;
                retained = true;
                return result;
            }
        }
        finally { if (!retained) process.Dispose(); }
    }

    internal delegate int AppModelQuery(SafeProcessHandle process, ref uint length, StringBuilder? value);
    internal static string ReadAppModel(SafeProcessHandle process, AppModelQuery query, bool allowNoApplication)
    {
        uint length = 0;
        int error = query(process, ref length, null);
        if (allowNoApplication && error == 15703) return ""; // APPMODEL_ERROR_NO_APPLICATION only.
        if (error != 122 || length is < 2 or > 8192) throw new InvalidDataException("The registered desktop identity is unavailable.");
        var value = new StringBuilder((int)length);
        if (query(process, ref length, value) != 0 || length is < 2 or > 8192 || length > value.Capacity || value.Length == 0)
            throw new InvalidDataException("The registered desktop identity changed during observation.");
        return value.ToString();
    }
    public void Dispose() => _process.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint timeout);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process, out long birth, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", EntryPoint="QueryFullProcessImageNameW", CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref uint length);
    [DllImport("kernel32.dll")] private static extern bool IsWow64Process2(SafeProcessHandle process,out ushort emulated,out ushort native);
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool OpenProcessToken(SafeProcessHandle process,uint access,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token,int kind,out int value,int size,out uint returned);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern int GetPackageFullName(SafeProcessHandle process,ref uint length,StringBuilder? value);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern int GetPackageFamilyName(SafeProcessHandle process,ref uint length,StringBuilder? value);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern int GetApplicationUserModelId(SafeProcessHandle process,ref uint length,StringBuilder? value);
}
