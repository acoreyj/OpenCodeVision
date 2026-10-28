# Keep rows green while a tool runs

> **For agentic workers:** work the tasks in order. Each task ends with a green
> `dotnet test tests/OpenCodeVision.Core.Tests`. Steps use checkbox (`- [x]`)
> syntax for tracking.

**Goal:** A session stays `Working` (green) for the whole time a turn is
executing a tool, instead of turning grey 20 seconds after the last SSE event.

**Problem:** `StatusEvaluator.Evaluate` treats a session as working only when
`Max(Updated, LastEventAt)` is within `WorkingGrace` (20 s). Opencode emits no
events and does not advance `time.updated` while a shell/tool call is in flight.
Measured on this machine: a 58-second `powershell` call produced zero SSE frames
and left `time.updated` untouched until the result landed, so the row went grey
while the TUI showed `esc interrupt`.

**Evidence (captured live, one tool call):**

```
10:01:04  session.step.started  ses_e7d14c26…
10:01:07  session.tool.called   ses_e7d14c26…
          ... 58 s of silence ...
10:02:05  session.tool.success  ses_e7d14c26…
10:02:05  session.step.ended    ses_e7d14c26…
10:02:08  session.step.started  ses_e7d14c26…
```

The step opens before the tool is called and closes only when the tool returns,
so an open step is a reliable "turn in flight" signal.

**Design:** Track the open step on `SessionInfo` from SSE:
`session.step.started` opens it, `session.step.ended` closes it. `Evaluate`
returns `Working` while a step is open, bounded by a ceiling so a lost stream
cannot pin a row green forever. Everything else (permissions first, colours,
sorting) is unchanged.

## Global constraints

- `StatusEvaluator` stays pure: no HTTP, no clock, tests pass `now`.
- All thresholds keep one owner: the step ceiling lives in `StatusEvaluator`.
- A missing or unknown event type changes nothing; old services that never emit
  step events keep the current 20-second behaviour.
- Preserve `StepOpen`/`StepStartedAt` across `MergeSessions`, because
  `GET /api/session` does not return them.
- Do not change colours, priorities, sorting, or the UI.

---

### Task 1: Failing evaluator tests

**Files:**
- Modify: `tests/OpenCodeVision.Core.Tests/StatusEvaluatorTests.cs`

- [x] Add `OpenStep_IsWorkingPastTheGraceWindow`: stale `updated`/`lastEventAt`
      (10 min), `StepOpen = true`, `StepStartedAt = now - 10 min` → `Working`.
- [x] Add `OpenStep_ExpiresAfterStepCeiling`: same but
      `StepStartedAt = now - 31 min` → `Idle`.
- [x] Add `OpenStep_DoesNotOverridePendingPermission`: `PendingPermissions = 1`
      with an open step → `WaitingApproval`.
- [x] Add `ClosedStep_FallsBackToGrace`: `StepOpen = false` with stale timestamps
      → `Idle`.
- [x] Add `BuildRows_KeepsRowWithOpenStepPastCutoff`: `updated` 2 h old,
      `StepOpen = true` → the row is still returned.
- [x] Run `dotnet test tests/OpenCodeVision.Core.Tests --filter StatusEvaluator`
      and confirm the new tests fail to compile / fail on the missing fields.

### Task 2: Model and evaluator

**Files:**
- Modify: `src/OpenCodeVision.Core/SessionInfo.cs`
- Modify: `src/OpenCodeVision.Core/StatusEvaluator.cs`

- [x] Add `bool StepOpen` and `DateTimeOffset? StepStartedAt` to `SessionInfo`.
- [x] Add `StepCeiling = TimeSpan.FromMinutes(30)` and
      `HasOpenStep(session, now)` to `StatusEvaluator` (open and started no more
      than the ceiling ago).
- [x] `Evaluate`: `HasOpenStep(...)` returns `Working` after the permission
      check and before the recency rule; the recency rule keeps its `!quiet`
      guard, the open step deliberately ignores `quiet`.
- [x] `BuildRows`: keep a row when `HasOpenStep(...)` even if its last activity
      is older than the 30-minute cutoff.
- [x] `dotnet test tests/OpenCodeVision.Core.Tests --filter StatusEvaluator` is
      green.

### Task 3: Failing monitor tests

**Files:**
- Modify: `tests/OpenCodeVision.Core.Tests/SessionMonitorTests.cs`

- [x] Add `OpenStep_KeepsWorkingThroughALongToolRun`: idle session, publish
      `session.step.started`, advance the fake clock 5 minutes, refresh →
      `Working`; publish `session.step.ended`, advance, refresh → `Idle`.
- [x] Add `Refresh_ClosesStepWhenServiceReportsNewerIdle`: publish a step start,
      then have the service report `time.idle` newer than the step start on the
      next refresh → the row is no longer `Working`.
- [x] Add a helper that writes to `FakeClient.Events` and waits until the pump
      has drained the channel, so the tests stay deterministic.
- [x] Run `dotnet test tests/OpenCodeVision.Core.Tests --filter SessionMonitor`
      and confirm both fail.

### Task 4: Monitor wiring

**Files:**
- Modify: `src/OpenCodeVision.Core/SessionMonitor.cs`

- [x] `ApplyEvent`: `session.step.started` sets `StepOpen = true`,
      `StepStartedAt = <event created>`; `session.step.ended` clears both; all
      events still set `LastEventAt`.
- [x] `MergeSessions`: carry `StepOpen`/`StepStartedAt` from the previous
      snapshot and clear them when the service's `time.idle` is newer than the
      step start (turn ended while the stream was down).
- [x] `dotnet test tests/OpenCodeVision.Core.Tests` is green; update the test
      count in `AGENTS.md` and `docs/development.md`.

### Task 5: Docs

**Files:**
- Modify: `docs/state-model.md`, `docs/opencode-api.md`,
  `docs/development.md`, `docs/decisions.md`

- [x] `state-model.md`: document the open-step rule, the ceiling, and why it
      precedes the grace window.
- [x] `opencode-api.md`: add the observed step/tool event types and how step
      events drive `Working`.
- [x] `development.md`: add the step ceiling to the thresholds table.
- [x] `decisions.md`: record the trade-off (green during long tools vs. a
      30-minute cap after a lost stream).

### Task 6: Live verification

- [x] `dotnet build OpenCodeVision.sln -c Debug`.
- [x] Start a debug instance next to the packaged one and confirm the row stays
      green through a tool call longer than 20 s, then goes grey/idle after the
      turn ends.
- [x] Confirm a normal short turn still ends grey, and a permission prompt is
      still amber (covered by `OpenStep_DoesNotOverridePendingPermission` and
      the live grey check on the finished session).
