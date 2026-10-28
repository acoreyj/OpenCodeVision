# OpenCode Vision

A small always-on-top window that shows every opencode session on your machine
at a glance. One row per session; the color tells you whether it is working,
waiting for you, or blocked.

> **Unofficial.** OpenCode Vision is an independent companion app. It is not
> affiliated with, endorsed by, or sponsored by the opencode project.
> "opencode" and related marks belong to their respective owners.

## Palette

| Color | Meaning |
| --- | --- |
| grey | idle / finished |
| green | working |
| amber | waiting for approval |
| blue | turn finished, not opened yet |
| red | last turn failed |

Rows are grouped by opencode service. Blocked sessions sort to the top and
keep the longest wait first. The remaining rows group by color (red, green,
grey) and stay in title order inside a color, so new messages do not reshuffle
the list. Only top-level sessions are shown; subagent sessions are hidden.

Each row shows how long the session has been open and how long ago it last
updated (`open 2h · updated 5m ago`). The right edge shows how long the row has
been in its current state. Hover a row for the exact local times.

## What it watches

- The native opencode service on Windows, Linux and macOS
  (`~/.local/state/opencode/service.json`, or
  `%USERPROFILE%\.local\state\opencode\service.json` on Windows).
- Every WSL distro that has a running opencode service, on Windows only.

It discovers services on startup and every 60 seconds. The options screen
lists each service with an on/off switch; choices are remembered. Nothing is
sent to opencode except authenticated read requests, the event stream, and an
explicit stop request from the row's hover control.

## Run

Requirements: opencode must have run at least once on the machine so its
service file exists. The published builds include the .NET runtime.

### Windows

Unzip `OpenCodeVision-win-x64.zip` and run `OpenCodeVision.exe`. The build is
unsigned, so SmartScreen may ask for confirmation the first time.

### Linux

```bash
tar -xzf OpenCodeVision-linux-x64.tar.gz
./OpenCodeVision
```

A desktop environment with X11 or Wayland is required, including the usual
X11 compatibility libraries (`libICE`, `libSM`). These are present on normal
desktop installs but absent from a headless WSL image. The tray icon depends
on the desktop's tray support; if it is unavailable the window still works and
`Quit` is done by closing the window.

The archive also contains `opencode-vision.desktop` and `opencode-vision.png`
for menu integration; copy the PNG to
`~/.local/share/icons/hicolor/256x256/apps/` and the desktop entry to
`~/.local/share/applications/`.

### macOS

```bash
tar -xzf OpenCodeVision-macos-arm64.tar.gz   # or -x64
open OpenCodeVision.app
```

The bundle is unsigned. On Apple Silicon, macOS will refuse to launch an
unsigned bundle, so ad-hoc sign it once before opening:

```bash
codesign --force --deep --sign - OpenCodeVision.app
open OpenCodeVision.app
```

If Gatekeeper still refuses it, run
`xattr -dr com.apple.quarantine OpenCodeVision.app` once. The bundle contains
the Avalonia, HarfBuzz and Skia native libraries next to the executable.

## Using it

- Gear: options (always on top, flash on block, per-service switches, rescan).
- Click a row: mark it viewed and open that session in your browser.
- Hover a row: a red `✕` button appears on the right. Click it once to arm
  `Stop?`/`Hide?`, then click again to confirm. A running session is
  interrupted; a finished one is hidden until its next update. Moving away or
  pressing `Esc` cancels the confirm.
- Close: hide to the tray on Windows and macOS. On Linux, where tray support
  varies, close quits the app and the window stays in the taskbar while open.
- Quit: from the tray menu where a tray is available.
- `Esc`: hide (Windows/Linux/macOS with tray) or minimize.

Flashing is a visible-window effect; when hidden, the tray icon shows the most
urgent state as a colored dot.

## Files

Settings and the diagnostics log live under the platform's application-data
folder:

- Windows: `%APPDATA%\OpenCodeVision\`
- Linux: `~/.config/OpenCodeVision/`
- macOS: `~/Library/Application Support/OpenCodeVision/`

`sticky.log` is truncated at startup when it exceeds 1 MB. `settings.json` is
written atomically.

## Building

```bash
dotnet test
powershell -File scripts/package.ps1
```

`scripts/package.ps1` publishes self-contained single-file builds for
`win-x64`, `linux-x64`, `osx-x64` and `osx-arm64`, then writes the archives to
`artifacts/`. Cross-publishing works from Windows; the Linux and macOS archives
are tarballs so executable bits survive.

## Documentation

- [Documentation index](docs/README.md) — every guide, with when to read it.
- [Architecture](docs/architecture.md) — modules, data flow, threading.
- [State model](docs/state-model.md) — colors, thresholds, blocked rules.
- [opencode API](docs/opencode-api.md) — endpoints, payloads, event stream.
- [Configuration](docs/configuration.md) — settings schema and file paths.
- [Platforms](docs/platforms.md) — Windows, WSL, Linux, macOS differences.
- [Building](docs/building.md) — build, test, publish and package.
- [Troubleshooting](docs/troubleshooting.md) — no rows, offline services, tray.
- [Development](docs/development.md) — TDD workflow and change checklists.
- [Decisions](docs/decisions.md) — why the app works this way, and the costs.
- [AGENTS.md](AGENTS.md) — entry point for coding agents.

## Verified

| Platform | Status |
| --- | --- |
| Windows 11 (x64) | Runtime-tested: native opencode 2.2.0 and Ubuntu WSL 2.1.4 both discovered; options, tray close, and stream toggle exercised. |
| Linux (x64) | Build-tested only. The ELF x64 binary launches; this machine's WSL is headless and has no X11 stack, so Avalonia stops at the missing `libICE` before opening a window. |
| macOS (x64 / arm64) | Packaged only. The `.app` includes the Avalonia, HarfBuzz and Skia dylibs; Mach-O architectures verified. Not launched on macOS hardware, and the bundle is unsigned (ad-hoc sign it as shown above). |

## Limits

- No desktop notifications or sounds. Color and optional flashing only.
- No remote machines; local services and local WSL distros only.
- The OpenCode desktop app (1.x) runs a private sidecar with a per-launch
  password. Sessions that live only in that sidecar are not visible to the
  sticky; see [the upstream request](docs/upstream-desktop-sidecar.md).
- Always-on-top and tray behavior depend on the Linux desktop environment.
- The app reads opencode's local API and can interrupt an active turn. It
  never edits session history, sends prompts, or touches the database.
