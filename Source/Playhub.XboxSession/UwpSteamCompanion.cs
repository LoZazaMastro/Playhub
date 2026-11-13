// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Playhub.GameSession;
using WSGM.PackagedLaunch;

internal sealed record UwpCompanionPlan(uint WrapperPid, long WrapperBirth, ulong GameId, string Aumid, string Executable)
{
    internal bool Matches(uint wrapper, long birth, ulong gameId) => WrapperPid == wrapper && WrapperBirth == birth
        && wrapper > 0 && birth > 0 && GameId == gameId && (uint)GameId == 0x02000000 && (GameId >> 32) >= 0x80000000
        && UwpCompanionProtocol.IsApplication(Aumid, Executable);
}
internal sealed record UwpCompanionTarget(uint GamePid, long GameBirth, long ActivationFileTime)
{
    internal bool Matches(uint pid, long birth, long wrapperBirth, long now) => GamePid == pid && pid is > 0 and <= int.MaxValue
        && GameBirth == birth && ActivationFileTime >= wrapperBirth && ActivationFileTime <= birth && birth <= now
        && now - ActivationFileTime <= TimeSpan.FromSeconds(15).Ticks;
}

internal static class UwpCompanionProtocol
{
    internal static bool IsApplication(string? aumid, string? executable)
    {
        if (aumid is null || executable is null) return false;
        var parts = aumid.Split('!');
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part)) && aumid.Length <= 512 && !aumid.Contains('\0')
            && !string.IsNullOrWhiteSpace(executable) && executable.Length <= 4096 && !Path.IsPathRooted(executable)
            && !executable.Contains(':') && !executable.Contains('\0') && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && executable.Replace('/', '\\').Split('\\').All(part => part is not (".." or "." or ""));
    }

    internal static async Task<T?> ReadLine<T>(Stream input, bool allowEmpty, bool requireEnd)
    {
        // Read one byte at a time so the first phase cannot consume the second
        // private message. Both phases have a hard byte limit and a deadline.
        byte[] buffer = new byte[64 * 1024], item = new byte[1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await input.ReadAsync(item).ConfigureAwait(false);
            if (read == 0 && length == 0 && allowEmpty) return default;
            if (read == 0) throw new InvalidDataException("The private UWP companion message is incomplete.");
            if (item[0] == '\n')
            {
                if (requireEnd && await input.ReadAsync(item).ConfigureAwait(false) != 0)
                    throw new InvalidDataException("Unexpected data after the UWP companion target.");
                return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length))
                    ?? throw new InvalidDataException("The private UWP companion message is empty.");
            }
            buffer[length++] = item[0];
        }
        throw new InvalidDataException("The private UWP companion message exceeds its size limit.");
    }

    internal static bool ParentArgumentsMatch(string[] arguments, string wrapperImage, UwpCompanionPlan plan) =>
        arguments.Length >= 4 && arguments[0].Equals(wrapperImage, StringComparison.OrdinalIgnoreCase)
        && arguments[1] == "--uwp" && arguments[2] == plan.Aumid
        && arguments[3].Equals(plan.Executable, StringComparison.OrdinalIgnoreCase);
}

