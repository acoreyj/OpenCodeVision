# OpenCode Vision — design

Date: 2026-10-27
Status: draft for approval

## Intent

A small always-on-top Windows window that answers one question at a glance:
*which of my opencode sessions is blocked on me right now?*

Sessions run both natively on Windows and inside WSL. They are easy to lose
track of once more than one is open. This app watches the opencode services
that are already running and paints each session's state as a color, so the
answer needs no reading.

## Goals

- Show every top-level opencode session from every opencode service on the
  machine — native on Windows, Linux or macOS, plus WSL distros on Windows —
  in one compact window.
- Show, per session: project folder, title, state color, time in current state.
- Update live from opencode's event stream; no scraping, no plugins, no
  changes to opencode.
- Discover running opencode services automatically and let the user switch
  each one on or off, with the choice remembered.
- Keep the window above other windows on demand, and flash quietly when a
  session starts blocking.
- Ship as one self-contained `.exe`; no installer, no runtime prerequisite.

## Non-goals

- Desktop notifications, sounds, or popups. Color and (optional) flashing only.
- Reading or rendering message content. Titles and state only.
- Approving permissions or sending prompts from the sticky.
- Remote machines. v1 covers this PC plus local WSL distros; the design should
  not block adding remote servers later.
- Multiple windows, one sticky per session, mobile, or web versions.
- Start-with-Windows, auto-update, installers.

## Anti-goals

- Stealing focus when the list updates.
- Flashing forever, or using CPU when nothing changes.
- Showing hundreds of old sessions and drowning the live ones.
- Requiring the user to edit config files to make it work.

## Decisions made by the user

- Single compact window, one row per session — not one sticky per session.
- Windows, Linux and macOS; .NET with Avalonia for the UI.
- Palette: grey idle/finished, green working, amber waiting for approval,
  blue waiting for your next message, red errored.
- Options screen contains an always-on-top toggle, a flash toggle, and
  per-service enable/disable.
- Streams are discovered automatically and can be toggled individually.
- Top-level sessions only; no subagent sessions.
- Color-only signalling for v1.
- "Blocked" means amber or blue: amber always, blue until opened and for at
  most 24 hours. Red never counts as blocked. The same definition drives the
  flash, the tray badge, the sort and the age rule.

## Product behaviour

### Window

A single window, 320 px wide with automatic height: it grows with its rows up
to about 60% of screen height, then scrolls. Width is fixed and the window is
not user-resizable. Rows are grouped under a small header per service
(`Windows`, `Ubuntu`, …). Header bar holds the app name, a gear for options,
and minimize/close buttons. Close hides to the tray; the app quits from the
tray menu. The window remembers its screen position between runs (not its
size), clamped to a visible monitor.

Each row: a colored state stripe or dot, project folder name, session title
(truncated with ellipsis), and elapsed time in the current state. Hovering
shows the full title and folder path in a tooltip.

Sort: blocked sessions first, longest-blocked first; then everything else by
most recent activity. Only top-level sessions (`parentID` absent) are shown.
A session is included while its last activity is within 30 minutes, or while
it is blocked regardless of age (subject to the 24-hour blue expiry below).

### States

| State | Color | Meaning |
| --- | --- | --- |
| Working | green | opencode is actively working this session |
| Waiting approval | amber | a tool/permission request is pending |
| Waiting for you | blue | turn finished, session idle, not viewed since |
| Errored | red | last turn failed |
| Idle / finished | grey | idle and viewed, or otherwise quiet |

**Blocked** is one shared definition, used by the flash, the tray badge, the
"never ages out" rule and the sort: amber always counts; blue counts until the
session is opened, and stops counting 24 hours after it turned blue; red never
counts as blocked.

Derivation uses the service API plus its live event stream:

- `outcome: "failed"` while idle → errored.
- Pending permission request → waiting approval. Also `session.permission`
  where present.
