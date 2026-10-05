$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$assets = Join-Path $repo 'Source\Playhub\Assets\GamingMode'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('Playhub-helper-idle-' + [Guid]::NewGuid().ToString('N'))
$shell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$oldAppData = $env:APPDATA
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
$checks = 0
function Assert-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:checks++
    Write-Output "PASS $Name"
}
function Start-Fixture([string]$Path) {
    $p = Start-Process -FilePath $shell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Path + '"') -WindowStyle Hidden -PassThru
    $children.Add($p)
    return $p
}
function Wait-File([string]$Path, [Diagnostics.Process]$Process) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $Path)) {
        if ($Process.HasExited -or $clock.Elapsed.TotalSeconds -gt 12) { throw "Fixture startup failed: $Path" }
        Start-Sleep -Milliseconds 40
    }
}
function Request-Focus([int]$Port, [string]$Request = 'GET /health HTTP/1.1') {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect([Net.IPAddress]::Loopback, $Port)
        $client.ReceiveTimeout = 2000
        $stream = $client.GetStream()
        $payload = [Text.Encoding]::ASCII.GetBytes($Request + "`r`nHost: 127.0.0.1`r`nConnection: close`r`n`r`n")
        $stream.Write($payload, 0, $payload.Length)
        $reader = [IO.StreamReader]::new($stream)
        return $reader.ReadToEnd()
    } finally { $client.Close() }
}
try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $env:APPDATA = Join-Path $fixture 'roaming'
    $markerDirectory = Join-Path $env:APPDATA 'GamingMode'
    New-Item -ItemType Directory -Path $markerDirectory -Force | Out-Null
    $marker = Join-Path $markerDirectory 'disabled-by-user'
    $namespace = 'Local\Playhub.GamingMode.Fixture.' + [Guid]::NewGuid().ToString('N') + '.'
    $sources = @{}
    foreach ($name in @('focus-rescue.ps1', 'xbox-gamebar.ps1')) {
        $source = [IO.File]::ReadAllText((Join-Path $assets $name))
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
        Assert-Check ($errors.Count -eq 0) "$name production syntax"
        $mutex = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Enter-GamingModeHelper'}, $true)
        Assert-Check ($mutex.Extent.Text.Contains('WindowsIdentity]::GetCurrent().User.Value') -and $mutex.Extent.Text.Contains('Local\Playhub.GamingMode.')) "$name mutex is scoped by native session namespace and exact user SID"
        Assert-Check ($source.IndexOf('if (Test-GamingModeOptOut) { return }') -lt $source.IndexOf('$helperMutex = Enter-GamingModeHelper')) "$name opt-out precedes mutex acquisition"
        $sources[$name] = @{Text=$source;Ast=$ast;Mutex=$mutex.Extent.Text}
    }
    Assert-Check ($sources['focus-rescue.ps1'].Mutex -eq $sources['xbox-gamebar.ps1'].Mutex) 'Both helpers use the same reviewed mutex ownership implementation'

    # Real processes execute the production guard. Only the namespace is isolated.
    $guard = $sources['xbox-gamebar.ps1'].Mutex.Replace('Local\Playhub.GamingMode.', $namespace)
    $ready = Join-Path $fixture 'guard-ready'
    $guardPath = Join-Path $fixture 'guard.ps1'
    $harness = $guard + "`r`n`$m = Enter-GamingModeHelper 'XboxGameBar'`r`nif (`$null -eq `$m) { exit 0 }`r`ntry { [IO.File]::WriteAllText('$ready', 'owned'); Start-Sleep -Seconds 60 } finally { try { `$m.ReleaseMutex() } finally { `$m.Dispose() } }`r`n"
    [IO.File]::WriteAllText($guardPath, $harness)
    $owner = Start-Fixture $guardPath
    Wait-File $ready $owner
    $duplicate = Start-Fixture $guardPath
    Assert-Check ($duplicate.WaitForExit(4000) -and $duplicate.ExitCode -eq 0 -and -not $owner.HasExited) 'Same-user same-session duplicate exits without replacing its owner'
    $differentReady = Join-Path $fixture 'different-ready'
    $differentPath = Join-Path $fixture 'different.ps1'
    [IO.File]::WriteAllText($differentPath, $harness.Replace("'XboxGameBar'", "'FocusRescue'").Replace($ready, $differentReady))
    $different = Start-Fixture $differentPath
    Wait-File $differentReady $different
    Assert-Check (-not $different.HasExited) 'Distinct helper roles coexist in the same user session'

    # Preserve a handle to the mutex so owner death is a real abandoned case.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $kept = [Threading.Mutex]::new($false, ($namespace + 'XboxGameBar.' + $sid))
    try {
        $owner.Kill(); [void]$owner.WaitForExit(4000)
        . ([scriptblock]::Create($guard))
        $recovered = Enter-GamingModeHelper 'XboxGameBar'
        Assert-Check ($null -ne $recovered) 'Production guard recovers a genuinely abandoned mutex'
        try { $recovered.ReleaseMutex() } finally { $recovered.Dispose() }
        $again = Enter-GamingModeHelper 'XboxGameBar'
        Assert-Check ($null -ne $again) 'Mutex can be acquired again after normal release'
        try { $again.ReleaseMutex() } finally { $again.Dispose() }
    } finally { $kept.Dispose() }

    # Exercise the complete focus server on an ephemeral isolated port. No
    # focus mutation routes are called; native Win32 methods remain untouched.
    $reservation = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $reservation.Start(); $port = $reservation.LocalEndpoint.Port; $reservation.Stop()
    $focusPath = Join-Path $fixture 'focus-rescue.ps1'
    $focusSource = $sources['focus-rescue.ps1'].Text.Replace('Local\Playhub.GamingMode.', $namespace).Replace('$port = 47992', ('$port = ' + $port))
    [IO.File]::WriteAllText($focusPath, $focusSource, [Text.UTF8Encoding]::new($false))
    # The earlier role-only harness must release its fixture namespace first.
    $different.Kill(); [void]$different.WaitForExit(4000)
    $focus = Start-Fixture $focusPath
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        try { $response = Request-Focus $port; break } catch { Start-Sleep -Milliseconds 40 }
        if ($focus.HasExited -or $clock.Elapsed.TotalSeconds -gt 12) { throw 'Isolated focus server did not start' }
    } while ($true)
    Assert-Check ($response.StartsWith('HTTP/1.1 200') -and $response.Contains('playhub-focus-rescue')) 'Full production focus server responds to actual loopback health request'
    $secondFocus = Start-Fixture $focusPath
    Assert-Check ($secondFocus.WaitForExit(4000) -and $secondFocus.ExitCode -eq 0 -and -not $focus.HasExited) 'Full focus script rejects duplicate before binding or compiling native code'
    $focus.Refresh(); $cpuBefore = $focus.TotalProcessorTime.TotalSeconds
    $idleClock = [Diagnostics.Stopwatch]::StartNew(); Start-Sleep -Seconds 8
    $focus.Refresh(); $idleCpu = $focus.TotalProcessorTime.TotalSeconds - $cpuBefore
    $idleSeconds = $idleClock.Elapsed.TotalSeconds
    $idlePercent = 100 * $idleCpu / $idleSeconds
    $latencies = @()
    foreach ($i in 1..12) {
        Start-Sleep -Milliseconds 35
        $requestClock = [Diagnostics.Stopwatch]::StartNew()
        $response = Request-Focus $port
        $latencies += $requestClock.Elapsed.TotalMilliseconds
        Assert-Check ($response.StartsWith('HTTP/1.1 200')) "Real health request $i succeeds during blocking socket wait"
    }
    Assert-Check (($latencies | Measure-Object -Maximum).Maximum -lt 300) 'Socket wait wakes for requests without waiting for its one-second timeout'
    Assert-Check ((Request-Focus $port 'OPTIONS /health HTTP/1.1').StartsWith('HTTP/1.1 204')) 'Actual OPTIONS response preserved'
    Assert-Check ((Request-Focus $port 'GET /unknown HTTP/1.1').StartsWith('HTTP/1.1 404')) 'Actual unknown route response preserved'
    $quitClock = [Diagnostics.Stopwatch]::StartNew()
    [IO.File]::WriteAllText($marker, 'fixture opt-out')
    Assert-Check ($focus.WaitForExit(6500) -and $focus.ExitCode -eq 0) 'Full production server exits on opt-out with bounded watchdog latency'
    $exitMilliseconds = $quitClock.Elapsed.TotalMilliseconds
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    try { $listener.Start(); Assert-Check $true 'Focus socket is released after shutdown' } finally { $listener.Stop() }
    Remove-Item -LiteralPath $marker

    # Full Game Bar lifecycle: only its registry-writing function is replaced
    # by a Temp-file spy. No registry or controller setting is changed.
    $barSource = $sources['xbox-gamebar.ps1'].Text.Replace('Local\Playhub.GamingMode.', $namespace)
    $setBar = $sources['xbox-gamebar.ps1'].Ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Set-GameBar'}, $true)
    $getBar = $sources['xbox-gamebar.ps1'].Ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-GameBarState'}, $true)
    $barWrites = Join-Path $fixture 'bar-writes'
    $barSource = $barSource.Replace($setBar.Extent.Text, "function Set-GameBar([int]`$value) { `$script:fixtureBarState = `$value; Add-Content -LiteralPath '$barWrites' -Value `$value; return `$true }")
    $barSource = $barSource.Replace($getBar.Extent.Text, 'function Get-GameBarState { return [int]$script:fixtureBarState }')
    $barPath = Join-Path $fixture 'xbox-gamebar.ps1'
    [IO.File]::WriteAllText($barPath, $barSource, [Text.UTF8Encoding]::new($false))
    $bar = Start-Fixture $barPath; Wait-File $barWrites $bar
    $before = [IO.File]::ReadAllText($barWrites)
    $secondBar = Start-Fixture $barPath
    Assert-Check ($secondBar.WaitForExit(4000) -and $secondBar.ExitCode -eq 0) 'Full Game Bar script rejects duplicate before startup settings writes'
    Assert-Check ([IO.File]::ReadAllText($barWrites) -eq $before) 'Duplicate Game Bar performs no settings write'
    [IO.File]::WriteAllText($marker, 'fixture opt-out')
    Assert-Check ($bar.WaitForExit(2500) -and $bar.ExitCode -eq 0) 'Full Game Bar script exits on opt-out'
    Assert-Check ((Get-Content -LiteralPath $barWrites | Select-Object -Last 1) -eq '0') 'Game Bar opt-out preserves disabling of the controller trigger'
    # Link the unchanged Steam shutdown grace to the production loop without
    # stopping actual Steam. Time and process availability alone are simulated.
    $focusLoop = $sources['focus-rescue.ps1'].Ast.Find({param($node) $node -is [Management.Automation.Language.WhileStatementAst] -and $node.Extent.Text.StartsWith('while ($true)')}, $true)
    & {
        $script:watchdogTick = 0; $script:watchdogCalls = 0; $script:watchdogLogs = @()
        $baseTime = [DateTime]::Now
        $lastSteamCheck = $baseTime.AddSeconds(-6)
        $steamSeen = $false; $steamGoneSince = $null; $listener = $null
        function Get-Date { return $baseTime.AddSeconds($script:watchdogTick) }
        function Get-Process {
            $script:watchdogCalls++
            if ($script:watchdogCalls -eq 1) { return [pscustomobject]@{Id=1} }
        }
        function Test-GamingModeOptOut { return $false }
        function Wait-FocusRequest {
            $script:watchdogTick += 5
            if ($script:watchdogTick -gt 60) { throw 'Steam exit grace did not terminate' }
            return $false
        }
        function Write-Log([string]$Message) { $script:watchdogLogs += $Message }
        . ([scriptblock]::Create($focusLoop.Extent.Text))
        Assert-Check ($steamSeen -and ($script:watchdogLogs -join ' ').Contains('Steam chiuso')) 'Production loop retains shutdown after Steam was previously observed'
        Assert-Check (($baseTime.AddSeconds($script:watchdogTick) - $steamGoneSince).TotalSeconds -eq 30) 'Production loop preserves the exact 30-second Steam-exit grace'
        Assert-Check ($script:watchdogCalls -eq 8) 'Steam watchdog remains on its existing five-second cadence'
    }
    [pscustomobject]@{Checks=$checks;FocusIdleSampleSeconds=$idleSeconds;FocusIdleCpuSeconds=$idleCpu;FocusIdleCpuPercentOneLogicalCore=$idlePercent;HealthRequestMaxMilliseconds=($latencies|Measure-Object -Maximum).Maximum;OptOutExitMilliseconds=$exitMilliseconds;Scope='Disposable isolated processes, ports, mutex names and APPDATA. Game Bar writes redirected to a Temp-file spy. No live UI, process or registry mutation.'} | ConvertTo-Json
} finally {
    $env:APPDATA = $oldAppData
    foreach ($process in $children) { if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(3000) }; $process.Dispose() }
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('Playhub-helper-idle-')) { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue }
}
