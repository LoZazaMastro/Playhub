using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace Playhub.Shared;

internal static class DeckyStartupGuard
{
    private const string Marker = "# Playhub guarded Decky startup";
    private const string Script = """
# Playhub guarded Decky startup
param([ValidateSet('PluginLoader.exe','PluginLoader_noconsole.exe')][string]$Loader)
$mutex = New-Object Threading.Mutex($false, 'Global\Playhub.Decky.Start')
$owned = $false
function Test-DeckyListener {
    $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -eq 1337 -and $_.LocalAddress -in @('127.0.0.1','0.0.0.0','::') })
    foreach ($listener in $listeners) {
        $ownerProcess = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
        if ($ownerProcess.ProcessName -notin @('PluginLoader','PluginLoader_noconsole')) { throw "Port 1337 is occupied by $($ownerProcess.ProcessName) (PID $($ownerProcess.Id)). No process was stopped." }
    }
    return ($listeners.Count -gt 0)
}
try {
    try { $owned = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { exit 1 }
    if (Test-DeckyListener) { exit 0 }
    $running = @(Get-Process -Name PluginLoader,PluginLoader_noconsole -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        for ($attempt=0; $attempt -lt 40; $attempt++) {
            if (Test-DeckyListener) { exit 0 }
            Start-Sleep -Milliseconds 250
        }
        throw 'Decky is still starting or unavailable. No duplicate was started.'
    }
    $exe = Join-Path $PSScriptRoot $Loader
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { exit 1 }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.WorkingDirectory = $PSScriptRoot
    $start.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($start)
    if ($null -ne $process) {
        try {
            for ($attempt=0; $attempt -lt 40; $attempt++) {
                if (Test-DeckyListener) { exit 0 }
                if ($process.HasExited) { throw "Decky exited during startup ($($process.ExitCode))." }
                Start-Sleep -Milliseconds 250
            }
            throw 'Decky did not open port 1337. No duplicate was started.'
        } finally { $process.Dispose() }
    }
} catch {
    try { Add-Content -LiteralPath (Join-Path $PSScriptRoot 'Playhub-Decky-startup.log') -Value ((Get-Date -Format o) + ' ' + $_.Exception.Message) -ErrorAction Stop } catch { }
    exit 1
} finally {
    if ($owned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
""";

    internal static T RunExclusive<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Global\Playhub.Decky.Start");
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new TimeoutException("Another Decky startup is still in progress.");
            return action();
        }
        finally { if (owned) mutex.ReleaseMutex(); }
    }

    internal static bool StartOrReuse(ProcessStartInfo start, Action<string>? log = null) => RunExclusive(() =>
    {
        if (ReuseListener(log)) return true;
        foreach (string name in new[] { "PluginLoader", "PluginLoader_noconsole" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                    if (!process.HasExited)
                    {
                        for (int attempt = 0; attempt < 40; attempt++)
                        {
                            if (ReuseListener(log)) return true;
                            if (process.HasExited) break;
                            Thread.Sleep(250);
                        }
                        if (!process.HasExited) throw new IOException("Decky is running but has not opened port 1337. No duplicate was started.");
                    }
        using var started = Process.Start(start) ?? throw new IOException("Decky could not be started.");
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (ReuseListener(log)) return true;
            if (started.HasExited) throw new IOException($"Decky exited during startup (code {started.ExitCode}).");
            Thread.Sleep(250);
        }
        throw new IOException("Decky did not open port 1337 within ten seconds. Its process was left running; no duplicate was started.");
    });

    internal static bool ReuseListener(Action<string>? log, int port = 1337)
    {
        int? owner = ListenerOwner(port);
        if (owner is null) return false;
        using var process = Process.GetProcessById(owner.Value);
        string name = process.ProcessName;
        if (name != "PluginLoader" && name != "PluginLoader_noconsole")
            throw new IOException($"Port {port} is already used by {name} (PID {owner}). Decky was not started and the other process was left untouched.");
        log?.Invoke($"Reusing Decky listener on port {port} (PID {owner}).");
        return true;
    }

    internal static int? ListenerOwner(int port) => ListenerOwnerForFamily(port, 2) ?? ListenerOwnerForFamily(port, 23);

    private static int? ListenerOwnerForFamily(int port, int family)
    {
        int size = 0;
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
        if (result != 122 && result != 0) throw new IOException($"Cannot inspect TCP listeners ({result}).");
        IntPtr table = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(table, ref size, false, family, 3, 0);
            if (result != 0) throw new IOException($"Cannot inspect TCP listeners ({result}).");
            int count = Marshal.ReadInt32(table);
            for (int index = 0; index < count; index++)
            {
                IntPtr row = IntPtr.Add(table, 4 + index * (family == 2 ? 24 : 56));
                uint address = family == 2 ? unchecked((uint)Marshal.ReadInt32(row, 4)) : 0;
                int networkPort = Marshal.ReadInt32(row, family == 2 ? 8 : 20);
                int localPort = ((networkPort & 255) << 8) | ((networkPort >> 8) & 255);
                if (localPort == port && (address == 0 || address == 0x0100007f))
                    return Marshal.ReadInt32(row, family == 2 ? 20 : 52);
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(table); }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    internal static string CreateCommand(string loaderPath)
    {
        string name = Path.GetFileName(loaderPath);
        if (!name.Equals("PluginLoader.exe", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("PluginLoader_noconsole.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a Decky executable.");
        string scriptPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(loaderPath))!, "Playhub-StartDecky.ps1");
        if (File.Exists(scriptPath) && !File.ReadAllText(scriptPath).StartsWith(Marker, StringComparison.Ordinal))
            throw new IOException("The Decky startup helper path is already in use.");
        if (!File.Exists(scriptPath) || File.ReadAllText(scriptPath) != Script)
            File.WriteAllText(scriptPath, Script, new UTF8Encoding(false));
        return $"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\" -Loader {name}";
    }

    internal static int UpgradeExistingAutostart()
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (run == null) return 0;
        int changed = 0;
        foreach (string name in run.GetValueNames())
        {
            string command = (run.GetValue(name) as string ?? "").Trim();
            // Only migrate direct executable entries; leave user scripts and arguments untouched.
            string path = Environment.ExpandEnvironmentVariables(command.Trim('"'));
            if (command.Contains("Playhub-StartDecky.ps1", StringComparison.OrdinalIgnoreCase))
            {
                string services = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "services");
                string loader = Path.Combine(services, command.EndsWith("PluginLoader.exe", StringComparison.OrdinalIgnoreCase) ? "PluginLoader.exe" : "PluginLoader_noconsole.exe");
                if (File.Exists(loader)) { run.SetValue(name, CreateCommand(loader)); changed++; }
                continue;
            }
            if (!File.Exists(path)) continue;
            string executable = Path.GetFileName(path);
            if (!executable.Equals("PluginLoader.exe", StringComparison.OrdinalIgnoreCase) &&
                !executable.Equals("PluginLoader_noconsole.exe", StringComparison.OrdinalIgnoreCase)) continue;
            run.SetValue(name, CreateCommand(path));
            changed++;
        }
        return changed;
    }
}
