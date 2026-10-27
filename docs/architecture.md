# Architecture

## Two projects

```
src/OpenCodeSticky.Core      UI-free class library (net8.0)
src/OpenCodeSticky.App       Avalonia desktop app  (net8.0)
tests/OpenCodeSticky.Core.Tests   xUnit, 62 tests
```

`Core` has no reference to Avalonia and no knowledge of windows, trays or
dispatchers. `App` references `Core` and owns every platform-specific concern.
This split is what makes the state machine and protocol code testable.

## Runtime shape

```
             ┌──────────────────────────── App ───────────────────────────┐
             │  MainWindow          OptionsWindow          AppTrayIcon     │
             │      │                    │                     │           │
             │      └────── Changed / BlockedStarted ──────────┘           │
             └──────────────────────────┬──────────────────────────────────┘
                                        │  (events; UI marshals via Dispatcher)
             ┌──────────────────────────▼──────────────────────────────────┐
             │                     SessionMonitor                         │
             │  runtimes by service key, poll loop, SSE pumps,            │
             │  view marks, blocked-set diff, offline grace                │
             └───────┬─────────────────────────────┬───────────────────────┘
                     │                             │
             ┌───────▼────────┐           ┌────────▼─────────┐
             │ ServiceDiscovery│          │  OpenCodeClient  │  one per service
             │ native + WSL    │          │  HTTP + SSE      │
             └───────┬─────────┘          └────────┬─────────┘
                     │                             │
             service.json files           http://127.0.0.1:<port>/api/*
```

`StatusEvaluator` sits beside `SessionMonitor` as a pure function over
`SessionInfo`; `Settings` / `SettingsStore` provide persisted preferences.

## Data flow

1. `App` loads settings and starts `SessionMonitor`.
2. `ServiceDiscovery` reads the native service file and, on Windows, each WSL
   distro's service file.
3. For each candidate, `SessionMonitor` creates an `OpenCodeClient` with the
   service URL and password.
4. A refresh calls `GET /api/info` then `GET /api/session`, and probes
   `GET /api/session/{id}/permission` for top-level sessions.
5. `StatusEvaluator` turns sessions into rows: state, state start, blocked flag.
6. `Changed` fires; `MainWindow` marshals to the UI thread, rebuilds/updates row
   view models, and `AppTrayIcon` updates the tray icon.
7. An SSE pump per service updates `LastEventAt` and schedules a debounced
   refresh. Permission-shaped events also refresh permission counts.

## Threading model

This is the part to understand before changing `SessionMonitor`.

- **UI thread** owns all Avalonia objects: windows, view models, tray icon.
  `Changed` and `BlockedStarted` are raised on background threads, so both UI
  listeners post through `Dispatcher.UIThread`.
- **Monitor gate**: `_gate` (a `SemaphoreSlim`) serialises discovery, refreshes,
  `SetServiceEnabledAsync` and `MarkViewedAsync`. Background refreshes use
  `Wait(0)` and are skipped when a refresh is already running, so a slow
  service cannot build a queue.
- **Per-runtime lock**: each runtime protects its `Sessions` list with
  `runtime.Sync`. SSE events, permission probes and snapshots all take it.
- **HTTP**: one `HttpClient` per service, 5-second timeout per request, infinite
  timeout only for the SSE stream.
- **Pumps**: one long-lived task per enabled service, reconnecting after 2 s.
  `DisposeAsync` cancels and waits for them.

## Lifecycle

- `App.OnFrameworkInitializationCompleted` builds settings, discovery, monitor,
  window and tray, then starts the monitor fire-and-forget.
- `StartAsync` creates both timers **before** the first rescan, so a discovery
  failure still leaves the 60-second retry running.
- Closing the window hides it on Windows/macOS (tray exists); on Linux it closes
  the app. Quit disposes the tray and awaits monitor disposal.

## Where a change belongs

| Change | Location |
| --- | --- |
| Color/state rules, thresholds, sorting | `Core/StatusEvaluator.cs` + tests |
| Discovery of services | `Core/Discovery/*` |
| API shape, timeouts, SSE parsing | `Core/OpenCodeClient.cs`, `Core/SseParser.cs` |
| Polling, offline grace, blocked events | `Core/SessionMonitor.cs` |
| Persisted preferences | `Core/Settings*.cs` |
| Window, rows, flashing, tray, options | `App/*` |

## Key files

| File | Responsibility |
| --- | --- |
| `Core/SessionMonitor.cs` | Aggregation, polling, SSE, enable/disable, view marks |
| `Core/StatusEvaluator.cs` | Pure state derivation, blocked rule, filtering, sort |
| `Core/OpenCodeClient.cs` | Authenticated HTTP, JSON mapping, SSE subscription |
| `Core/Discovery/ServiceDiscovery.cs` | Native + WSL candidates, alternate WSL address |
| `Core/Settings.cs` / `SettingsStore.cs` | Locked settings model, atomic JSON persistence |
| `App/MainWindow.axaml.cs` | Grouped rows, elapsed ticker, flash, open session |
| `App/OptionsWindow.axaml.cs` | Toggles, service switches, rescan |
| `App/AppTrayIcon.cs` | Tray icon, menu, worst-state badge |
