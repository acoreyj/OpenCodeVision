# Troubleshooting

Start with the log:

| Platform | Path |
| --- | --- |
| Windows | `%APPDATA%\OpenCodeSticky\sticky.log` |
| Linux | `~/.config/OpenCodeSticky/sticky.log` |
| macOS | `~/Library/Application Support/OpenCodeSticky/sticky.log` |

A healthy startup looks like:

```
starting monitor
service native http://127.0.0.1:28894/ v2.2.0 enabled=True online=True
rows=1
```

## No services listed

1. Confirm opencode has run and wrote its service file:

   ```powershell
   Test-Path "$env:USERPROFILE\.local\state\opencode\service.json"
   Get-Content "$env:USERPROFILE\.local\state\opencode\service.json" -Raw |
       ConvertFrom-Json | Select-Object url, version, pid
   ```

2. On Linux/macOS use `~/.local/state/opencode/service.json`.
3. If the file is missing, run `opencode` once or restart it.
4. Press **Rescan** in options; a missing file is not cached forever.

## Services listed but offline

Check the endpoint directly (this does not print the password):

```powershell
$s = Get-Content "$env:USERPROFILE\.local\state\opencode\service.json" -Raw | ConvertFrom-Json
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("opencode:$($s.password)"))
Invoke-RestMethod -Uri "$($s.url)/api/info" -Headers @{ Authorization = "Basic $b64" }
```

- `401`: the file is stale. Restart opencode so it rewrites URL/password, then
  Rescan.
- Connection refused: the service is not running on that port.
- Timeout: a hung process; restart opencode.

## No rows, but services are online

- Only top-level sessions are shown; subagent sessions are hidden by design.
- Only sessions active in the last 30 minutes appear, plus blocked ones.
- If opencode reports no recent sessions, the list is legitimately empty.
- Check the log for `rows=` and any state-change lines.

## WSL service missing

```powershell
wsl.exe -l -v
wsl.exe -d Ubuntu -e cat ~/.local/state/opencode/service.json
```

- The distro must be running (WSL2 keeps it alive for a while).
- If the file exists but the app cannot reach `127.0.0.1:<port>`, WSL
  localhost forwarding is blocked. Discovery stores a fallback address from
  `wsl.exe -d Ubuntu -e hostname -I`; the monitor retries it automatically.
- A stopped distro shows as offline; start it and rescan.

## Tray icon missing (Linux)

Avalonia's tray needs a StatusNotifier/AppIndicator host. Without one:

- the app still runs and the window stays in the taskbar;
- closing the window quits the app;
- `Esc` minimizes.

Install your desktop's AppIndicator extension if you want the tray.

## Window will not open (Linux)

Avalonia needs X11/Wayland libraries. A headless WSL image fails with
`libICE.so.6`. On a desktop install:

```bash
sudo apt install libice6 libsm6 libx11-6
```

## macOS refuses to open the app

The bundle is unsigned. On Apple Silicon:

```bash
codesign --force --deep --sign - OpenCodeSticky.app
xattr -dr com.apple.quarantine OpenCodeSticky.app
open OpenCodeSticky.app
```

If it still fails, run the executable from a terminal to see the error:

```bash
./OpenCodeSticky.app/Contents/MacOS/OpenCodeSticky
```

## Always-on-top does nothing

Wayland compositors may ignore the request. X11 window managers normally honor
it. The option is still saved.

## Flashing does not happen

- The toggle is off in options.
- The window is hidden; only the tray badge changes while hidden.
- The session was already blocked at startup; flashing starts on new blocks.

## Window is off-screen

The app clamps the saved position to a visible screen on startup. If the layout
changed dramatically, delete `settings.json` (see
[Configuration](configuration.md)) and restart.

## Two instances

The app does not enforce a single instance. Closing the tray icon of one does not
affect the other. Quit both from their tray menus before deleting settings.

## Nothing helps

1. Quit the app.
2. Rename `settings.json` to `settings.json.bak`.
3. Start the app and press Rescan.
4. Include the last 20 lines of `sticky.log` when reporting the problem.
