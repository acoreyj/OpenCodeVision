# AGENTS.md

OpenCode Sticky is a .NET 8 Avalonia tray app that shows opencode session state
on Windows, Linux and macOS. Core logic is UI-free and tested with xUnit.

## Commands

```powershell
dotnet build OpenCodeSticky.sln -c Debug
dotnet test tests/OpenCodeSticky.Core.Tests
dotnet run --project src/OpenCodeSticky.App
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

`dotnet run` needs opencode to have run at least once so a service file exists.
Packaging needs WSL for `tar`/`wslpath`.

## Structure

- `src/OpenCodeSticky.Core` — discovery, HTTP/SSE, state rules, settings, monitor
- `src/OpenCodeSticky.App` — Avalonia window, options dialog, tray, diagnostics log
- `tests/OpenCodeSticky.Core.Tests` — 62 tests, fakes in `Fakes.cs`
- `packaging/` — `.desktop`, `Info.plist`, icon generator
- `scripts/package.ps1` — publish + archive four RIDs
- `artifacts/` — generated, git-ignored

## Invariants

- `Core` does not reference Avalonia or any UI type. UI changes stay in `App`.
- `StatusEvaluator` is pure: no HTTP, no clock. Tests pass `now` explicitly.
- Thresholds have one owner: `StatusEvaluator` (state), `SessionMonitor`
  (polling/offline), `OpenCodeClient` (timeouts), `MainWindow` (visual timing).
- All monitor mutations take `_gate`; session lists take `runtime.Sync`.
  `Changed` is raised on background threads and the UI must marshal it.
- JSON and SSE parsing tolerate missing or unknown fields.
- Avalonia is pinned to 11.3.22. No other third-party packages.
- Published artifacts are not committed.

## Docs

- Read [docs/architecture.md](docs/architecture.md) before changing
  `SessionMonitor`, threading, or module boundaries.
- Read [docs/state-model.md](docs/state-model.md) before changing colors,
  thresholds, sorting, blocked rules, or inclusion.
- Read [docs/opencode-api.md](docs/opencode-api.md) before changing discovery,
  the HTTP client, or event handling.
- Read [docs/configuration.md](docs/configuration.md) before changing settings
  keys, defaults, or file paths.
- Read [docs/platforms.md](docs/platforms.md) before touching tray, windowing,
  WSL, signing, or packaging behavior.
- Read [docs/building.md](docs/building.md) for publish/package commands.
- Read [docs/troubleshooting.md](docs/troubleshooting.md) when a service shows
  no rows or stays offline.
- Read [docs/development.md](docs/development.md) for the TDD flow, adding a
  state, concurrency rules, and the manual UI checklist.
- Read [docs/decisions.md](docs/decisions.md) for the rationale and costs behind
  the current design.

## Verifying

- Run `dotnet test` before claiming a core change is done; the suite must be
  green (62 tests at this commit).
- UI changes need the live checklist in `docs/development.md`, including the
  packaged build.
- Packaging changes need `scripts/package.ps1` and a check of each archive:
  Windows exe, Linux executable bit, macOS dylibs and `.app` layout.
