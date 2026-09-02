param([Parameter(Mandatory)][string[]]$Languages)
$ErrorActionPreference = 'Stop'
$names = @{ en='English'; es='Spanish'; fr='French'; de='German'; pt='Portuguese'; uk='Ukrainian'; zh='Simplified Chinese'; ja='Japanese'; ko='Korean'; hi='Hindi'; ru='Russian'; it='Italian source QA' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workers = Join-Path $PSScriptRoot 'Workers'
[IO.Directory]::CreateDirectory($workers) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $repo 'Source/Playhub/Assets/Localization')) | Out-Null
$codex = (Get-Command codex -ErrorAction Stop).Source
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorkerPrompt.txt'))
foreach ($language in $Languages) {
    if (-not $names.ContainsKey($language)) { throw "Unsupported worker language: $language" }
    $statePath = Join-Path $workers "$language.state.json"
    if (Test-Path -LiteralPath $statePath) { throw "Worker already dispatched: $language. Resume its existing session instead." }
    $promptPath = Join-Path $workers "$language.prompt.txt"
    $prompt = $template.Replace('LANG_NAME', $names[$language]).Replace('LANG_KEY', $language)
    [IO.File]::WriteAllText($promptPath, $prompt, [Text.UTF8Encoding]::new($false))
    $result = Join-Path $workers "$language.result.txt"
    $arguments = "exec -C `"$repo`" -s workspace-write -c approval_policy=never --json -o `"$result`" -"
    $process = Start-Process -FilePath $codex -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardInput $promptPath -RedirectStandardOutput (Join-Path $workers "$language.events.jsonl") `
        -RedirectStandardError (Join-Path $workers "$language.stderr.log")
    $state = @{ Language=$language; Name=$names[$language]; ProcessId=$process.Id; StartedAt=[DateTimeOffset]::Now.ToString('O'); Result=$result }
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $state | ConvertTo-Json -Compress
}
