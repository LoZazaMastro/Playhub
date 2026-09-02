param(
    [string]$AppDirectory = "$PSScriptRoot\..\Playhub\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64",
    [switch]$Full,
    [switch]$Languages,
    [switch]$UpdateScroll
)
$ErrorActionPreference = 'Stop'
$AppDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
$output = Join-Path $AppDirectory 'ui-review'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReviewCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
$previous = $env:PLAYHUB_REVIEW_SCREENSHOTS_ONLY
$previousLanguages = $env:PLAYHUB_REVIEW_LANGUAGES_ONLY
$previousUpdateScroll = $env:PLAYHUB_REVIEW_UPDATE_SCROLL_ONLY
$env:PLAYHUB_REVIEW_UPDATE_SCROLL_ONLY = if ($UpdateScroll) { '1' } else { $null }
$env:PLAYHUB_REVIEW_LANGUAGES_ONLY = if ($Languages) { '1' } else { $null }
$env:PLAYHUB_REVIEW_SCREENSHOTS_ONLY = if ($Full) { $null } else { '1' }
$process = $null
try {
    $readyPath = Join-Path $output 'clock-ready.json'
    if (Test-Path -LiteralPath $readyPath) { Remove-Item -LiteralPath $readyPath }
    $process = Start-Process -FilePath (Join-Path $AppDirectory 'Playhub.exe') -WorkingDirectory $AppDirectory -WindowStyle Hidden -PassThru
    Write-Output "Isolated review process: $($process.Id)"
    $deadline = [DateTime]::UtcNow.AddSeconds($(if ($Full) { 300 } else { 100 }))
    $metadata = $null
    while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        $path = Join-Path $output 'clock-ready.json'
        if (Test-Path -LiteralPath $path) {
            try {
                $candidate = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                if ($candidate.ProcessId -eq $process.Id) { $metadata = $candidate; break }
            } catch { }
        }
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    }
    if ($metadata) {
        Write-Output "Countdown ready: process $($metadata.ProcessId), window $($metadata.WindowHandle)"
        $window = [IntPtr]$metadata.WindowHandle
        $oldDpi = [ReviewCapture]::SetThreadDpiAwarenessContext([IntPtr](-4))
        try {
            if ([ReviewCapture]::GetForegroundWindow() -ne $window) {
                [void][ReviewCapture]::SetForegroundWindow($window)
                Start-Sleep -Milliseconds 150
            }
            $rect = New-Object ReviewCapture+Rect
            $origin = New-Object ReviewCapture+Point
            if (![ReviewCapture]::GetClientRect($window, [ref]$rect)) { throw 'The review window is no longer available.' }
            [void][ReviewCapture]::ClientToScreen($window, [ref]$origin)
            $bitmap = [System.Drawing.Bitmap]::new($rect.Right, $rect.Bottom)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                if ([ReviewCapture]::GetForegroundWindow() -eq $window) {
                    $graphics.CopyFromScreen($origin.X, $origin.Y, 0, 0, $bitmap.Size)
                } else {
                    $dc = $graphics.GetHdc()
                    try { if (![ReviewCapture]::PrintWindow($window, $dc, 3)) { throw 'Cannot capture the isolated review window.' } }
                    finally { $graphics.ReleaseHdc($dc) }
                }
                $bitmap.Save((Join-Path $output 'clock-native.png'), [System.Drawing.Imaging.ImageFormat]::Png)
                foreach ($circle in $metadata.Circles | Where-Object { $_.Fraction -eq 1 }) {
                    $bounds = $circle.Bounds
                    $x = [int](($bounds.X + $bounds.Width / 2) * $metadata.Scale)
                    $y = [int](($bounds.Y + $bounds.Height * .7) * $metadata.Scale)
                    $center = $bitmap.GetPixel($x, $y)
                    $left = $bitmap.GetPixel($x - 2, $y)
                    $right = $bitmap.GetPixel($x + 2, $y)
                    if ($center.R -lt 100 -or [Math]::Abs($center.R - $left.R) -gt 12 -or [Math]::Abs($center.R - $right.R) -gt 12) {
                        throw "Countdown center seam or blank pixels at $($circle.Scale)x: $center / $left / $right"
                    }
                }
                Write-Output 'PASS native countdown pixels are nonblank with no vertical center seam at 1x and 4x.'
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        } finally { [void][ReviewCapture]::SetThreadDpiAwarenessContext($oldDpi) }
    }
    if (!$process.WaitForExit(60000)) { throw 'Isolated UI review did not finish.' }
    $result = Get-Content -LiteralPath (Join-Path $output 'results.json') -Raw | ConvertFrom-Json
    if ($result.processId -ne $process.Id) { throw 'This isolated process did not complete its UI review; rejecting stale results.' }
    [pscustomobject]@{ Passed = $result.passed; Checks = $result.checks.Count; Failures = $result.failures } | ConvertTo-Json -Depth 4
    if (!$result.passed) { exit 1 }
} finally {
    $env:PLAYHUB_REVIEW_SCREENSHOTS_ONLY = $previous
    $env:PLAYHUB_REVIEW_LANGUAGES_ONLY = $previousLanguages
    $env:PLAYHUB_REVIEW_UPDATE_SCROLL_ONLY = $previousUpdateScroll
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id }
}