- Activity newer than the session's idle timestamp → working.
- Idle, last outcome succeeded, and `time.viewed` older than `time.idle` →
  waiting for you.
- Otherwise grey.

Time in current state is approximated from opencode's own timestamps: `time.idle`
where the state began at idle, `time.updated` otherwise. For a session already
blocked at launch, the counter shows time since the earliest timestamp the
service reports rather than a persisted history.

Exact field semantics (`time.idle`, `time.viewed`, `outcome`) must be verified
against live sessions during the build; the table is the intended mapping,
not a substitute for observation. The same applies to the event that signals a
permission request. If the event stream proves unreliable for a state, fall
back to polling `/api/session` on a short interval.

### Options

A small modal dialog with two sections.

**General**
- Always on top — pinned above other windows. Applies immediately.
- Flash when a session starts blocking — on by default, since it replaces
  notifications. The affected row pulses between its color and a brighter tint
  for a few seconds, stopping when the session is viewed or after a short
  timeout. Flashing only happens while the window is visible; when the window
  is hidden, the tray icon shows a static badge instead.

**Streams**
- One entry per discovered opencode service: friendly name (`Windows`,
  `Ubuntu`), URL, version, connection status, and an on/off switch.
- Switch off: stop polling and streaming, remove that service's rows
  immediately. Switch on: reconnect and retry on a timer.
- Choices persist per stable key (`native`, `wsl:<distro>`). Newly discovered
  services start enabled.
- A "Rescan" button re-runs discovery.

### Tray

Tray icon with Show/Hide, Options, and Quit. Icon shows a small badge/dot when
any monitored session is blocked. Double-click toggles visibility. Closing the
window hides to tray rather than quitting.

## Discovery and connectivity

Verified on this machine:

- Native: `%USERPROFILE%\.local\state\opencode\service.json` on Windows, and
  `~/.local/state/opencode/service.json` on Linux and macOS; the native
  service answered on `http://127.0.0.1:28894` here.
- WSL: each distro keeps its own `~/.local/state/opencode/service.json`
  (Ubuntu answered on `http://127.0.0.1:49374`, reachable from Windows via
  WSL2 localhost forwarding).
- Auth: HTTP Basic, username `opencode`, password from that file. All
  `/api/*` endpoints require it; unauthenticated requests return 401.
- `GET /api/info` → version, pid, hostname, urls.
- `GET /api/session` → `{data: [session…], cursor}`.
- `GET /api/session/{id}` → `{data: session}`.
- `GET /api/event` → server-sent events (`server.connected`,
  `session.tool.success`, `shell.exited`, `message.updated`, …), each with
  `type`, `created`, `location.directory`, `data`.
- Session object: `id`, optional `parentID`, `title`, `agent`, `model`,
  `outcome`, `time.{created,updated,idle,viewed}`, `location.directory`.

Discovery loop:

1. Read the native service file, if present.
2. On Windows only, run `wsl.exe -l -q` for distro names; for each, read its
   service file with
   `wsl.exe -d <distro> -e sh -lc 'cat "$HOME/.local/state/opencode/service.json"'`.
3. Validate each candidate with `GET /api/info` using the stored password.
4. Re-scan every 60 seconds and on demand; keep previously known services
   listed even while they are down, marking them offline.

When an enabled service goes offline, its last-known rows stay visible and
dimmed for five minutes, labelled offline, and are excluded from the badge,
flash and blocked sort. After that they are removed. A service that is switched
off shows no rows at all and produces no network traffic.

If WSL localhost forwarding does not work on a machine, retry with the
distro's IP from `wsl hostname -I`. Service URLs are always loopback.

Native opencode is 2.2.0 here; the WSL distro runs 2.1.4. The client must
tolerate missing or renamed fields rather than crash, and show the version in
options so drift is visible.

## Interaction

