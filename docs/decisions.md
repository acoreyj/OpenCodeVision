# Decisions

Short records of choices that shape the app. Each lists what it costs.

## Avalonia instead of WPF

**Decision:** one Avalonia 11.3.22 UI for Windows, Linux and macOS.
**Why:** the same window/tray/options code ships everywhere; WPF would lock the
app to Windows.
**Cost:** one UI dependency, a larger single-file binary, and Linux/macOS UI
behavior that cannot be fully verified on the build machine.

## Local HTTP API instead of the database or terminal scraping

**Decision:** read `/api/session`, `/api/session/{id}/permission` and
`/api/event` from each running service.
**Why:** no schema coupling, no terminal parsing, live updates, and it works the
same for native and WSL services.
**Cost:** depends on opencode's HTTP surface and version drift; requires the
service to be running.

## Poll + SSE hybrid

**Decision:** SSE triggers updates; a 10-second poll is the safety net. A
5-second request timeout and a non-queuing refresh guard protect against a hung
service.
**Why:** responsive without trusting one event type, resilient without hammering.
**Cost:** two sources of truth to keep consistent; permission counts are still
polled rather than event-driven.

## One window, not one sticky per session

**Decision:** a single list window grouped by service.
**Why:** stays glanceable with several sessions, one place for options and tray.
**Cost:** a very long session list needs scrolling; no per-session placement.

## Top-level sessions only

**Decision:** hide sessions with a `parentID`.
**Why:** subagents are implementation detail; showing them drowns the real work.
**Cost:** no visibility into a running subagent except through its parent's
state.

## Color-only, no notifications

**Decision:** state color plus optional row flashing; a tray dot when hidden.
**Why:** the app is a sticky note, not a notification system; no interruption.
**Cost:** a block can be missed if the window is hidden and the tray is ignored.

## 30 minutes / 24 hours / 5 minutes / 20 seconds

**Decision:** recent window 30 min; blue stops blocking after 24 h; offline rows
linger 5 min; working grace 20 s; idle tolerance 2 s.
**Why:** enough context to see the day's work without drowning in history; blue
cannot pin the badge forever; brief hiccups do not erase rows.
**Cost:** an old but genuinely blocked blue session stops counting; a long model
call with no events for >20 s can briefly look idle.

## Per-service switches

**Decision:** services are discovered automatically and can be toggled.
**Why:** WSL distros and the native service start and stop independently; users
need control without editing config.
**Cost:** one more state to persist and reason about.

## Four artifacts

**Decision:** `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`, self-contained and
single-file where possible.
**Why:** "ship all three" with the least runtime setup for the user.
**Cost:** four builds to keep green; macOS needs dylibs and manual signing.

## Linux never hides to the tray

**Decision:** on Linux, close quits and `Esc`/tray-toggle minimize.
**Why:** tray support varies by desktop; a hidden window with no tray is
unreachable.
**Cost:** no hide-to-tray on Linux even when the desktop supports it.

## macOS bundle is unsigned

**Decision:** ship the `.app` and document the one-line ad-hoc signing step.
**Why:** the build machine is Windows and cannot run `codesign`.
**Cost:** an extra command before first launch on Apple Silicon; Gatekeeper still
needs quarantine removal in some setups.

## Desktop app sidecar is not monitored

**Decision:** monitor the shared local service only; do not scrape the desktop
app's UI state or capture its sidecar credential.
**Why:** the desktop app (1.x) generates a per-launch password and passes it
in memory; reading UI files would give titles but no live state, and a
debug-port bridge would weaken local security for a fragile integration.
**Cost:** desktop-only sessions are invisible until upstream exposes a service
file or reuses the shared service. The limitation is documented and an upstream
issue is drafted in [upstream-desktop-sidecar.md](upstream-desktop-sidecar.md).

## No single-instance guard, no remote services, no auto-update

**Decision:** v1 keeps these out.
**Why:** each adds a large subsystem without serving the core glanceable-status
goal.
**Cost:** two instances can run; remote machines are unsupported; updates are
manual.
