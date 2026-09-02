param([switch]$AllowPublicGoogleTrial)

$ErrorActionPreference = 'Stop'
if (-not $AllowPublicGoogleTrial) {
    throw 'Explicit -AllowPublicGoogleTrial is required. This sends only the fixed public GitHub v1.2.1 release body to Google.'
}

# Experimental consumer endpoint, NOT the supported Google Cloud Translation API.
# No credentials, cookies, redirects, retries, billing setup, or arbitrary input.
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$handler.UseDefaultCredentials = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(12)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('Playhub-Public-ReleaseNotes-Trial/1.0')
try {
    $releaseResponse = $client.GetAsync('https://api.github.com/repos/LoZazaMastro/Playhub/releases/tags/v1.2.1').GetAwaiter().GetResult()
    try {
        $releaseResponse.EnsureSuccessStatusCode() | Out-Null
        $release = $releaseResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    }
    finally { $releaseResponse.Dispose() }
    if ($release.tag_name -cne 'v1.2.1' -or
        $release.html_url -cne 'https://github.com/LoZazaMastro/Playhub/releases/tag/v1.2.1' -or
        [string]::IsNullOrWhiteSpace($release.body) -or $release.body.Length -gt 5000) {
        throw 'The expected public release could not be verified; nothing sent to Google.'
    }

    $uri = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=it&dt=t&q=' + [Uri]::EscapeDataString($release.body)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $response = $client.GetAsync($uri).GetAwaiter().GetResult()
    try {
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $translated = $null
        if ($response.IsSuccessStatusCode) {
            $json = $body | ConvertFrom-Json -NoEnumerate
            $translated = ($json[0] | ForEach-Object { $_[0] }) -join ''
        }
        [ordered]@{
            timestamp_utc = [DateTime]::UtcNow.ToString('o')
            source = $release.html_url
            source_characters = $release.body.Length
            endpoint = 'https://translate.googleapis.com/translate_a/single (undocumented consumer endpoint)'
            google_requests = 1
            http_status = [int]$response.StatusCode
            elapsed_ms = $clock.ElapsedMilliseconds
            translation = $translated
            basic_markdown_probe_passed = if ($translated) {
                (($release.body -split '\r?\n' | Where-Object { $_ -match '^##|^- ' }).Count -eq
                 ($translated -split '\r?\n' | Where-Object { $_ -match '^##|^- ' }).Count) -and
                $translated.Contains('`Playhub-Setup-1.2.1.exe`')
            } else { $null }
            supported_api = $false
        } | ConvertTo-Json -Depth 6
    }
    finally { $response.Dispose() }
}
finally { $client.Dispose() }
