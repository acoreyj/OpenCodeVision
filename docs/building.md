# Building

## Prerequisites

- .NET 8 SDK (this repo was built with 8.0.406).
- PowerShell 5.1 or later on Windows (scripts use Windows PowerShell syntax).
- WSL with a Linux distro for packaging (`wslpath` and `tar` are used to create
  tar.gz files with executable bits).
- opencode must have run at least once so its service file exists, if you want
  to see live data while developing.

## Everyday commands

```powershell
dotnet restore
dotnet build OpenCodeVision.sln -c Debug
dotnet test tests/OpenCodeVision.Core.Tests
dotnet run --project src/OpenCodeVision.App
```

The test suite is the fast gate: 89 tests, no network, no UI.

## Project layout

```
OpenCodeVision.sln
src/OpenCodeVision.Core/      net8.0 class library
src/OpenCodeVision.App/       net8.0 Avalonia app
tests/OpenCodeVision.Core.Tests/  xUnit
packaging/                    desktop entry, Info.plist, icon generator
scripts/package.ps1           publish + archive all four RIDs
artifacts/                    build output, git-ignored
```

## Publish one runtime

```powershell
dotnet publish src/OpenCodeVision.App -c Release -r win-x64 -o artifacts/win-x64
```

Publish properties (`SelfContained`, `PublishSingleFile`,
`IncludeNativeLibrariesForSelfExtract`, `EnableCompressionInSingleFile`) are
applied when a `RuntimeIdentifier` is set, so a plain `dotnet build` stays
framework-dependent and fast.

## Package everything

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

It publishes and packages:

| RID | Output |
| --- | --- |
| `win-x64` | `artifacts/OpenCodeVision-win-x64.zip` |
| `linux-x64` | `artifacts/OpenCodeVision-linux-x64.tar.gz` |
| `osx-x64` | `artifacts/OpenCodeVision-macos-x64.tar.gz` |
| `osx-arm64` | `artifacts/OpenCodeVision-macos-arm64.tar.gz` |

Packaging details:

- Windows zip contains the exe and README.
- Linux tar contains the binary, `.desktop` entry, `opencode-vision.png`,
  README; `chmod +x` is applied through WSL.
- macOS tar contains `OpenCodeVision.app` (executable + three dylibs +
  `Info.plist` + `Resources/OpenCodeVision.icns`) and the README. The script
  asserts the bundle contains every file from the publish directory.
- macOS signing is not performed; see [Platforms](platforms.md).

## Icons

`packaging/make-icons.ps1` needs ImageMagick 7 (`magick` on `PATH`) and reads
`packaging/icon-source.jpg` — the disc artwork centred on a square canvas. It
crops and circular-masks the art, then writes:

- `src/OpenCodeVision.App/Assets/app.png` — window and taskbar icon (256 px).
- `packaging/windows/app.ico` — embedded in the Windows executable through
  `ApplicationIcon`.
- `packaging/macos/OpenCodeVision.icns` — referenced by `CFBundleIconFile`;
  ImageMagick has no ICNS writer, so the script packs the PNG sizes itself.
- `packaging/linux/opencode-vision.png` — installed alongside the desktop entry.
- `src/OpenCodeVision.App/Assets/tray-<state>.png` — the logo at 64 px, muted,
  with a state-coloured LED in the lower-right corner (`idle`, `working`,
  `approval`, `waiting`, `errored`).

Run it only when the artwork or the palette changes, then commit the outputs.

## Version pin

Avalonia is pinned to **11.3.22** in the app csproj. Changing it is a deliberate
migration, not routine maintenance.

## Common build failures

| Symptom | Cause / fix |
| --- | --- |
| `NETSDK1031` self-contained without RID | Pass `-r <rid>`; the props are conditional. |
| Avalonia package restore fails | Check network/proxy; versions are pinned. |
| `AVLN3001` reappears | The warning suppression in the csproj was removed; windows take DI constructors and are created in `App`. |
| Packaging cannot delete `artifacts/win-x64` | The published app is still running. Stop `OpenCodeVision.exe`. |
| Tar creation fails | WSL or `tar` is unavailable; install a distro or create the archives another way. |
