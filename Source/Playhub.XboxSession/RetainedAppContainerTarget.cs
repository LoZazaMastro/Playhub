// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed record AppContainerProcessIdentity(uint Pid, long Creation, uint Session, string Image,
    string PackageFullName, string Family, string Aumid, bool NativeX64, bool AppContainer)
{
    internal bool Matches(XboxCommand command, AppContainerLaunchPlan plan, uint expectedSession) =>
        Pid > 0 && Creation > 0 && Session == expectedSession && NativeX64 && AppContainer
        && Image.Equals(plan.Executable, StringComparison.OrdinalIgnoreCase)
        && PackageFullName.Equals(plan.PackageFullName, StringComparison.OrdinalIgnoreCase)
        && Family.Equals(command.Aumid.Split('!')[0], StringComparison.OrdinalIgnoreCase)
        && Aumid.Equals(command.Aumid, StringComparison.Ordinal);
}

internal sealed class RetainedAppContainerTarget : IDisposable, IRetainedSteamGameTarget
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x100000;
    private const uint TokenQuery = 0x0008;
    private const uint DuplicateAccess = 0x0040;
    private const int TokenIsAppContainer = 29;
    private const int TokenAppContainerSid = 31;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const uint WaitTimeout = 258;

    private readonly SafeProcessHandle _handle;
    private readonly AppContainerProcessIdentity _identity;
    internal uint Pid => _identity.Pid;
    internal long Creation => _identity.Creation;
    internal string Aumid => _identity.Aumid;
    internal SafeProcessHandle ProcessHandle => _handle;
    internal bool HasExited => WaitForSingleObject(_handle, 0) == 0;
    uint IRetainedSteamGameTarget.Pid => Pid;
    long IRetainedSteamGameTarget.Creation => Creation;
    string IRetainedSteamGameTarget.Aumid => Aumid;
    SafeProcessHandle IRetainedSteamGameTarget.ProcessHandle => ProcessHandle;
    bool IRetainedSteamGameTarget.HasExited => HasExited;
    bool IRetainedSteamGameTarget.IsCurrent() => IsCurrent();

    private RetainedAppContainerTarget(SafeProcessHandle handle, AppContainerProcessIdentity identity)
    {
        _handle = handle;
        _identity = identity;
    }

    internal static RetainedAppContainerTarget Find(XboxCommand command, AppContainerLaunchPlan plan)
    {
        using var current = Process.GetCurrentProcess();
        uint session = (uint)current.SessionId;
        var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(plan.Executable));
        RetainedAppContainerTarget? match = null;
        try
        {
            foreach (var candidate in candidates)
            {
                if (candidate.HasExited || (uint)candidate.SessionId != session) continue;
                var verified = Inspect((uint)candidate.Id, command, plan, session);
                if (verified is null) continue;
                if (match is not null)
                {
                    verified.Dispose();
                    throw new InvalidOperationException("Multiple running applications match the registered AppContainer target.");
                }
                match = verified;
            }
            if (match is null) throw new InvalidOperationException("No exact live AppContainer target was verified. Start the game manually and retry.");
            var result = match;
            match = null;
            return result;
        }
        finally
        {
            match?.Dispose();
            foreach (var candidate in candidates) candidate.Dispose();
        }
    }

    internal static RetainedAppContainerTarget? Inspect(uint pid, XboxCommand command, AppContainerLaunchPlan plan, uint session)
    {
        var handle = OpenProcess(QueryLimitedInformation | Synchronize, false, pid);
        bool retained = false;
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not grant a query-only process handle.");
            if (!TryReadLifetime(handle, out uint actualPid, out long creation) || actualPid != pid) return null;
            if (!ProcessIdToSessionId(actualPid, out uint actualSession) || actualSession != session) return null;
            uint length = 32768;
            var image = new StringBuilder((int)length);
            if (!QueryFullProcessImageName(handle, 0, image, ref length)) return null;
            string imagePath = Path.GetFullPath(image.ToString());
            if (!imagePath.Equals(plan.Executable, StringComparison.OrdinalIgnoreCase)) return null;
            if (!IsWow64Process2(handle, out ushort emulated, out ushort native) || emulated != 0 || native != ImageFileMachineAmd64) return null;
            if (!OpenProcessToken(handle, TokenQuery, out var token)) return null;
            using (token)
            {
                if (!GetTokenInformation(token, TokenIsAppContainer, out int isAppContainer, sizeof(int), out uint returned)
                    || returned != sizeof(int) || isAppContainer != 1) return null;
            }
            var package = ReadAppModelValue(handle, GetPackageFullName);
            var family = ReadAppModelValue(handle, GetPackageFamilyName);
            var aumid = ReadAppModelValue(handle, GetApplicationUserModelId);
            var identity = new AppContainerProcessIdentity(actualPid, creation, actualSession, imagePath, package, family, aumid, true, true);
            if (!identity.Matches(command, plan, session)
                || !TryReadLifetime(handle, out uint confirmedPid, out long confirmedBirth) || confirmedPid != actualPid || confirmedBirth != creation) return null;
            retained = true;
            return new RetainedAppContainerTarget(handle, identity);
        }
        finally { if (!retained) handle.Dispose(); }
    }

    internal bool IsCurrent() => TryReadLifetime(_handle, out uint pid, out long creation)
        && pid == _identity.Pid && creation == _identity.Creation
        && ProcessIdToSessionId(pid, out uint session) && session == _identity.Session;

    internal bool Owns(int pid, nint handle) => pid == _identity.Pid && handle != 0 && IsCurrent()
        && CompareObjectHandles(_handle, handle);

    internal SafeProcessHandle OpenDuplicateHandle()
    {
        var handle = OpenProcess(QueryLimitedInformation | Synchronize | DuplicateAccess, false, Pid);
        if (!handle.IsInvalid && Owns((int)Pid, handle.DangerousGetHandle())) return handle;
        handle.Dispose();
        throw new InvalidOperationException("The retained AppContainer denied exact handle transfer.");
    }

    internal SecurityIdentifier ReadContainerSid()
    {
        if (!IsCurrent()) throw new InvalidOperationException("The retained AppContainer exited.");
        return ReadContainerSid(_handle);
    }

    internal static SecurityIdentifier ReadContainerSid(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token))
            throw new InvalidOperationException("The retained AppContainer token is unavailable.");
        using (token)
        {
            _ = ReadTokenPointer(token, TokenAppContainerSid, 0, 0, out uint size);
            if (size == 0 || size > 1024) throw new Win32Exception(Marshal.GetLastWin32Error());
            nint buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!ReadTokenPointer(token, TokenAppContainerSid, buffer, size, out _) || Marshal.ReadIntPtr(buffer) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    internal void WaitForExit()
    {
        if (WaitForSingleObject(_handle, uint.MaxValue) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The exact game lifetime could not be observed.");
    }

    private static bool TryReadLifetime(SafeProcessHandle handle, out uint pid, out long creation)
    {
        pid = GetProcessId(handle);
        creation = 0;
        return pid != 0 && WaitForSingleObject(handle, 0) == WaitTimeout
            && GetProcessTimes(handle, out creation, out _, out _, out _);
    }

    private delegate int AppModelQuery(SafeProcessHandle handle, ref uint length, StringBuilder text);

    private static string ReadAppModelValue(SafeProcessHandle handle, AppModelQuery query)
    {
        uint length = 512;
        var text = new StringBuilder((int)length);
        return query(handle, ref length, text) == 0 ? text.ToString() : "";
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernelbase.dll")] private static extern bool CompareObjectHandles(SafeProcessHandle process, nint other);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint processId, out uint session);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeout);
    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint length);
    [DllImport("kernel32.dll")]
    private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("advapi32.dll")]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll")]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int value, uint size, out uint returned);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    private static extern bool ReadTokenPointer(SafeAccessTokenHandle token, int informationClass, nint value, uint size, out uint returned);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder name);
}
