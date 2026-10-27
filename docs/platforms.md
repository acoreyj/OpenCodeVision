# Platforms

## Windows

- **Discovery**: native service file, plus every WSL distro's service file.
- **Window**: frameless, fixed 320 px wide, height follows the rows, 60% of the
  screen maximum. Drag by the title text.
- **Tray**: reliable. Close hides to the tray; Quit exits cleanly.
- **Topmost**: works as expected.
- **Packaging**: self-contained single `.exe` in a zip. Unsigned, so SmartScreen
  may prompt on first run.

## Desktop app (1.x)

The desktop app runs its own sidecar with a per-launch random password and does
not publish a service file. Its own sessions are not readable by this app; see
[the upstream request](upstream-desktop-sidecar.md). Sessions created through
the shared background service remain visible everywhere.

## WSL (Windows only)
- Distros are listed with `wsl.exe -l -q`, which prints UTF-16LE without a BOM.
- Each distro's service file is read with
  `wsl.exe -d <distro> -e sh -lc 'cat "$HOME/.local/state/opencode/service.json"'`.
- The URL inside the file is loopback. WSL2 normally forwards it to Windows, so
  `127.0.0.1:<port>` works.
- If forwarding does not work, discovery also records the distro IP from
  `wsl.exe -d <distro> -e hostname -I` and the monitor retries the alternate
  address once.
- WSL probes are skipped on Linux and macOS; `ServiceDiscovery` receives a null
  gateway there.

## Linux

- **Discovery**: native service only.
- **Window/tray**: Avalonia 11.3.22, Fluent dark theme. Tray support depends on
  the desktop (StatusNotifier/AppIndicator). If the tray is missing, the app
  still works.
- **Close behavior**: Linux never hides the window. Close quits, `Esc`
  minimizes, and the window stays in the taskbar while open. This avoids the
  trap of a hidden window with no tray to restore it.
- **Always-on-top**: honored by most X11 window managers; Wayland compositors
  may ignore it.
- **Runtime libraries**: needs the usual X11 stack (`libX11`, `libICE`,
  `libSM`) and fontconfig. A headless WSL image has none of these and stops at
  `libICE` before opening a window.
- **Packaging**: single ELF x64 binary plus `opencode-sticky.desktop` in a
  tar.gz. The desktop entry assumes the binary is on `PATH`; extract and add it
  to `PATH`, or edit `Exec=` to an absolute path.

## macOS

- **Discovery**: native service only.
- **Window/tray**: Avalonia with `NSStatusItem` for the tray. Close hides to the
  tray.
- **Packaging**: `OpenCodeSticky.app` with `Contents/MacOS/OpenCodeSticky`, the
  three native dylibs (Avalonia, HarfBuzz, Skia), `Info.plist`, and an app PNG.
  Shipped as `macos-x64` and `macos-arm64` tarballs.
- **Signing**: the bundle is built on Windows and is unsigned. Apple Silicon
  refuses unsigned bundles, so run this once on the Mac:

  ```bash
  codesign --force --deep --sign - OpenCodeSticky.app
  ```

  If Gatekeeper still blocks it, `xattr -dr com.apple.quarantine OpenCodeSticky.app`.
- **Runtime verification**: none was possible on the build machine.

## Verification matrix

| Platform | Verified |
| --- | --- |
| Windows 11 x64 | Full runtime: discovery of native 2.2.0 + Ubuntu WSL 2.1.4, row updates, options toggles, close-to-tray. |
| Linux x64 | Build only. ELF binary launches; headless WSL stops at missing `libICE`. |
| macOS x64 / arm64 | Package only. Correct Mach-O architectures, dylibs present, unsigned. |

## Cross-publishing

All four builds are produced from Windows with `dotnet publish -r <rid>`.
Avalonia's `Avalonia.Desktop` package brings the per-RID native assets.
Packaging uses WSL only to preserve executable bits and create tar.gz archives.
