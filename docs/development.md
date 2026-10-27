# Development

## Rules of the split

- `Core` must not reference Avalonia, `Dispatcher`, or any window type.
- `App` may reference `Core`; `Core` never references `App`.
- UI state lives in view models; protocol and state logic live in `Core`.
- Time enters `Core` through `TimeProvider`. Never call `DateTimeOffset.UtcNow`
  in `StatusEvaluator`; tests pass a fixed `now`.

## Test-first workflow

The core is developed with xUnit in `tests/OpenCodeVision.Core.Tests`:

```powershell
dotnet test tests/OpenCodeVision.Core.Tests --filter SessionMonitor
dotnet test tests/OpenCodeVision.Core.Tests
```

For a new rule:

1. Write the failing test with a fixed `now` and explicit timestamps.
2. Run it and confirm the failure is the missing behavior.
3. Implement the smallest change.
4. Run the whole suite (62 tests at the time of writing).
5. Commit with a message describing the behavior.

Fakes live in `Fakes.cs`: `FakeClient`, `FakeDiscovery`,
`MutableTimeProvider`. Use them instead of sleeping or hitting a real service.

## Adding a state

1. Add the value to `SessionState`.
2. Extend `StatusEvaluator.Evaluate` and its decision table; add tests for the
   new branch and the branches it precedes.
3. Add a color constant in `StateBrushes` (`App/ViewModels/RowViewModel.cs`) and
   the matching resource in `App.axaml`.
4. Update `AppTrayIcon.Priority` and add a `tray-<name>.png` via
   `packaging/make-icons.ps1`.
5. Update `docs/state-model.md` and the palette in `README.md`.
6. Verify against a live session before claiming it works.

## Changing thresholds

All thresholds have one owner:

| Threshold | Location |
| --- | --- |
| 30-minute recency, 24-hour blue, 20-second working grace, 2-second idle tolerance | `StatusEvaluator` |
| 10-second refresh, 60-second rescan, 5-minute offline grace, 300 ms debounce, 2-second reconnect | `SessionMonitor` |
| 5-second request timeout | `OpenCodeClient` |
| 6-second flash, 500 ms pulse, 1-second elapsed tick | `MainWindow` |

Changing one is a product change: update the spec and the tests together. Do not
special-case a service or a session in the UI.

## Concurrency checklist

When touching `SessionMonitor`:

- Mutations to runtimes, rows, services and view marks happen under `_gate`.
- `Sessions` lists are read and written under `runtime.Sync`.
- Background refreshes use `Wait(0)` so they cannot queue up.
- UI updates are posted to `Dispatcher.UIThread`.
- Do not raise `Changed` while holding `runtime.Sync`.

## Manual UI checklist

Automated tests cover Core only. Before merging a UI change:

1. Launch against live opencode; confirm discovery and row colors.
2. Open options; toggle a service off and on; confirm rows disappear/return and
   `settings.json` updates.
3. Toggle always-on-top over a maximized window.
4. Trigger a block (permission prompt or finished turn) and watch the row flash.
5. Click a row; confirm the browser opens and the row turns grey.
6. Close to tray and restore on Windows/macOS; on Linux confirm close quits.
7. Run the packaged exe, not just `dotnet run`.

## Code style

- Nullable annotations are enabled; avoid `!` except where a XAML field is
  guaranteed after `InitializeComponent`.
- Async methods end in `Async`; fire-and-forget work goes through `SafeAsync`
  and logs instead of throwing.
- JSON parsing is tolerant: missing fields are normal.
- Keep comments for non-obvious decisions, not for restating code.

## Documentation

When behavior changes, update:

- `README.md` for user-visible behavior
- `docs/state-model.md` for colors/thresholds
- `docs/opencode-api.md` for protocol changes
- `docs/platforms.md` for platform-specific behavior
- `AGENTS.md` only if the commands or invariants change
