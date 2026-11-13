// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WSGM.PackagedLaunch;
using Playhub.Shared;

internal sealed record Win32CompanionHandoff(uint WrapperPid, long WrapperBirth, uint GamePid,
    long GameBirth, ulong GameId, string Executable, EpicLaunchProvenance? Epic = null)
{
    internal bool Matches(uint wrapper, long wrapperBirth, ulong gameId, uint gamePid, long gameBirth,
        string image, long now) => WrapperPid == wrapper && WrapperBirth == wrapperBirth && GameId == gameId
        && GamePid == gamePid && gamePid != wrapper && GameBirth == gameBirth && gameBirth >= wrapperBirth
        && gameBirth <= now && now - gameBirth <= TimeSpan.FromSeconds(15).Ticks
        && !string.IsNullOrWhiteSpace(Executable) && Path.IsPathFullyQualified(Executable)
        && Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetFullPath(Executable), image, StringComparison.OrdinalIgnoreCase)
        && (Epic is null || (Epic.WrapperPid == wrapper && Epic.WrapperBirth == wrapperBirth && Epic.GamePid == gamePid
            && Epic.GameBirth == gameBirth && Epic.GameExecutable.Equals(image, StringComparison.OrdinalIgnoreCase)));
}

internal static class Win32SteamCompanion
{
    internal static int Run()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            using var parent = TrialSteamTracking.OpenParent(self);
            ulong gameId = AppContainerTrial.ReadSteamGameId(Environment.GetEnvironmentVariable("SteamGameId"));
            using var steam = TrustedSteamFiles.Open();
            var handoff = ReceiveHandoff();
            if (handoff is null) return 0;
            if (!AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
                throw new InvalidOperationException("Steam did not register the exact protocol session wrapper.");
            using var epic = handoff.Epic is null ? null : EpicLaunchWindows.RetainFromAuthenticatedWrapper(handoff.Epic, (uint)parent.Id, parent.StartTime.ToFileTimeUtc());
            if (handoff.Epic is not null && epic?.Current != true)
                throw new InvalidOperationException("The Epic game launch provenance is unavailable.");
            using var game = Inspect(handoff, parent, gameId, self.SessionId, epic is null ? null : () => epic.Current);
            if (!AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
                throw new InvalidOperationException("Steam did not register the exact Win32 session wrapper.");
            using var readerBootstrap = epic is null ? null : SteamTrackedWrapperChain.OpenEpic(steam, parent, handoff, epic);
            if (epic is not null) PackagedLaunchLog.Info($"Steam reader bootstrap: origin=epic-protocol; wrapper-chain-created={readerBootstrap is not null}; reader-steam-log-not-assumed=True.");
            PackagedLaunchLog.Info($"Win32 Steam companion verified wrapper={parent.Id} game={handoff.GamePid} birth={handoff.GameBirth} gameId={gameId}.");
            return SteamCompanionTrial.RunSurface(game, parent, steam, gameId, handoff.GamePid, handoff.GameBirth,
                onConfiguredLayout: () => SteamControllerLayoutTrial.TryStartWin32(steam, gameId, game, handoff.GamePid, handoff.GameBirth,
                    parent, () => SteamCompanionTrial.GameHasForeground && (epic?.RetainedCurrent ?? true), () => SteamCompanionTrial.OverlayIsOpen, readerBootstrap));
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Error("Win32 Steam companion skipped: " + error.Message);
            return 1;
        }
    }

    internal static Win32CompanionHandoff? ReceiveHandoff()
    {
        if (!Console.IsInputRedirected) throw new InvalidOperationException("A private GameSession handoff is required.");
        using var input = Console.OpenStandardInput();
        return ReadHandoff(input).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    internal static async Task<Win32CompanionHandoff?> ReadHandoff(Stream input)
    {
        byte[] buffer = new byte[64 * 1024];
        int length = 0;
        while (length < buffer.Length)
        {
            int count = await input.ReadAsync(buffer.AsMemory(length)).ConfigureAwait(false);
            if (count == 0)
            {
                if (length == 0) return null;
                throw new InvalidDataException("The Win32 handoff is incomplete.");
            }
            int end = Array.IndexOf(buffer, (byte)'\n', length, count);
            if (end >= 0)
            {
                if (end != length + count - 1 || await input.ReadAsync(new byte[1]).ConfigureAwait(false) != 0)
                    throw new InvalidDataException("Unexpected data after the Win32 handoff.");
                return JsonSerializer.Deserialize<Win32CompanionHandoff>(buffer.AsSpan(0, end))
                    ?? throw new InvalidDataException("The Win32 handoff is empty.");
            }
            length += count;
        }
        throw new InvalidDataException("The Win32 handoff is too large.");
    }

    internal static SafeProcessHandle Inspect(Win32CompanionHandoff handoff, Process parent, ulong gameId, int session, Func<bool>? epicCurrent = null)
    {
        if (handoff.GamePid is 0 or > int.MaxValue) throw new InvalidOperationException("Invalid Win32 target identity.");
        var game = OpenProcess(0x00101000, false, handoff.GamePid);
        try
        {
            if (game.IsInvalid || WaitForSingleObject(game, 0) != 258 || GetProcessId(game) != handoff.GamePid
                || !GetProcessTimes(game, out long birth, out _, out _, out _)
                || !ProcessIdToSessionId(handoff.GamePid, out uint actualSession) || actualSession != session)
                throw new InvalidOperationException("The original Win32 game is unavailable.");
            var image = new StringBuilder(32768);
            uint length = (uint)image.Capacity;
            if (!QueryFullProcessImageName(game, 0, image, ref length)
                || !handoff.Matches((uint)parent.Id, parent.StartTime.ToFileTimeUtc(), gameId, handoff.GamePid,
                    birth, Path.GetFullPath(image.ToString()), DateTime.UtcNow.ToFileTimeUtc())
                || (handoff.Epic is null ? ParentPid(handoff.GamePid) != parent.Id : epicCurrent?.Invoke() != true) || parent.HasExited)
                throw new InvalidOperationException("The Win32 target is not the wrapper's exact fresh child.");
            VerifyUserToken(game);
            if (WaitForSingleObject(game, 0) != 258 || handoff.Epic is not null && epicCurrent?.Invoke() != true)
                throw new InvalidOperationException("The original Win32 game exited during inspection.");
            return game;
        }
        catch { game.Dispose(); throw; }
    }

    private static void VerifyUserToken(SafeProcessHandle game)
    {
        if (!OpenProcessToken(game, 0x0008, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        using (var own = WindowsIdentity.GetCurrent())
        {
            if (identity.User is null || own.User is null || !identity.User.Equals(own.User)
                || !GetTokenInformation(token, 29, out int container, sizeof(int), out _) || container != 0)
                throw new InvalidOperationException("The Win32 game token is outside the exact Windows user session.");
        }
    }

    private static uint ParentPid(uint pid)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var row = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        for (bool available = Process32First(snapshot, ref row); available; available = Process32Next(snapshot, ref row))
            if (row.ProcessId == pid) return row.ParentId;
        return 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal nuint Heap;
        internal uint Module, Threads, ParentId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Exe;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint rights, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long birth, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint rights, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, out int value, int size, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW")] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW")] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
