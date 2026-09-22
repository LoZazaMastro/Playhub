using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Playhub.Shared;

internal static class DeckyStartupGuard
{
    private const string Marker = "# Playhub guarded Decky startup";
    private const string Script = """
# Playhub guarded Decky startup
param([ValidateSet('PluginLoader.exe','PluginLoader_noconsole.exe')][string]$Loader)
$mutex = New-Object Threading.Mutex($false, ('Local\Playhub.Decky.Start.' + [Diagnostics.Process]::GetCurrentProcess().SessionId))
$owned = $false
try {
    try { $owned = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { exit 1 }
    if (Get-Process -Name PluginLoader,PluginLoader_noconsole -ErrorAction SilentlyContinue) { exit 0 }
    $exe = Join-Path $PSScriptRoot $Loader
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { exit 1 }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.WorkingDirectory = $PSScriptRoot
    $start.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($start)
    if ($null -ne $process) { $process.Dispose() }
} finally {
    if ($owned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
""";

    internal static T RunExclusive<T>(Func<T> action)
    {
        using var self = Process.GetCurrentProcess();
        using var mutex = new Mutex(false, @"Local\Playhub.Decky.Start." + self.SessionId);
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
