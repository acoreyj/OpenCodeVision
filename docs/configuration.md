# Configuration

There is no config file to hand-edit. Everything is set from the options
dialog; the app writes a small JSON file.

## Paths

| What | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Settings | `%APPDATA%\OpenCodeSticky\settings.json` | `~/.config/OpenCodeSticky/settings.json` | `~/Library/Application Support/OpenCodeSticky/settings.json` |
| Log | same folder, `sticky.log` | same folder | same folder |

`Environment.SpecialFolder.ApplicationData` resolves these; `%APPDATA%` is the
Windows form.

## Settings schema

```json
{
  "AlwaysOnTop": true,
  "FlashOnBlock": true,
  "WindowLeft": 3410,
  "WindowTop": 30,
  "ServiceEnabled": {
    "native": true,
    "wsl:Ubuntu": false
  }
}
```

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `AlwaysOnTop` | bool | `true` | Keep the window above other windows. |
| `FlashOnBlock` | bool | `true` | Pulse a row when it becomes blocked. |
| `WindowLeft` | number? | `null` | Last window X in screen pixels. |
| `WindowTop` | number? | `null` | Last window Y in screen pixels. |
| `ServiceEnabled` | object | `{}` | Per-service switch. Missing key means enabled. |

Writes are atomic: the app writes `settings.json.tmp`, then replaces the real
file. A corrupt or unreadable file falls back to defaults.

## Service keys

| Key | Service |
| --- | --- |
| `native` | The native opencode service on this machine |
| `wsl:<distro>` | A WSL distro, e.g. `wsl:Ubuntu` (Windows only) |

Newly discovered services start enabled. Disabling one stops polling and its
SSE stream immediately and removes its rows. Re-enabling refreshes it without
restarting the app.

## Options dialog

| Control | Effect |
| --- | --- |
| Always on top | Sets `Topmost` immediately and saves `AlwaysOnTop`. |
| Flash when a session starts blocking | Saves `FlashOnBlock`. Takes effect on the next block. |
| Service checkbox | Calls `SetServiceEnabledAsync`, saves the key, and refreshes. |
| Rescan | Re-runs discovery now; reports failure without crashing. |

Service rows show `name`, `version`, and `online` / `offline` / `off`; the URL is
in the tooltip.

## Resetting

Close the app, delete the folder above, and start again. Sessions are unaffected
because the app stores nothing about opencode itself.

## Log

`sticky.log` records startup, discovered services, offline transitions, and
state changes:

```
12:24:34.984 starting monitor
12:24:35.941 service native http://127.0.0.1:28894/ v2.2.0 enabled=True online=True
12:24:35.941 service wsl:Ubuntu http://127.0.0.1:49374/ v2.1.4 enabled=True online=True
12:24:35.942 rows=1
12:25:10.002 native ses_… Working -> WaitingForYou
```

It is truncated at startup when it exceeds 1 MB.
