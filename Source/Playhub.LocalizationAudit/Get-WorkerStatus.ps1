$ErrorActionPreference = 'Stop'
Get-ChildItem (Join-Path $PSScriptRoot 'Workers') -Filter '*.state.json' | ForEach-Object {
    $state = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -DateKind String
    $process = Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
    $running = $process -and [Math]::Abs(($process.StartTime.ToUniversalTime() - ([DateTimeOffset]::Parse($state.StartedAt)).UtcDateTime).TotalSeconds) -lt 3
    $asset = Join-Path $PSScriptRoot "../Playhub/Assets/Localization/$($state.Language).json"
    [pscustomobject]@{
        Language = $state.Language
        PID = $state.ProcessId
        Running = [bool]$running
        ResultReady = (Test-Path -LiteralPath $state.Result)
        Entries = if (Test-Path -LiteralPath $asset) { (Get-Content -LiteralPath $asset -Raw | ConvertFrom-Json -AsHashtable).Count } else { 0 }
        Result = $state.Result
    }
}
