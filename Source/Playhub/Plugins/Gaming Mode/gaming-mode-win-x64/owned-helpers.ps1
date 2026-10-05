function ConvertFrom-WindowsCommandLine([string]$CommandLine) {
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return @() }
    if (-not ('PlayhubOwnedHelpers.CommandLine' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PlayhubOwnedHelpers {
    public static class CommandLine {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string command, out int count);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
        public static string[] Parse(string command) {
            int count;
            IntPtr pointer = CommandLineToArgvW(command, out count);
            if (pointer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            try {
                var result = new string[count];
                for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size));
                return result;
            } finally { LocalFree(pointer); }
        }
    }
}
'@
    }
    return [PlayhubOwnedHelpers.CommandLine]::Parse($CommandLine)
}

function Test-OwnedGamingModeHelper($Record, [string]$OwnerSid, [int]$SessionId, [string]$UserSid, [string[]]$PowerShellPaths, [string[]]$ScriptPaths) {
    if ($null -eq $Record -or $Record.SessionId -ne $SessionId -or $OwnerSid -ne $UserSid -or [string]::IsNullOrWhiteSpace($UserSid)) { return $false }
    try {
        if (-not [IO.Path]::IsPathRooted($Record.ExecutablePath)) { return $false }
        $image = [IO.Path]::GetFullPath($Record.ExecutablePath)
        if ($image -notin $PowerShellPaths) { return $false }
        $arguments = @(ConvertFrom-WindowsCommandLine $Record.CommandLine)
        if ($arguments.Count -lt 3 -or -not [IO.Path]::IsPathRooted($arguments[0]) -or -not [string]::Equals([IO.Path]::GetFullPath($arguments[0]), $image, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $fileIndex = -1
        for ($index = 1; $index -lt $arguments.Count; $index++) {
            # Only known startup flags are accepted before -File. In particular,
            # a command string containing a fake -File never establishes ownership.
            switch ($arguments[$index].ToLowerInvariant()) {
                '-noprofile' { continue }
                '-noninteractive' { continue }
                '-sta' { continue }
                '-nologo' { continue }
                '-executionpolicy' { if (++$index -ge $arguments.Count -or $arguments[$index] -ne 'Bypass') { return $false }; continue }
                '-windowstyle' { if (++$index -ge $arguments.Count -or $arguments[$index] -ne 'Hidden') { return $false }; continue }
                '-file' { $fileIndex = $index; break }
                default { return $false }
            }
            if ($fileIndex -ge 0) { break }
        }
        # These three installed helpers take no arguments. Reject extra script
        # arguments rather than broadening ownership to another invocation.
        if ($fileIndex -lt 1 -or $fileIndex -ne $arguments.Count - 2) { return $false }
        if (-not [IO.Path]::IsPathRooted($arguments[$fileIndex + 1])) { return $false }
        return [IO.Path]::GetFullPath($arguments[$fileIndex + 1]) -in $ScriptPaths
    } catch { return $false }
}

function Stop-OwnedGamingModeHelpers([string]$AssetsDirectory) {
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $powerShellPaths = @((Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'), (Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'))
    $scripts = @('desktop-safety.ps1', 'focus-rescue.ps1', 'xbox-gamebar.ps1') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $AssetsDirectory $_)) }
    foreach ($record in @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop)) {
        if ($record.SessionId -ne $session) { continue }
        if (-not (Test-OwnedGamingModeHelper $record $userSid $session $userSid $powerShellPaths $scripts)) { continue }
        try { $owner = Invoke-CimMethod -InputObject $record -MethodName GetOwnerSid -ErrorAction Stop } catch { continue }
        if ($owner.ReturnValue -ne 0 -or -not (Test-OwnedGamingModeHelper $record $owner.Sid $session $userSid $powerShellPaths $scripts)) { continue }
        $process = $null
        try {
            $process = [Diagnostics.Process]::GetProcessById([int]$record.ProcessId)
            # Acquire identity before termination and reject a reused PID.
            $null = $process.Handle
            $creationTicks = $process.StartTime.ToUniversalTime().Ticks
            # CIM represents creation time to microsecond precision.
            $creationTicks -= $creationTicks % 10
            if ($process.HasExited -or $process.SessionId -ne $session -or
                $creationTicks -ne $record.CreationDate.ToUniversalTime().Ticks -or
                -not [string]::Equals($process.Path, $record.ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $process.Kill()
            if (-not $process.WaitForExit(10000)) { throw 'Owned Gaming Mode helper did not stop.' }
        } catch [ArgumentException] {
            # A helper can exit naturally after seeing the opt-out marker.
        } catch {
            if ($null -ne $process -and $process.HasExited) { continue }
            throw
        } finally { if ($null -ne $process) { $process.Dispose() } }
    }
}
