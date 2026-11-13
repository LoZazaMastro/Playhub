using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WSGM.PackagedLaunch;

internal static class AppContainerChildProof
{
    private const ulong FixtureGameId = (0xF0010203ul << 32) | 0x02000000;
    private const uint WaitTimeout = 258;
    private enum Scenario { Complete, TargetExit, TransferFailure, PublishFailure }

    internal static int Run(string childPath, string bridgePath)
    {
        string name = "Playhub.AppContainer.Proof." + Guid.NewGuid().ToString("N");
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), name));
        string child = Path.Combine(folder.FullName, "PlayhubAppContainerFixture.exe");
        string bridge = Path.GetFullPath(bridgePath);
        nint sid = 0;
        bool profileCreated = false;
        try
        {
            File.Copy(Path.GetFullPath(childPath), child);
            int result = CreateAppContainerProfile(name, name, "Playhub temporary IPC fixture", 0, 0, out sid);
            if (result != 0 || sid == 0) throw new COMException("The temporary AppContainer profile could not be created.", result);
            profileCreated = true;
            var security = folder.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            folder.SetAccessControl(security);

            using var image = File.OpenRead(bridge);
            var packageSid = new SecurityIdentifier(sid);
            var hash = Convert.ToHexString(SHA256.HashData(image));
            string stagedPath;
            using (var staged = TrialBridgeFile.Open(packageSid, bridge, hash))
            {
                stagedPath = staged.Path;
                foreach (var scenario in Enum.GetValues<Scenario>())
                    RunChild(child, folder.FullName, sid, stagedPath, scenario);
            }
            if (File.Exists(stagedPath) || Directory.Exists(Path.GetDirectoryName(stagedPath)))
                throw new InvalidOperationException("The trial staging file was not cleaned after all fixtures exited.");
        }
        finally
        {
            try
            {
                if (profileCreated) Marshal.ThrowExceptionForHR(DeleteAppContainerProfile(name));
            }
            finally
            {
                if (sid != 0) FreeSid(sid);
                File.Delete(child);
                folder.Delete();
            }
        }
        Console.WriteLine("PASS real AppContainer IPC, target-exit recovery, exact fixture cleanup and no Steam components loaded.");
        return 0;
    }

    private static void RunChild(string child, string folder, nint sid, string bridge, Scenario scenario)
    {
        var created = StartSuspended(child, folder, sid);
        using var process = new SafeProcessHandle(created.Process, true);
        using var thread = new SafeWaitHandle(created.Thread, true);
        try
        {
            if (!RetainedAppContainerTarget.ReadContainerSid(process).Equals(new SecurityIdentifier(sid)))
                throw new InvalidOperationException("The child did not receive the exact sandbox SID.");
            if (scenario == Scenario.Complete)
            {
                using var original = Process.GetProcessById(checked((int)created.ProcessId));
                using var wrapper = Process.GetCurrentProcess();
                long birth = original.StartTime.ToFileTimeUtc();
                var target = new UwpCompanionTarget(created.ProcessId, birth, birth);
                if (!target.Matches(created.ProcessId, birth, wrapper.StartTime.ToFileTimeUtc(), DateTime.UtcNow.ToFileTimeUtc()))
                    throw new InvalidOperationException("The real owned AppContainer handoff lost its exact birth.");
                using var refused = UwpSteamCompanion.Inspect(target, new XboxCommand("Unregistered.Fixture_abc!App", Path.GetFileName(child), ""),
                    new AppContainerLaunchPlan("Unregistered.Fixture_1.0.0.0_x64__abc", child, false), (uint)wrapper.SessionId, wrapper.StartTime.ToFileTimeUtc());
                if (refused is not null || original.HasExited)
                    throw new InvalidOperationException("An unregistered AppContainer impersonated a registered UWP game or was terminated.");
                Console.WriteLine("PASS real owned AppContainer retains exact birth and sandbox but cannot impersonate registered package identity.");
            }
            Prove(process, thread, created.ProcessId, bridge, scenario);
        }
        finally
        {
            // Only the exact fixture process created above is ever terminated on a failed check.
            if (WaitForSingleObject(process, 0) == WaitTimeout) StopOwnedFixture(process, 1);
        }
    }

    private static void Prove(SafeProcessHandle process, SafeWaitHandle thread, uint pid, string bridge, Scenario scenario)
    {
        Require(OpenProcessToken(process, 0x0008, out var token), "The fixture token is unavailable.");
        using (token)
        {
            Require(GetTokenInformation(token, 29, out int appContainer, 4, out uint returned) && returned == 4 && appContainer == 1,
                "Windows did not give the owned fixture an AppContainer token.");
        }
        int processId = checked((int)pid);
        nint retained = process.DangerousGetHandle();
        var guard = new TargetHandleGuard(processId, retained);
        bool Guard(int target, nint handle) => GetProcessId(handle) == pid && guard.Allows(target, handle);
        var localTransportHandles = new List<nint>(3);
        var failure = new InvalidOperationException("Injected handshake failure.");
        nint Duplicate(nint source, uint access)
        {
            Require(Guard(processId, retained), "The owned fixture identity expired.");
            if (scenario == Scenario.TransferFailure && localTransportHandles.Count == 1) throw failure;
            Require(DuplicateHandle(GetCurrentProcess(), source, process, out nint target, access, false, 0),
                "Could not duplicate the fixture transport handle into the AppContainer.");
            if (localTransportHandles.Count < 3)
            {
                localTransportHandles.Add(source);
                if (scenario == Scenario.TargetExit && localTransportHandles.Count == 3) StopOwnedFixture(process, 77);
            }
            return target;
        }

        using var probe = new EventWaitHandle(false, EventResetMode.ManualReset, ProbeName(pid), out bool created);
        if (!created) throw new InvalidOperationException("The fixture probe event already exists.");
        Require(ResumeThread(thread) == 1, "The AppContainer fixture did not resume.");
        void Trace(string message)
        {
            if (scenario == Scenario.PublishFailure && message.StartsWith("Carried 3 of 3 ", StringComparison.Ordinal))
                throw failure;
            Console.WriteLine(message);
        }
        var injector = new GameInjector(new PrivilegeJournal(Console.WriteLine), Guard, Trace);
        using var session = AppContainerBridgeSession.Start(processId, FixtureGameId, bridge, injector, Duplicate);
        if (scenario is Scenario.TransferFailure or Scenario.PublishFailure)
        {
            var transferred = session.TargetHandles;
            bool publishing = scenario == Scenario.PublishFailure;
            var stage = publishing ? AppContainerBridgeStage.Publish : AppContainerBridgeStage.Transfer;
            int expectedHandles = publishing ? 3 : 1;
            if (session.Failure != failure || session.Stage != stage || session.Uncertain != publishing ||
                transferred.Mapping == 0 || (transferred.Request != 0) != publishing || (transferred.Response != 0) != publishing ||
                WaitForSingleObject(process, 0) != WaitTimeout || GameInjector.HasModule(processId, AppContainerProof.BridgeFileName))
                throw new InvalidOperationException($"The {scenario} fixture did not preserve the failed handoff.");
            VerifyTransportOpen(localTransportHandles, expectedHandles);
            StopOwnedFixture(process, 78);
            VerifyTransportClosed(session, localTransportHandles, expectedHandles);
            Console.WriteLine($"PASS {scenario} retains broker transport until exact fixture exit.");
            return;
        }
        if (scenario == Scenario.TargetExit)
        {
            if (session.Stage != AppContainerBridgeStage.Publish || session.Uncertain || probe.WaitOne(0))
                throw new InvalidOperationException($"Exited fixture was not refused during handle publication (stage {session.Stage}).");
            VerifyTransportClosed(session, localTransportHandles);
            Console.WriteLine("PASS exact target exit during handoff prevents native bridge loading and tears down the broker.");
            return;
        }
        if (session.Stage != AppContainerBridgeStage.Ready || session.Uncertain)
            throw new InvalidOperationException($"The AppContainer bridge fixture stopped at {session.Stage} (initialization {session.InitializationResult}).");
        var (map, request, response) = session.TargetHandles;
        Require(injector.SetEnvironment(processId,
            [$"PLAYHUB_FIXTURE_MAPPING={map:X}", $"PLAYHUB_FIXTURE_REQUEST={request:X}", $"PLAYHUB_FIXTURE_RESPONSE={response:X}",
                "PLAYHUB_FIXTURE_GO=1"]), "The owned fixture did not receive its probe signal.");
        if (WaitForSingleObject(process, 10000) != 0) throw new TimeoutException("The AppContainer fixture did not finish its IPC request.");
        Require(GetExitCodeProcess(process, out uint exit), "Could not read the fixture outcome.");
        if (exit != 0) throw new InvalidOperationException($"The AppContainer fixture refused the IPC proof (exit {exit}).");
        if (!probe.WaitOne(0)) throw new InvalidOperationException("The sandbox did not signal the brokered desktop event.");
        VerifyTransportClosed(session, localTransportHandles);
    }

    private static void VerifyTransportOpen(List<nint> handles, int expectedHandles)
    {
        if (handles.Count != expectedHandles || handles.Any(handle => !GetHandleInformation(handle, out _)))
            throw new InvalidOperationException("The broker did not retain its desktop transport through the handshake.");
    }

    private static void VerifyTransportClosed(AppContainerBridgeSession session, List<nint> handles, int expectedHandles = 3)
    {
        VerifyTransportOpen(handles, expectedHandles);
        session.Dispose();
        if (handles.Any(handle => GetHandleInformation(handle, out _)))
            throw new InvalidOperationException("The broker retained a desktop transport handle after teardown.");
    }

    private static void StopOwnedFixture(SafeProcessHandle process, uint exitCode)
    {
        Require(TerminateProcess(process, exitCode), "Could not stop the owned fixture.");
        Require(WaitForSingleObject(process, 5000) == 0, "The owned fixture process did not exit during cleanup.");
    }

    private static string ProbeName(uint pid) => $"GameOverlay_GameExitingEvent_{pid}-IPCWrapper";

    private static ProcessInformation StartSuspended(string executable, string workingDirectory, nint sid)
    {
        // A handle list is unnecessary: the fixture starts with no inherited handles.
        nuint size = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref size);
        if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint list = Marshal.AllocHGlobal(checked((int)size));
        nint capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
        bool initialized = false;
        try
        {
            Require(InitializeProcThreadAttributeList(list, 1, 0, ref size), "Could not initialize AppContainer startup attributes.");
            initialized = true;
            Marshal.StructureToPtr(new SecurityCapabilities { Sid = sid }, capabilities, false);
            Require(UpdateProcThreadAttribute(list, 0, 0x00020009, capabilities, (nuint)Marshal.SizeOf<SecurityCapabilities>(), 0, 0),
                "Could not assign the no-capability AppContainer token.");
            var startup = new StartupInfoEx { Info = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() }, List = list };
            // The PID is not available when building the command line. The child derives the probe name from its own PID.
            var command = new StringBuilder($"\"{executable}\"");
            Require(CreateProcess(executable, command, 0, 0, false, 0x08080004, 0, workingDirectory, ref startup, out var process),
                "Could not start the owned AppContainer fixture.");
            return process;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(capabilities);
            Marshal.FreeHGlobal(list);
        }
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityCapabilities { internal nint Sid, Capabilities; internal uint Count, Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        internal uint Size;
        internal nint Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        internal ushort ShowWindow, ReservedCount;
        internal nint ReservedData, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo Info; internal nint List; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal nint Process, Thread; internal uint ProcessId, ThreadId; }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CreateAppContainerProfile(string name, string display, string description, nint capabilities, uint count, out nint sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int DeleteAppContainerProfile(string name);
    [DllImport("advapi32.dll")] private static extern nint FreeSid(nint sid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint type, nint value, nuint size, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes, nint threadAttributes,
        bool inherit, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool GetHandleInformation(nint handle, out uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(nint process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(nint sourceProcess, nint source, SafeProcessHandle targetProcess, out nint target, uint access, bool inherit, uint flags);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, out int value, uint size, out uint returned);
}
