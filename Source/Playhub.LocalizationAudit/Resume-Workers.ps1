param([Parameter(Mandatory)][string[]]$Languages, [string]$Brief = 'ExistingQualityBrief.txt')
$ErrorActionPreference = 'Stop'
$workers = Join-Path $PSScriptRoot 'Workers'
$codex = (Get-Command codex -ErrorAction Stop).Source
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $Brief))
foreach ($language in $Languages) {
    $statePath = Join-Path $workers "$language.state.json"
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -DateKind String
    $existing = Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
    if ($existing -and [Math]::Abs(($existing.StartTime.ToUniversalTime() - ([DateTimeOffset]::Parse($state.StartedAt)).UtcDateTime).TotalSeconds) -lt 3) {
        throw "Worker still running: $language"
    }
    $started = Get-Content -LiteralPath (Join-Path $workers "$language.events.jsonl") | ForEach-Object {
        $event = $_ | ConvertFrom-Json
        if ($event.type -eq 'thread.started') { $event }
    } | Select-Object -First 1
    if (-not $started.thread_id) { throw "Worker session not found: $language" }
    $phase = [DateTimeOffset]::Now.ToUnixTimeSeconds()
    $promptPath = Join-Path $workers "$language.$phase.prompt.txt"
    [IO.File]::WriteAllText($promptPath, $template.Replace('LANG_KEY', $language), [Text.UTF8Encoding]::new($false))
    $result = Join-Path $workers "$language.$phase.result.txt"
    $arguments = "exec resume $($started.thread_id) -c approval_policy=never --json -o `"$result`" -"
    $process = Start-Process -FilePath $codex -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardInput $promptPath -RedirectStandardOutput (Join-Path $workers "$language.$phase.events.jsonl") `
        -RedirectStandardError (Join-Path $workers "$language.$phase.stderr.log")
    $state.ProcessId = $process.Id
    $state.StartedAt = [DateTimeOffset]::Now.ToString('O')
    $state.Result = $result
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $state | ConvertTo-Json -Compress
}