internal static class UwpSteamCompanion
{
    internal static int Run()
    {
        try
        {
            if (!typeof(UwpSteamCompanion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Any(value => value.Key == "PlayhubUwpSteamCompanion" && value.Value == "true"))
                throw new InvalidOperationException("The registered UWP companion preview is not enabled in this build.");
            if (!Console.IsInputRedirected || !Console.IsOutputRedirected)
                throw new InvalidOperationException("The registered UWP companion requires its private parent transport.");
            using var self = Process.GetCurrentProcess();
            using var parent = TrialSteamTracking.OpenParent(self);
            ulong gameId = AppContainerTrial.ReadSteamGameId(Environment.GetEnvironmentVariable("SteamGameId"));
            using var input = Console.OpenStandardInput();
            var plan = UwpCompanionProtocol.ReadLine<UwpCompanionPlan>(input, true, false).WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter().GetResult();
            if (plan is null) return 0;
            if (!plan.Matches((uint)parent.Id, parent.StartTime.ToFileTimeUtc(), gameId)
                || !UwpCompanionProtocol.ParentArgumentsMatch(ReadArguments(parent), parent.MainModule!.FileName!, plan))
                throw new InvalidOperationException("The UWP companion plan does not match the exact Steam wrapper launch.");
            var command = new XboxCommand(plan.Aumid, plan.Executable, "");
            var catalog = new RegisteredPackageCatalog(physicalPaths: true);
            var kind = RegisteredXboxApplication.Resolve(command, catalog);
            if (kind == RegisteredXboxApplicationKind.PackagedDesktop)
                return RunDesktop(plan, command, catalog, self, parent, input, gameId);
            if (kind != RegisteredXboxApplicationKind.AppContainer)
                throw new InvalidOperationException("The registered application's trust mode is not supported by this companion.");
            var package = AppContainerProof.Resolve(command, new RegisteredPackageCatalog(physicalPaths: true), verifyImage: false);
            using var manifest = RegisteredUwpManifestProof.Verify(command, package);
            using var native = SteamCompanionTrial.OpenVerifiedLibrary();
            using var steam = TrustedSteamFiles.Open();
            if (!AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
                throw new InvalidOperationException("Steam did not register the exact UWP wrapper session.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Kind = "playhub-uwp-companion-ready", plan.Aumid, package.PackageFullName, package.Executable
            }));
            Console.Out.Flush();
            var handoff = UwpCompanionProtocol.ReadLine<UwpCompanionTarget>(input, true, true).WaitAsync(TimeSpan.FromSeconds(30))
                .GetAwaiter().GetResult();
            if (handoff is null) return 0;
            if (parent.HasExited || !steam.UnchangedRoot() || !plan.Matches((uint)parent.Id, parent.StartTime.ToFileTimeUtc(), gameId)
                || !UwpCompanionProtocol.ParentArgumentsMatch(ReadArguments(parent), parent.MainModule!.FileName!, plan))
                throw new InvalidOperationException("The UWP wrapper changed before activation completed.");
            using var target = Inspect(handoff, command, package, (uint)self.SessionId, plan.WrapperBirth);
            if (target is null || !AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
                throw new InvalidOperationException("The UWP companion target did not match the exact newly activated registered application.");
            if (!target.ReadContainerSid().Equals(TrialBridgeFile.PackageSid(plan.Aumid.Split('!')[0])))
                throw new InvalidOperationException("The UWP target has a different registered package sandbox SID.");
            VerifyWindowsUser(target);
            PackagedLaunchLog.Info($"UWP Steam companion verified aumid={plan.Aumid} package={package.PackageFullName} pid={target.Pid} birth={target.Creation}.");
            using var controllerProbe = SteamControllerSourceProbe.TryStart(steam, gameId, target, parent);
            return SteamCompanionTrial.RunSurface(target.ProcessHandle, parent, steam, gameId, target.Pid, target.Creation, plan.Aumid,
                () => SteamControllerLayoutTrial.TryStart(steam, gameId, target, parent,
                    () => SteamCompanionTrial.GameHasForeground, () => SteamCompanionTrial.OverlayIsOpen));
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Error("Registered UWP Steam companion skipped: " + error.Message);
            return 1;
        }
    }

    private static int RunDesktop(UwpCompanionPlan plan, XboxCommand command, IRegisteredPackageCatalog catalog,
        Process self, Process parent, Stream input, ulong gameId)
    {
        // This route observes the same original COM activation. It never starts
        // GameLaunchHelper or a protected game executable itself.
        using var prepared = RegisteredDesktopPreparation.Prepare(command, catalog);
        using var native = SteamCompanionTrial.OpenVerifiedLibrary();
        using var steam = TrustedSteamFiles.Open();
        if (!AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
            throw new InvalidOperationException("Steam did not register the desktop game's exact wrapper session.");
        Console.WriteLine(JsonSerializer.Serialize(new
            { Kind = "playhub-uwp-companion-ready", plan.Aumid, prepared.PackageFullName, prepared.Executable }));
        Console.Out.Flush();
        var handoff = UwpCompanionProtocol.ReadLine<UwpCompanionTarget>(input, true, true).WaitAsync(TimeSpan.FromSeconds(30))
            .GetAwaiter().GetResult();
        if (handoff is null) return 0;
        if (parent.HasExited || !steam.UnchangedRoot() || !plan.Matches((uint)parent.Id, parent.StartTime.ToFileTimeUtc(), gameId)
            || !UwpCompanionProtocol.ParentArgumentsMatch(ReadArguments(parent), parent.MainModule!.FileName!, plan))
            throw new InvalidOperationException("The registered desktop wrapper changed before activation completed.");
        using var target = RegisteredDesktopTarget.Inspect(handoff, command, prepared, (uint)self.SessionId, plan.WrapperBirth);
        if (target is null || !AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
            throw new InvalidOperationException("The registered desktop target differs from the exact newly activated game.");
        PackagedLaunchLog.Info($"Registered desktop Steam companion verified aumid={plan.Aumid} package={prepared.PackageFullName} pid={target.Pid} birth={target.Creation}.");
        using var controllerProbe = SteamControllerSourceProbe.TryStart(steam, gameId, target, parent);
        return SteamCompanionTrial.RunSurface(target.ProcessHandle, parent, steam, gameId, target.Pid, target.Creation,
            onConfiguredLayout: () => SteamControllerLayoutTrial.TryStart(steam, gameId, target, parent,
                () => SteamCompanionTrial.GameHasForeground, () => SteamCompanionTrial.OverlayIsOpen));
    }

    internal static RetainedAppContainerTarget? Inspect(UwpCompanionTarget handoff, XboxCommand command,
        AppContainerLaunchPlan plan, uint session, long wrapperBirth)
    {
        if (!handoff.Matches(handoff.GamePid, handoff.GameBirth, wrapperBirth, DateTime.UtcNow.ToFileTimeUtc())) return null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var target = RetainedAppContainerTarget.Inspect(handoff.GamePid, command, plan, session);
            if (target is not null)
            {
                if (handoff.Matches(target.Pid, target.Creation, wrapperBirth, DateTime.UtcNow.ToFileTimeUtc())) return target;
                target.Dispose();
                return null;
            }
            if (attempt < 7) Thread.Sleep(75);
        }
        return null;
    }

    private static void VerifyWindowsUser(RetainedAppContainerTarget target)
    {
        if (!OpenProcessToken(target.ProcessHandle, 0x0008, out var token))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        using (var own = WindowsIdentity.GetCurrent())
            if (identity.User is null || own.User is null || !identity.User.Equals(own.User) || !target.IsCurrent())
                throw new InvalidOperationException("The UWP target belongs to a different Windows user or lifetime.");
    }

    private static string[] ReadArguments(Process parent)
    {
        _ = NtQueryInformationProcess(parent.SafeHandle, 60, 0, 0, out int size);
        if (size is < 16 or > 128 * 1024) throw new InvalidDataException("The UWP parent command line is unavailable.");
        nint memory = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(parent.SafeHandle, 60, memory, size, out int written) != 0 || written > size)
                throw new InvalidDataException("The UWP parent command line could not be verified.");
            var value = Marshal.PtrToStructure<UnicodeString>(memory);
            long offset = value.Buffer.ToInt64() - memory.ToInt64();
            if ((value.Length & 1) != 0 || value.Length == 0 || value.Length > value.MaximumLength
                || offset < Marshal.SizeOf<UnicodeString>() || offset > size - value.Length)
                throw new InvalidDataException("The UWP parent command line has invalid bounds.");
            string command = Marshal.PtrToStringUni(value.Buffer, value.Length / 2)!;
            nint arguments = CommandLineToArgvW(command, out int count);
            if (arguments == 0) throw new InvalidDataException("The UWP parent command line has invalid arguments.");
            try
            {
                if (count is < 1 or > 4096) throw new InvalidDataException("The UWP parent command line has invalid arguments.");
                return Enumerable.Range(0, count).Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, index * IntPtr.Size))!).ToArray();
            }
            finally { LocalFree(arguments); }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { internal ushort Length, MaximumLength; internal nint Buffer; }
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(Microsoft.Win32.SafeHandles.SafeProcessHandle process, int informationClass, nint data, int size, out int returned);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint CommandLineToArgvW(string line, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint access, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
}
