// Playhub Xbox Session changes, 2026-10-03. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Playhub.GameSession;
using WSGM.PackagedLaunch;

internal static class XboxSession
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            HideOwnConsole();
            if (args.Length == 1 && args[0] == "--check-steam")
            {
                using var files = TrustedSteamFiles.Open();
                Console.WriteLine("PASS installed Steam client path and four signed Valve x64 components; no activation or remote write.");
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--check-uwp")
            {
                var check = XboxCommand.Parse(new[] { "--uwp" }.Concat(args.Skip(1)).ToArray());
                var package = GdkLaunchResolver.Resolve(check.Aumid, check.Executable, new RegisteredPackageCatalog(physicalPaths: true));
                using var files = TrustedSteamFiles.Open();
                Console.WriteLine($"PASS registered package={package.PackageFullName}; helper={package.Helper}; game={package.Game}; signed Steam components; no activation or remote write.");
                return 0;
            }
            var command = XboxCommand.Parse(args);
            return Run(command);
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Error(error.ToString());
            MessageBox(0, error.Message, "Playhub", 0x10);
            return 1;
        }
    }

    internal static int Run(XboxCommand command, Func<IUwpRuntime>? originalRuntime = null,
        Func<XboxCommand, IUwpRuntime>? experimentalRuntime = null)
    {
        var runtime = command.Experimental
            ? (experimentalRuntime ?? CreateExperimentalRuntime)(command)
            : (originalRuntime ?? (() => new WindowsUwpRuntime()))();
        return UwpLifetime.Run(command.Aumid, command.Executable, command.Arguments, runtime);
    }

    private static IUwpRuntime CreateExperimentalRuntime(XboxCommand command)
    {
        GdkLaunchPlan? plan = null;
        try { plan = GdkLaunchResolver.Resolve(command.Aumid, command.Executable, new RegisteredPackageCatalog(physicalPaths: true)); }
        catch (Exception error) { PackagedLaunchLog.Info("Original activation route: " + error.Message); }
        if (plan is not null) GdkLaunchResolver.RejectRunningGame(plan);
        return plan is not null && XboxCommand.IsSteamShortcutSession(SteamInstallation.SessionVariables())
            ? new HandoffRuntime(plan, command.Aumid) : new WindowsUwpRuntime();
    }

    private static void HideOwnConsole()
    {
        var console = NativeMethods.GetConsoleWindow();
        var processes = new uint[2];
        if (console != 0 && GetConsoleProcessList(processes, 2) == 1 && processes[0] == Environment.ProcessId)
            NativeMethods.ShowWindow(console, 0);
    }

    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList([Out] uint[] ids, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint window, string text, string caption, uint type);
}

internal sealed record XboxCommand(string Aumid, string Executable, string Arguments, bool Experimental = false)
{
    internal static XboxCommand Parse(string[] args)
    {
        if (args.Length < 3 || (args[0] != "--uwp" && args[0] != "--gdk-experimental"))
            throw new ArgumentException("Expected --uwp, application identity and game executable.");
        var parts = args[1].Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(args[2]) || !args[2].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid Xbox application identity.");
        var arguments = args.Length > 3 ? args[3] : "";
        if (args.Length > 4) arguments += " " + string.Join(" ", args.Skip(4).Select(UwpShortcutArguments.Quote));
        return new XboxCommand(args[1], args[2], arguments, args[0] == "--gdk-experimental");
    }

    internal static bool IsSteamShortcutSession(IEnumerable<string> variables)
    {
        var identities = variables.Where(value => value.StartsWith("SteamGameId=", StringComparison.OrdinalIgnoreCase)).ToArray();
        return identities.Length == 1 && ulong.TryParse(identities[0][12..], out var id)
            && (uint)id == 0x02000000 && (id >> 32) >= 0x80000000;
    }
}

internal sealed class HandoffRuntime(GdkLaunchPlan plan, string aumid, IUwpRuntime? runtime = null, Func<TrustedSteamFiles>? openFiles = null) : IUwpRuntime
{
    private readonly IUwpRuntime _runtime = runtime ?? new WindowsUwpRuntime();
    private bool _observed;
    public long Now => _runtime.Now;
    public bool ShellReady => _runtime.ShellReady;
    public void StartShell() => _runtime.StartShell();
    public void Delay(int milliseconds) => _runtime.Delay(milliseconds);
    public void Log(string message) => PackagedLaunchLog.Info(message);