Clicking a row opens that session: first try the service's web UI URL for the
session, otherwise fall back to the service root. Whether the desktop app can
be deep-linked reliably is a build-time check; the browser is the fallback
that must always work. Opening a session marks it viewed locally, which
downgrades blue to grey on this app's next update. That mark is in-memory only
and resets on restart; opencode's own `time.viewed` is authoritative whenever
it changes.

Keyboard: `Esc` hides the window, `Ctrl+,` opens options. Not required for
v1; low cost if trivial.

## Technical shape

- .NET 8 with Avalonia 11.3.22 for the UI, `net8.0`, cross-platform.
- One `HttpClient` per service with Basic auth preconfigured.
- One background task per enabled service: SSE reader plus a periodic
  full-refresh (10 s) safety net. Events update an in-memory session map;
  the UI is updated on the dispatcher only when something changed.
- Service discovery and status derivation are pure, testable classes. WSL
  probing is enabled only on Windows.
- Tray icon via Avalonia `TrayIcon`; where a desktop environment does not
  support one, the app keeps running without it.
- Settings: the platform's application-data folder
  (`%APPDATA%\OpenCodeVision\settings.json`, `~/.config/OpenCodeVision/`,
  `~/Library/Application Support/OpenCodeVision/`) — enabled services, window
  position, always-on-top, flash. Written atomically; corrupt file falls back
  to defaults.
- One UI dependency: Avalonia (MIT). No other third-party packages.
- Published self-contained single-file binaries for `win-x64`, `linux-x64`,
  `osx-x64` and `osx-arm64`, with a `.app` bundle for macOS.

## Calls made while refining the design

These were settled during review, not chosen by the user, and are easy to
revisit:

- Offline services keep dimmed last-known rows for five minutes, then drop
  them; they never badge or flash.
- Window width is fixed and height is automatic; only the position persists.
- The local viewed mark is in-memory and resets on restart.
- Time in state is approximated from opencode timestamps; there is no history
  file.
- Flashing is a visible-window effect only; the tray shows a static badge.

## Acceptance criteria

1. With opencode running natively and in Ubuntu, both services appear in
   options without configuration, each with a working on/off switch.
2. Rows appear for top-level sessions only; subagent sessions never appear.
3. Starting work in a session turns its row green within ~2 seconds; finishing
   turns it blue shortly after; the time-in-state counter updates.
4. A permission request turns its row amber within ~2 seconds.
5. Toggling always-on-top keeps the window above a maximized browser and
   survives a restart.
6. With flash on, a newly blocked session visibly pulses; with it off, only
   the color changes.
7. Switching a service off removes its rows and stops network activity;
   switching it back on restores them without restarting the app.
8. The app uses negligible CPU while all sessions are idle.
9. Closing the window hides it to the tray; Quit exits cleanly.
10. The published Windows single-file `.exe` runs on this machine with no
    install.
11. The Linux binary launches and shows sessions when a display is available
    (WSLg or X11); it degrades to a clear error without one.
12. A macOS `.app` bundle is produced with the expected structure and
    executable; runtime verification happens on hardware where available.
13. All four published artifacts are built from the same version and listed in
    the README.

## Risks and open items

- **Permission signal** — the exact event is unverified. Mitigation: observe a
  real prompt during the build; fallback to polling session state.
- **WSL networking** — localhost forwarding is normal on WSL2 but not
  guaranteed. Mitigation: distro IP fallback.
- **API drift** — 2.1.4 vs 2.2.0 differ. Mitigation: tolerant parsing, version
  shown in options.
- **Idle semantics** — `time.idle`/`time.viewed` may not map exactly as
  assumed. Mitigation: verify against live sessions before locking the state
  machine, and cover the mapping with unit tests.
- **Click target** — web UI session URL pattern is assumed. Mitigation:
  confirm during build; fall back to the service root.
- **Cross-platform UI** — Avalonia tray support and always-on-top vary by
  Linux desktop (especially Wayland); macOS is not available on this machine.
  Mitigation: degrade gracefully, keep the window usable without a tray, and
  state what was verified where in the README.
