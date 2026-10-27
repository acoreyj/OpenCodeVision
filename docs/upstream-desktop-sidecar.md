# Upstream request: expose desktop-app sidecar sessions

This is a draft issue for the OpenCode desktop application. It is not a bug in
OpenCode Sticky; the desktop app simply has no external discovery point today.

## Summary

The desktop app runs its own in-process "sidecar" server with a per-launch
random password. External tools cannot discover or authenticate to it, so
sessions that live only in the desktop app are invisible to local session
monitors.

## What we observe

- Desktop app version: **1.19.4**, Windows 11 x64. CLI/background service:
  **2.2.0**.
- On launch the app forks `sidecar.js` and listens on `127.0.0.1:<random>`,
  e.g. `http://127.0.0.1:53785`.
- The sidecar uses HTTP Basic auth with username `opencode` and a password
  generated with `randomUUID()` in the main process. The password is sent to
  the sidecar over Electron IPC (`child.postMessage`), and to the renderer as
  part of the server-ready payload.
- No `service.json` (or equivalent URL/password file) is written for the
  sidecar. The app's `opencode` state directory contains only `locks`.
- The password is not present in the app's local storage, session storage, or
  logs.
- Desktop window state marks its tabs as `"server": "sidecar"`. Session IDs
  from those tabs return `404` from the background service that the CLI uses
  (`~/.local/state/opencode/service.json`, default port 28894), so the two
  stores are separate.
- Consequence: a local monitor that reads the background service sees CLI
  sessions and any sessions created against it, but not the desktop app's own
  sessions.

## Why it matters

The desktop app is the primary client for many users. A status widget or tray
monitor cannot show "which session is waiting on me" if the desktop app's
sessions are private. Reading process memory, sniffing loopback traffic, or
enabling a debug port are not acceptable integration paths.

## Requested change (either is sufficient)

1. **Publish a discovery file for the sidecar.** Write a file in a stable
   location (for example `<userData>/opencode/service.json`) with the same
   shape the CLI uses:

   ```json
   {
     "version": "1.19.4",
     "url": "http://127.0.0.1:53785",
     "username": "opencode",
     "password": "…",
     "pid": 12345
   }
   ```

   Requirements: created before the server accepts connections, replaced on
   every launch, `0600` permissions, removed on clean exit. A random password
   per launch is fine as long as the file is current.

2. **Reuse the shared background service.** Let the desktop app connect to the
   same local service the CLI uses, or allow configuring a server URL so its
   sessions live in the same store. This also avoids a second database for
   users who use both clients.

## Nice to have

- A machine-readable "current session" field would help status tools, but is
  not required: the session list plus `time.idle`/`time.viewed` is enough.
- If the sidecar must stay private, document that desktop sessions are not
  exposed to other local tools.

## Environment

- OS: Windows 11 (26100)
- Desktop app: 1.19.4 (Electron)
- CLI: 2.2.0
- Symptom: desktop tabs reference sessions that are absent from
  `GET /api/session/{id}` on the background service.

## Notes

No credentials, tokens, or session content are included here. The logs quoted
in the investigation contain only the sidecar URL.
