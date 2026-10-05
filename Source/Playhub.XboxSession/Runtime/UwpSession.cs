using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Playhub.GameSession;

internal interface IUwpProcess : IDisposable
{
    uint Id { get; }
    string Executable { get; }
    string PackageFamily { get; }
    int WaitForExit();
}

internal interface IUwpRuntime
{
    long Now { get; }
    bool ShellReady { get; }
    void StartShell();
    void Delay(int milliseconds);
    IUwpProcess Activate(string aumid, string arguments);
    IUwpProcess? FindGame(string family, string executable);
    void Log(string message);
}

internal static class UwpLifetime
{
    internal static int Run(string aumid, string executable, string arguments, IUwpRuntime runtime)
    {
        var separator = aumid.IndexOf('!');
        if (separator <= 0 || separator == aumid.Length - 1 || string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("Invalid Xbox application identity.");
        var family = aumid[..separator];
        var ready = runtime.ShellReady;
        if (!ready) runtime.StartShell();
        var shellDeadline = runtime.Now + 15000;
        long? readySince = null;
        while (true)
        {
            if (runtime.ShellReady)
            {
                readySince ??= runtime.Now;
                if (runtime.Now - readySince.Value >= 700) break;
            }
            else readySince = null;
            if (runtime.Now >= shellDeadline) throw new TimeoutException("Windows desktop shell did not become ready for the Xbox game.");
            runtime.Delay(100);
        }
        runtime.Log($"shell-ready restored={!ready}; activating {aumid}");
        using var activated = runtime.Activate(aumid, arguments);
        runtime.Log($"activated pid={activated.Id} executable={activated.Executable}");
        if (string.Equals(Path.GetFileName(activated.Executable), Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase))
            return Wait(activated, runtime);
        if (!string.Equals(Path.GetFileName(activated.Executable), "GameLaunchHelper.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Xbox activation returned an unexpected process; game tracking was not attached.");

        var handoffDeadline = runtime.Now + 15000;
        while (runtime.Now < handoffDeadline)
        {
            using var game = runtime.FindGame(family, executable);
            if (game is not null && IsGame(game.PackageFamily, game.Executable, family, executable))
            {
                runtime.Log($"game-handoff pid={game.Id} executable={game.Executable}");
                return Wait(game, runtime);
            }
            runtime.Delay(250);
        }
        throw new TimeoutException("Xbox launched its bootstrapper but the game process did not become available.");
    }

    internal static bool IsGame(string actualFamily, string actualExecutable, string family, string executable) =>
        string.Equals(actualFamily, family, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(actualExecutable), Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase);

    private static int Wait(IUwpProcess game, IUwpRuntime runtime)
    {
        var code = game.WaitForExit();
        runtime.Log($"exited pid={game.Id} code=0x{code:X8}");
        return code;
    }
}

internal sealed class WindowsUwpRuntime : IUwpRuntime
{
    public long Now => Environment.TickCount64;
    public bool ShellReady
    {
        get
        {
            var window = GetShellWindow();
            if (window == IntPtr.Zero) return false;
            GetWindowThreadProcessId(window, out var pid);
            try
            {
                using var shell = WindowsUwpProcess.TryOpen(pid);
                return shell is not null && string.Equals(Path.GetFileName(shell.Executable), "explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                    SendMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, 2, 100, out _) != IntPtr.Zero;
            }
            catch (Win32Exception) { return false; }
        }
    }

    public void StartShell()
    {
        XboxShellBrokerClient.Ensure();
    }

    public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
    public void Log(string message) => UwpSessionLog.Write(message);

    public IUwpProcess Activate(string aumid, string arguments)
    {
        var manager = (IApplicationActivationManager)(object)new ApplicationActivationManager();
        try
        {
            Marshal.ThrowExceptionForHR(manager.ActivateApplication(aumid, arguments, 0, out var pid));
            return WindowsUwpProcess.TryOpen(pid) ?? throw new InvalidOperationException("Xbox game exited before its process could be tracked.");
        }
        finally { Marshal.FinalReleaseComObject(manager); }
    }

    public IUwpProcess? FindGame(string family, string executable)
    {
        WindowsUwpProcess? match = null;
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (candidate)
                {
                    try
                    {
                        try { if (candidate.SessionId != current.SessionId) continue; }
                        catch (InvalidOperationException) { continue; }
                        var process = WindowsUwpProcess.TryOpen(unchecked((uint)candidate.Id));
                        if (process is null) continue;
                        if (!UwpLifetime.IsGame(process.PackageFamily, process.Executable, family, executable)) { process.Dispose(); continue; }
                        if (match is not null)
                        {
                            process.Dispose();
                            throw new InvalidOperationException("Multiple Xbox game processes match this application; refusing ambiguous tracking.");
                        }
                        match = process;
                    }
                    catch (Win32Exception) { }
                    catch (ArgumentException) { }
                }
            }
            return match;
        }
        catch { match?.Dispose(); throw; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManager { }

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string aumid,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint pid);
        [PreserveSig] int ActivateForFile(string aumid, IntPtr items, string verb, out uint pid);
        [PreserveSig] int ActivateForProtocol(string aumid, IntPtr items, out uint pid);
    }
}

internal sealed class WindowsUwpProcess : IUwpProcess
{
    private readonly SafeFileHandle _handle;
    public uint Id { get; }
    public string Executable { get; }
    public string PackageFamily { get; }

    private WindowsUwpProcess(uint id, SafeFileHandle handle, string executable, string family)
    {
        Id = id; _handle = handle; Executable = executable; PackageFamily = family;
    }

    internal static WindowsUwpProcess? TryOpen(uint id)
    {
        var handle = OpenProcess(0x00101000, false, id);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        try
        {
            var size = 32768u;
            var path = new StringBuilder((int)size);
            if (!QueryFullProcessImageName(handle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var familySize = 256u;
            var family = new StringBuilder((int)familySize);
            var result = GetPackageFamilyName(handle, ref familySize, family);
            return new WindowsUwpProcess(id, handle, path.ToString(), result == 0 ? family.ToString() : "");
        }
        catch { handle.Dispose(); throw; }
    }

    public int WaitForExit()
    {
        if (WaitForSingleObject(_handle, uint.MaxValue) != 0 || !GetExitCodeProcess(_handle, out var code))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return unchecked((int)code);
    }

    public void Dispose() => _handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(SafeFileHandle process, ref uint length, StringBuilder family);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
}

internal static class UwpSessionLog
{
    internal static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "game-session.log"), $"{DateTimeOffset.Now:O} {message}\n");
        }
        catch { }
    }
}
