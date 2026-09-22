$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$output = Join-Path $root 'src\assets\history'
$base = 'https://www.konami.com/mg/archive/mgs_tlc/_img/hisyory/'

$sets = [ordered]@{
    'kojima-metal-gear-msx.jpg' = @('mg-pic01.jpg', 'mg-pic02.jpg', 'mg-pic03.jpg', 'mg-pic04.jpg')
    'kojima-mgs-playstation.jpg' = @('mgs-pic01.jpg', 'mgs-pic02.jpg', 'mgs-pic03.jpg', 'mgs-pic04.jpg')
    'kojima-mgs2-information.jpg' = @('mgs2-pic01.jpg', 'mgs2-pic02.jpg', 'mgs2-pic03.jpg', 'mgs2-pic04.jpg')
    'kojima-mgs3-legacy.jpg' = @('mgs3-pic01.jpg', 'mgs3-pic02.jpg', 'mgs3-pic03.jpg', 'mgs3-pic04.jpg')
}

function Get-RemoteImage([string]$url) {
    $client = [System.Net.WebClient]::new()
    $client.Headers.Add('User-Agent', 'Playhub editorial asset builder')
    try {
        $bytes = $client.DownloadData($url)
        $stream = [System.IO.MemoryStream]::new($bytes)
        try {
            $source = [System.Drawing.Image]::FromStream($stream)
            try { return [System.Drawing.Bitmap]::new($source) } finally { $source.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $client.Dispose() }
}

function Draw-Cover([System.Drawing.Graphics]$graphics, [System.Drawing.Image]$image, [System.Drawing.Rectangle]$target) {
    $sourceRatio = $image.Width / $image.Height
    $targetRatio = $target.Width / $target.Height
    if ($sourceRatio -gt $targetRatio) {
        $sourceHeight = $image.Height
        $sourceWidth = [int]($sourceHeight * $targetRatio)
        $sourceX = [int](($image.Width - $sourceWidth) / 2)
        $sourceY = 0
    } else {
        $sourceWidth = $image.Width
        $sourceHeight = [int]($sourceWidth / $targetRatio)
        $sourceX = 0
        $sourceY = [int](($image.Height - $sourceHeight) / 2)
    }
    $source = [System.Drawing.Rectangle]::new($sourceX, $sourceY, $sourceWidth, $sourceHeight)
    $graphics.DrawImage($image, $target, $source, [System.Drawing.GraphicsUnit]::Pixel)
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach ($entry in $sets.GetEnumerator()) {
    $canvas = [System.Drawing.Bitmap]::new(1280, 720)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        for ($index = 0; $index -lt 4; $index++) {
            $image = Get-RemoteImage ($base + $entry.Value[$index])
            try {
                $target = [System.Drawing.Rectangle]::new(($index % 2) * 640, [math]::Floor($index / 2) * 360, 640, 360)
                Draw-Cover $graphics $image $target
            } finally { $image.Dispose() }
        }
        $canvas.Save((Join-Path $output $entry.Key), [System.Drawing.Imaging.ImageFormat]::Jpeg)
    } finally {
        $graphics.Dispose()
        $canvas.Dispose()
    }
    Write-Output $entry.Key
}

$mgsv = Get-RemoteImage 'https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/287700/ss_8d3e8949591899054dc0ae7e56dc04ddab5ba7b5.1920x1080.jpg'
$canvas = [System.Drawing.Bitmap]::new(1280, 720)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
try {
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    Draw-Cover $graphics $mgsv ([System.Drawing.Rectangle]::new(0, 0, 1280, 720))
    $canvas.Save((Join-Path $output 'kojima-mgsv-open-world.jpg'), [System.Drawing.Imaging.ImageFormat]::Jpeg)
} finally {
    $graphics.Dispose()
    $canvas.Dispose()
    $mgsv.Dispose()
}
Write-Output 'kojima-mgsv-open-world.jpg'
