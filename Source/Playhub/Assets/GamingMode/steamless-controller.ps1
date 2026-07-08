param(
    [switch]$Stop
)

$ErrorActionPreference = 'SilentlyContinue'

$gameListPath = Join-Path $env:APPDATA 'Playhub\steamless-games.txt'
$logPath = Join-Path $env:APPDATA 'Playhub\steamless-controller.log'
$mirrorLogPath = 'F:\Playhub\steamless-controller.log'
$mutexName = 'Local\PlayhubSteamlessControllerWatcher'

function Write-PlayhubLog {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    try {
        $dir = Split-Path -Parent $logPath
        if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch {}
    try {
        if (Test-Path -LiteralPath 'F:\Playhub') {
            Add-Content -LiteralPath $mirrorLogPath -Value $line -Encoding UTF8
        }
    } catch {}
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PlayhubSteamlessNative {
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
"@

function Get-SteamlessExecutable {
    $candidates = New-Object System.Collections.Generic.List[string]
    try { $candidates.Add((Join-Path $env:ProgramFiles 'SteamlessController\SteamlessController.exe')) } catch {}
    try {
        $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
        if ($programFilesX86) {
            $candidates.Add((Join-Path $programFilesX86 'SteamlessController\SteamlessController.exe'))
        }
    } catch {}
    try { $candidates.Add((Join-Path $env:LOCALAPPDATA 'SteamlessController\SteamlessController.exe')) } catch {}
    try {
        $runValue = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SteamlessController' -ErrorAction SilentlyContinue).SteamlessController
        if ($runValue) {
            $text = ($runValue + '').Trim()
            if ($text.StartsWith('"')) {
                $end = $text.IndexOf('"', 1)
                if ($end -gt 1) { $text = $text.Substring(1, $end - 1) }
            } elseif ($text.IndexOf('.exe', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $idx = $text.IndexOf('.exe', [System.StringComparison]::OrdinalIgnoreCase)
                $text = $text.Substring(0, $idx + 4)
            }
            $candidates.Add($text)
        }
    } catch {}

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Start-SteamlessController {
    $exe = Get-SteamlessExecutable
    if (-not $exe) {
        Write-PlayhubLog 'SteamlessController non installato: watcher in pausa.'
        return $false
    }

    if (-not (Get-Process -Name 'SteamlessController' -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden
        Start-Sleep -Milliseconds 900
        Write-PlayhubLog 'SteamlessController avviato.'
    }

    return $true
}

function Get-SteamlessWindow {
    $parent = [IntPtr]::new(-3)
    return [PlayhubSteamlessNative]::FindWindowEx($parent, [IntPtr]::Zero, 'SteamlessControllerTray', 'SteamlessController')
}

function Send-SteamlessCommand {
    param([int]$CommandId)
    if (-not (Start-SteamlessController)) {
        return $false
    }

    for ($i = 0; $i -lt 20; $i++) {
        $hwnd = Get-SteamlessWindow
        if ($hwnd -ne [IntPtr]::Zero) {
            $result = [IntPtr]::Zero
            [PlayhubSteamlessNative]::SendMessageTimeout($hwnd, 0x0111, [IntPtr]::new($CommandId), [IntPtr]::Zero, 0, 3000, [ref]$result) | Out-Null
            return $true
        }
        Start-Sleep -Milliseconds 250
    }

    Write-PlayhubLog 'SteamlessController avviato, ma il canale comandi non risponde.'
    return $false
}

function Test-SteamlessModeActive {
    $sidecar = ('vii' + 'per')
    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq ($sidecar + '.exe') }
    foreach ($process in $processes) {
        $path = ($process.ExecutablePath + '')
        $cmd = ($process.CommandLine + '')
        if ($path -match '(?i)SteamlessController' -or $cmd -match '(?i)SteamlessController') {
            return $true
        }
    }

    return $false
}

function Set-SteamlessMode {
    param([bool]$Enabled)

    $active = Test-SteamlessModeActive
    if ($active -eq $Enabled) {
        return $true
    }

    if (Send-SteamlessCommand -CommandId 1001) {
        Start-Sleep -Milliseconds 1200
        $nowActive = Test-SteamlessModeActive
        Write-PlayhubLog ("Profilo controller: " + ($(if ($nowActive) { 'attivo' } else { 'normale' })))
        return ($nowActive -eq $Enabled)
    }

    return $false
}

function Get-GameProcessNames {
    $names = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    if (Test-Path -LiteralPath $gameListPath) {
        foreach ($line in Get-Content -LiteralPath $gameListPath -ErrorAction SilentlyContinue) {
            $name = ($line + '').Trim()
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                [void]$names.Add([IO.Path]::GetFileNameWithoutExtension($name))
            }
        }
    }

    [void]$names.Add('UWPHook')
    return $names
}

function Test-NonSteamGameRunning {
    $names = Get-GameProcessNames
    if ($names.Count -eq 0) {
        return $false
    }

    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
        if ($names.Contains($process.ProcessName)) {
            return $true
        }
    }

    return $false
}

if ($Stop) {
    Write-PlayhubLog 'Stop richiesto da Playhub.'
    Set-SteamlessMode -Enabled:$false | Out-Null
    try {
        Get-CimInstance Win32_Process |
            Where-Object { $_.ProcessId -ne $PID -and $_.Name -eq 'powershell.exe' -and $_.CommandLine -like '*steamless-controller.ps1*' } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    } catch {}
    exit 0
}

$mutex = New-Object System.Threading.Mutex($false, $mutexName)
if (-not $mutex.WaitOne(0)) {
    exit 0
}

Write-PlayhubLog 'Watcher Steam Controller avviato.'
$lastRunning = $false
try {
    Set-SteamlessMode -Enabled:$false | Out-Null
    while ($true) {
        $running = Test-NonSteamGameRunning
        if ($running -ne $lastRunning) {
            if ($running) {
                Write-PlayhubLog 'Gioco non-Steam rilevato.'
                Set-SteamlessMode -Enabled:$true | Out-Null
            } else {
                Write-PlayhubLog 'Gioco non-Steam chiuso.'
                Set-SteamlessMode -Enabled:$false | Out-Null
            }
            $lastRunning = $running
        }
        elseif ($running -and -not (Test-SteamlessModeActive)) {
            Write-PlayhubLog 'Profilo controller non attivo durante il gioco: ritento.'
            Set-SteamlessMode -Enabled:$true | Out-Null
        }
        elseif (-not $running -and (Test-SteamlessModeActive)) {
            Write-PlayhubLog 'Profilo controller ancora attivo senza gioco: ripristino.'
            Set-SteamlessMode -Enabled:$false | Out-Null
        }

        Start-Sleep -Seconds 2
    }
}
finally {
    try { Set-SteamlessMode -Enabled:$false | Out-Null } catch {}
    try { $mutex.ReleaseMutex() } catch {}
    try { $mutex.Dispose() } catch {}
}
