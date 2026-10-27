# State model

Everything here is implemented in `Core/StatusEvaluator.cs` and covered by
`tests/OpenCodeSticky.Core.Tests/StatusEvaluatorTests.cs`.

## Which sessions appear

- Top-level only: a session with a non-null `parentID` is a subagent session and
  is always hidden.
- Recent: last activity (`Updated`, else `LastEventAt`, else `Created`) within
  **30 minutes**.
- Blocked sessions appear regardless of age, subject to the blue expiry below.

## States

| State | Color | Meaning |
| --- | --- | --- |
| `Working` | green `#3FB950` | opencode is actively working |
| `WaitingApproval` | amber `#D29922` | a permission request is pending |
| `WaitingForYou` | blue `#388BFD` | turn finished, not viewed since |
| `Errored` | red `#F85149` | the last turn failed |
| `Idle` | grey `#8A8A8A` | idle and viewed, or quiet |

### Derivation order

`Evaluate(session, viewedLocally, now)` checks in this order:

1. `PendingPermissions > 0` → **WaitingApproval**.
2. Last activity within **20 s** and not quiet → **Working**.
   - Last activity = `max(Updated, LastEventAt)`.
   - Quiet = `Idle` exists and `Updated <= Idle + 2 s`.
3. `outcome == "failed"` → **Errored**.
4. `Viewed >= Idle` → **Idle**.
5. Locally marked viewed for this turn → **Idle**.
6. `Idle` exists → **WaitingForYou**.
7. Otherwise → **Idle**.

The 20-second grace keeps a session green between tool calls. A session with no
`idle` and stale `updated` falls through to idle instead of staying green.

## Blocked

"Blocked" is one shared definition used by the flash, the tray badge, the sort
and the age rule:

- **Amber**: always blocked.
- **Blue**: blocked until the session is opened. A blue session stops counting
  **24 hours** after it turned blue.
- **Red, green, grey**: never blocked.

The 24-hour expiry prevents an abandoned session from pinning the badge forever.

## Time in state

Approximate, from opencode's own timestamps:

| State | Start |
| --- | --- |
| WaitingApproval | `PendingSince` (earliest service timestamp on first observation), else `Idle`, `Updated`, now |
| Working | `Idle + 2 s`, else `Created`, `Updated`, now |
| Everything else | `Idle`, else `Updated`, `Created`, now |

`ElapsedFormatter.Format` clamps negative spans to `0s`, so clock skew or a
future timestamp displays as `0s` rather than a negative duration.

## Ordering and grouping

1. Blocked rows first, longest-blocked first.
2. Then everything else by last activity, newest first.
3. Ties by title.

The window emits **one header per service**, in the order each service first
appears in that sorted list. Rows inside a group keep the sorted order, so the
most urgent group floats to the top without repeating headers.

## Offline services

- A service that stops responding is marked offline; its rows stay visible,
  dimmed, labelled `offline`, for **5 minutes**.
- Offline rows never count as blocked and never flash.
- A service whose file disappears from discovery is kept as a known offline
  service, so an opencode restart does not make its rows vanish instantly.
- After the grace period its rows are removed; the service remains listed in
  options as offline until it returns.

## Edge cases

| Input | Behavior |
| --- | --- |
| Missing `time`, `title`, `directory` | Empty strings/null fields; renders as idle. |
| `parentID` set | Hidden. |
| `updated` in the future | Not "working" indefinitely; clamped elapsed. |
| Permission probe fails | Session keeps its previous state; one failure doesn't hide it. |
| Service returns 401 | Offline; retry on the next refresh/rescan. |
| Session deleted in opencode | Row disappears on the next refresh. |
