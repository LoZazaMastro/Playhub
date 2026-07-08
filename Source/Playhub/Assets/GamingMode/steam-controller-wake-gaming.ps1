param(
    [switch]$ConfigureOnly,
    [switch]$EnableWake
)

$ErrorActionPreference = "SilentlyContinue"

$playhubData = Join-Path $env:APPDATA "Playhub"
New-Item -ItemType Directory -Force -Path $playhubData | Out-Null

$logFile = Join-Path $playhubData "steam-controller-wake.log"
$mirrorLog = $null
if (Test-Path "F:\Playhub") {
    $mirrorLog = "F:\Playhub\steam-controller-wake.log"
}

function Write-PlayhubLog {
    param([string]$Message)

    $line = "{0} {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
    if ($mirrorLog) {
        Add-Content -LiteralPath $mirrorLog -Value $line -Encoding UTF8
    }
}

function Enable-SteamControllerWake {
    $classes = @("MSPower_DeviceWakeEnable", "MSPower_DeviceEnable")
    foreach ($class in $classes) {
        $items = Get-CimInstance -Namespace root\wmi -ClassName $class |
            Where-Object { $_.InstanceName -match "VID_28DE&PID_1304" }

        foreach ($item in $items) {
            if ($EnableWake -or -not $item.Enable) {
                $item.Enable = $true
                Set-CimInstance -InputObject $item | Out-Null
                Write-PlayhubLog ("Enabled wake on {0}: {1}" -f $class, $item.InstanceName)
            }
        }
    }

    $names = New-Object System.Collections.Generic.HashSet[string]
    try {
        Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
            Where-Object { $_.InstanceId -match "VID_28DE|PID_1304" -or $_.FriendlyName -match "Steam Controller|Valve" } |
            ForEach-Object {
                if (-not [string]::IsNullOrWhiteSpace($_.FriendlyName)) {
                    [void]$names.Add($_.FriendlyName)
                }
            }
    } catch {}

    try {
        Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
            Where-Object { $_.DeviceID -match "VID_28DE|PID_1304" -or $_.Name -match "Steam Controller|Valve" } |
            ForEach-Object {
                if (-not [string]::IsNullOrWhiteSpace($_.Name)) {
                    [void]$names.Add($_.Name)
                }
            }
    } catch {}

    try {
        powercfg /devicequery wake_programmable 2>$null |
            Where-Object { $_ -match "Steam Controller|Valve|Controller Puck" } |
            ForEach-Object {
                if (-not [string]::IsNullOrWhiteSpace($_)) {
                    [void]$names.Add($_.Trim())
                }
            }
    } catch {}

    foreach ($name in $names) {
        try {
            powercfg /deviceenablewake "$name" 2>$null | Out-Null
            Write-PlayhubLog "Enabled wake through powercfg: $name"
        } catch {}
    }
}

function Get-WakeEvidence {
    $lastWake = ""
    try {
        $lastWake = powercfg /lastwake 2>&1 | Out-String
    } catch {
    }

    $eventText = ""
    try {
        $events = Get-WinEvent -FilterHashtable @{
            LogName = "System"
            ProviderName = "Microsoft-Windows-Power-Troubleshooter"
            Id = 1
            StartTime = (Get-Date).AddMinutes(-10)
        } -MaxEvents 3
        $eventText = ($events | ForEach-Object { $_.Message }) -join "`n"
    } catch {
    }

    return ($lastWake + "`n" + $eventText)
}

function Test-SteamControllerWake {
    param([string]$Evidence)

    if ($Evidence -match "(?i)(VID_28DE|PID_1304|USB\\VID_28DE|Steam Controller|Valve)") {
        return $true
    }

    $receiverPresent = Test-ValveReceiverPresent
    if ($receiverPresent -and $Evidence -match "(?i)(Origine attivazione:\s+Unknown|Wake Source:\s+Unknown|Unknown|Conteggio origine riattivazione\s+-\s+0|Wake Source Count\s+-\s+0)") {
        Write-PlayhubLog "Wake source is unknown, but the Steam Controller receiver is present. Continuing."
        return $true
    }

    if ($Evidence -match "(?i)(Origine attivazione:\s+Unknown|Wake Source:\s+Unknown|Unknown)") {
        Write-PlayhubLog "Wake source is unknown. Gaming Mode switch skipped."
        return $false
    }

    Write-PlayhubLog "Wake source does not match Steam Controller. Gaming Mode switch skipped."
    return $false
}

function Test-ValveReceiverPresent {
    try {
        $devices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
            Where-Object { $_.InstanceId -match "VID_28DE|PID_1304" -or $_.FriendlyName -match "Steam Controller|Valve" }
        if ($devices) { return $true }
    } catch {}

    try {
        $devices = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
            Where-Object { $_.DeviceID -match "VID_28DE|PID_1304" -or $_.Name -match "Steam Controller|Valve" }
        if ($devices) { return $true }
    } catch {}

    return $false
}

function Ensure-GamingModeAgent {
    $agent = Join-Path $env:LOCALAPPDATA "GamingMode\GamingMode.exe"
    if (Test-Path $agent) {
        Start-Process -FilePath $agent -ArgumentList "agent" -WindowStyle Hidden -WorkingDirectory (Split-Path $agent) | Out-Null
    }
}

function Invoke-GamingModeSwitch {
    Ensure-GamingModeAgent

    for ($i = 0; $i -lt 20; $i++) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:47991/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
            Invoke-WebRequest -Uri "http://127.0.0.1:47991/mode/gaming/switch" -Method Post -UseBasicParsing -TimeoutSec 4 | Out-Null
            Write-PlayhubLog "Gaming Mode switch requested after Steam Controller wake."
            return
        } catch {
            Start-Sleep -Seconds 2
        }
    }

    Write-PlayhubLog "Gaming Mode agent did not respond after Steam Controller wake."
}

Write-PlayhubLog "Steam Controller wake helper started. ConfigureOnly=$ConfigureOnly EnableWake=$EnableWake"
Enable-SteamControllerWake

if ($ConfigureOnly) {
    Write-PlayhubLog "Configuration complete."
    exit 0
}

Start-Sleep -Seconds 8
$evidence = Get-WakeEvidence
Write-PlayhubLog ("Wake evidence: " + (($evidence -replace "\s+", " ").Trim()))

if (Test-SteamControllerWake -Evidence $evidence) {
    Invoke-GamingModeSwitch
}