    public IUwpProcess Activate(string identity, string arguments)
    {
        TrustedSteamFiles? files = null;
        try { files = (openFiles ?? TrustedSteamFiles.Open)(); }
        catch (Exception error) { PackagedLaunchLog.Error("Steam overlay setup unavailable; original activation: " + error.Message); }
        using (files)
        {
            var activated = _runtime.Activate(identity, arguments);
            if (files is null) return activated;
            try
            {
                using var helper = Process.GetProcessById((int)activated.Id);
                using var current = Process.GetCurrentProcess();
                string image = Path.Combine(RegisteredPackageCatalog.PhysicalDirectory(Path.GetDirectoryName(activated.Executable)!), Path.GetFileName(activated.Executable));
                if (!image.Equals(plan.Helper, StringComparison.OrdinalIgnoreCase) || activated.PackageFamily != aumid.Split('!')[0])
                    throw new InvalidOperationException("Activation returned a different registered launch helper. No write allowed.");
                if (helper.SessionId != current.SessionId || ProcessInspector.IsNativeX64(helper.Id) != true)
                    throw new InvalidOperationException("Returned helper session or architecture is unsupported. No write allowed.");
                uint length = 512;
                var package = new StringBuilder((int)length);
                if (NativeMethods.GetPackageFullName(helper.Handle, ref length, package) != 0 || package.ToString() != plan.PackageFullName)
                    throw new InvalidOperationException("Returned helper package differs from the validated manifest. No write allowed.");
                if (!NativeMethods.OpenProcessToken(helper.Handle, NativeMethods.TokenQuery, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    nint value = Marshal.AllocHGlobal(sizeof(int));
                    try
                    {
                        if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenIsAppContainer, value, sizeof(int), out _) || Marshal.ReadInt32(value) != 0)
                            throw new InvalidOperationException("Expected a full-trust GDK helper. AppContainer or unknown token refused.");
                    }
                    finally { Marshal.FreeHGlobal(value); }
                }
                finally { NativeMethods.CloseHandle(token); }
                var guard = new TargetHandleGuard(helper.Id, helper.Handle);
                var injector = new GameInjector(new PrivilegeJournal(), (pid, handle) => guard.Allows(pid, handle) && files.UnchangedRoot());
                var outcome = new PackagedWin32OverlayRoute(injector).Prepare(helper.Id);
                PackagedLaunchLog.Info($"setup succeeded={outcome.Succeeded}; {outcome.Detail}");
            }
            catch (Exception error) { PackagedLaunchLog.Error("Setup refused; continue the same game supervision: " + error.Message); }
            return activated;
        }
    }

    public IUwpProcess? FindGame(string family, string executable)
    {
        var game = _runtime.FindGame(family, executable);
        if (game is not null)
        {
            try
            {
                string image = Path.Combine(RegisteredPackageCatalog.PhysicalDirectory(Path.GetDirectoryName(game.Executable)!), Path.GetFileName(game.Executable));
                if (!image.Equals(plan.Game, StringComparison.OrdinalIgnoreCase))
                {
                    game.Dispose();
                    PackagedLaunchLog.Error("Matching package process has a different configured game path; not tracked.");
                    return null;
                }
            }
            catch { game.Dispose(); throw; }
        }
        if (game is not null && !_observed)
        {
            _observed = true;
            bool renderer = false;
            long deadline = Now + 5000;
            do
            {
                renderer = GameInjector.HasModule((int)game.Id, "GameOverlayRenderer64.dll");
                if (renderer) break;
                Delay(100);
            } while (Now < deadline);
            PackagedLaunchLog.Info($"actual-game pid={game.Id} renderer-reached={renderer}; observation only, no game writes");
        }
        return game;
    }
}

internal sealed class TargetHandleGuard
{
    private readonly int _pid;
    private readonly long _creation;
    private readonly Stopwatch _budget = Stopwatch.StartNew();
    internal TargetHandleGuard(int pid, nint handle, long? creationOverride = null)
    {
        _pid = pid;
        if (!NativeMethods.GetProcessTimes(handle, out long creation, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        _creation = creationOverride ?? creation;
    }
    internal bool Allows(int pid, nint handle)
        => pid == _pid && handle != 0 && _budget.Elapsed < TimeSpan.FromSeconds(90)
            && NativeMethods.WaitForSingleObject(handle, 0) == 258
            && NativeMethods.GetProcessTimes(handle, out long creation, out _, out _, out _) && creation == _creation;
}
