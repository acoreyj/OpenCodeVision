Add-Type -AssemblyName System.Drawing

$root = Join-Path $PSScriptRoot "..\src\OpenCodeVision.App\Assets"
New-Item -ItemType Directory -Force $root | Out-Null

$colors = @{
    idle     = '#8A8A8A'
    working  = '#3FB950'
    approval = '#D29922'
    waiting  = '#388BFD'
    errored  = '#F85149'
}

foreach ($name in $colors.Keys) {
    $bitmap = New-Object System.Drawing.Bitmap 64, 64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1F1F1F'))
    $graphics.FillEllipse($dark, 2, 2, 60, 60)

    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($colors[$name]))
    $graphics.FillEllipse($accent, 20, 20, 24, 24)

    $bitmap.Save((Join-Path $root "tray-$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Output "wrote tray icons to $root"
