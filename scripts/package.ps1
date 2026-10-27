param(
    [string[]] $Rids = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/OpenCodeVision.App'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null

function ToWsl([string] $path) {
    (wsl.exe -e wslpath -a $path).Trim()
}

foreach ($rid in $Rids) {
    $output = Join-Path $artifacts $rid
    if (Test-Path $output) {
        Remove-Item -Recurse -Force $output
    }

    Write-Output "publishing $rid"
    dotnet publish $project -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $output

    if ($LASTEXITCODE -ne 0) {
        throw "publish failed for $rid"
    }
}

# Windows
if (Test-Path (Join-Path $root 'README.md')) {
    Copy-Item (Join-Path $root 'README.md') (Join-Path $artifacts 'win-x64/README.md')
}
Compress-Archive -Path (Join-Path $artifacts 'win-x64/*') `
    -DestinationPath (Join-Path $artifacts 'OpenCodeVision-win-x64.zip') -Force

# Linux
$linuxStage = Join-Path $artifacts 'linux-stage'
if (Test-Path $linuxStage) { Remove-Item -Recurse -Force $linuxStage }
New-Item -ItemType Directory -Force $linuxStage | Out-Null
Copy-Item (Join-Path $artifacts 'linux-x64/OpenCodeVision') $linuxStage
Copy-Item (Join-Path $root 'packaging/linux/opencode-vision.desktop') $linuxStage
if (Test-Path (Join-Path $root 'README.md')) {
    Copy-Item (Join-Path $root 'README.md') $linuxStage
}

$wslStage = ToWsl $linuxStage
$wslArchive = ToWsl (Join-Path $artifacts 'OpenCodeVision-linux-x64.tar.gz')
wsl.exe -e bash -lc "chmod +x '$wslStage/OpenCodeVision' && tar -czf '$wslArchive' -C '$wslStage' ."

# macOS
foreach ($arch in @('x64', 'arm64')) {
    $rid = "osx-$arch"
    $stage = Join-Path $artifacts "$rid-stage"
    $contents = Join-Path $stage 'OpenCodeVision.app/Contents'
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force (Join-Path $contents 'MacOS') | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $contents 'Resources') | Out-Null

    Copy-Item (Join-Path $artifacts "$rid/OpenCodeVision") (Join-Path $contents 'MacOS/OpenCodeVision')
    Copy-Item (Join-Path $root 'packaging/macos/Info.plist') (Join-Path $contents 'Info.plist')
    Copy-Item (Join-Path $root 'src/OpenCodeVision.App/Assets/tray-idle.png') (Join-Path $contents 'Resources/app.png')

    $published = Get-ChildItem (Join-Path $artifacts $rid) -File
    foreach ($file in $published) {
        Copy-Item $file.FullName (Join-Path $contents 'MacOS')
    }

    $staged = Get-ChildItem (Join-Path $contents 'MacOS') -File
    if ($staged.Count -ne $published.Count) {
        throw "macOS bundle for $rid is missing publish files ($($staged.Count) of $($published.Count))"
    }

    if (Test-Path (Join-Path $root 'README.md')) {
        Copy-Item (Join-Path $root 'README.md') $stage
    }

    $wslStage = ToWsl $stage
    $wslArchive = ToWsl (Join-Path $artifacts "OpenCodeVision-macos-$arch.tar.gz")
    wsl.exe -e bash -lc "chmod +x '$wslStage/OpenCodeVision.app/Contents/MacOS/OpenCodeVision' && tar -czf '$wslArchive' -C '$wslStage' OpenCodeVision.app README.md"
}

Get-ChildItem $artifacts -File | Select-Object Name, Length | Format-Table -AutoSize
