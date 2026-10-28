# Generates the app icon set and the five state-badged tray icons from
# packaging/icon-source.jpg. Requires ImageMagick 7 (`magick`) on PATH.

$ErrorActionPreference = 'Stop'

$magick = (Get-Command magick -ErrorAction SilentlyContinue).Source
if (-not $magick) {
    throw 'ImageMagick 7 is required; install it and put "magick" on PATH.'
}

$packaging = $PSScriptRoot
$assets = [System.IO.Path]::GetFullPath((Join-Path $packaging '..\src\OpenCodeVision.App\Assets'))
$source = Join-Path $packaging 'icon-source.jpg'
$windows = Join-Path $packaging 'windows'
$macos = Join-Path $packaging 'macos'
$linux = Join-Path $packaging 'linux'

if (-not (Test-Path $source)) {
    throw "missing source art: $source"
}

foreach ($directory in @($assets, $windows, $macos, $linux)) {
    New-Item -ItemType Directory -Force $directory | Out-Null
}

# ImageMagick has no ICNS writer, so the bundle icon is packed here.
$icnsSizes = @(
    [pscustomobject]@{ Size = 16; Type = 'icp4' }
    [pscustomobject]@{ Size = 32; Type = 'icp5' }
    [pscustomobject]@{ Size = 64; Type = 'icp6' }
    [pscustomobject]@{ Size = 128; Type = 'ic07' }
    [pscustomobject]@{ Size = 256; Type = 'ic08' }
    [pscustomobject]@{ Size = 512; Type = 'ic09' }
    [pscustomobject]@{ Size = 1024; Type = 'ic10' }
)

function Invoke-Magick {
    & $magick @args
    if ($LASTEXITCODE -ne 0) {
        throw "magick failed: $args"
    }
}

function Write-BigEndian {
    param([System.IO.BinaryWriter] $Writer, [uint32] $Value)

    $Writer.Write([byte](($Value -shr 24) -band 0xFF))
    $Writer.Write([byte](($Value -shr 16) -band 0xFF))
    $Writer.Write([byte](($Value -shr 8) -band 0xFF))
    $Writer.Write([byte]($Value -band 0xFF))
}

function Write-Icns {
    param([string] $Path, [object[]] $Entries)

    $length = 8 + (($Entries | Measure-Object -Property Length -Sum).Sum)
    $stream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('icns'))
        Write-BigEndian $writer ([uint32] $length)
        foreach ($entry in $Entries) {
            $writer.Write([System.Text.Encoding]::ASCII.GetBytes($entry.Type))
            Write-BigEndian $writer ([uint32] $entry.Length)
            $writer.Write([byte[]] $entry.Data)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ocv-icons-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null

try {
    # The art is a centred disc on a square canvas. Crop ~90% of the canvas so
    # the disc fills the icon, then clip to a circle to clear the corners.
    $size = (& $magick identify -format '%w %h' $source).Split(' ')
    $side = [int]([Math]::Min([int]$size[0], [int]$size[1]) * 0.902)
    $inset = [int]((([Math]::Min([int]$size[0], [int]$size[1])) - $side) / 2)

    Invoke-Magick $source -crop (('{0}x{0}+{1}+{1}' -f $side, $inset)) +repage -resize 1024x1024 (Join-Path $work 'square.png')
    Invoke-Magick -size 1024x1024 xc:none -fill white -draw 'circle 512,512 512,1017' (Join-Path $work 'mask.png')
    Invoke-Magick (Join-Path $work 'square.png') (Join-Path $work 'mask.png') -alpha off -compose CopyOpacity -composite (Join-Path $work 'master.png')

    $master = Join-Path $work 'master.png'

    # Application icon: window/taskbar PNG, Windows executable ICO, macOS
    # bundle ICNS and the Linux desktop-entry PNG.
    Invoke-Magick $master -resize 256x256 -strip (Join-Path $assets 'app.png')
    Invoke-Magick $master -define 'icon:auto-resize=256,128,64,48,32,24,16' (Join-Path $windows 'app.ico')
    Invoke-Magick $master -resize 256x256 -strip (Join-Path $linux 'opencode-vision.png')

    $entries = @()
    foreach ($icnsSize in $icnsSizes) {
        $png = Join-Path $work "icns-$($icnsSize.Size).png"
        Invoke-Magick $master -resize "$($icnsSize.Size)x$($icnsSize.Size)" -strip $png
        $entries += [pscustomobject]@{
            Type   = $icnsSize.Type
            Data   = [System.IO.File]::ReadAllBytes($png)
            Length = 8 + (Get-Item $png).Length
        }
    }

    Write-Icns (Join-Path $macos 'OpenCodeVision.icns') $entries

    # Tray icons: the logo at 64px with a state LED in the lower-right corner.
    # The logo is muted so the LED, not the four bars, carries the state colour.
    $states = [ordered]@{
        idle     = '#8A8A8A'
        working  = '#3FB950'
        approval = '#D29922'
        waiting  = '#388BFD'
        errored  = '#F85149'
    }

    $trayBase = Join-Path $work 'tray-base.png'
    Invoke-Magick $master -resize 64x64 -modulate '100,40,100' -strip $trayBase
    foreach ($state in $states.Keys) {
        Invoke-Magick $trayBase -stroke '#101010' -strokewidth 4 -fill $states[$state] `
            -draw 'circle 44,44 44,56' -strip (Join-Path $assets "tray-$state.png")
    }

    Write-Output "wrote app icons to $windows, $macos, $linux and $assets"
    Write-Output "wrote tray icons to $assets"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
