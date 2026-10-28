# opencode API used by this app

The app reads local HTTP endpoints and makes one explicit write: interrupting
an active session. It never edits session history, sends prompts, approves
permissions, or installs a plugin.

## Service files

Each running opencode service writes a small JSON file containing its URL and
password:

```json
{
  "id": "9b945cac-6082-4bba-9710-df5af1e254f6",
  "version": "2.2.0",
  "url": "http://127.0.0.1:28894",
  "pid": 80716,
  "password": "…"
}
```

| Platform | Path |
| --- | --- |
| Windows | `%USERPROFILE%\.local\state\opencode\service.json` |
| Linux / macOS | `~/.local/state/opencode/service.json` |
| WSL distro | `~/.local/state/opencode/service.json` inside the distro |

The file is created after opencode has run at least once; it is rewritten when
the service restarts (new port/password are normal).

## Authentication

HTTP Basic with username `opencode` and the file's `password`:

```
Authorization: Basic base64("opencode:" + password)
```

Unauthenticated requests return `401` with
`www-authenticate: Basic realm="Secure Area"`. The app treats any non-success
response as a connection failure and retries on the next refresh.

## Endpoints

| Method | Path | Used for |
| --- | --- | --- |
| GET | `/api/info` | Liveness, version, pid |
| GET | `/api/session` | Session list (one page; no cursor following) |
| GET | `/api/session/{id}/permission` | Pending permission count for one session |
| POST | `/api/session/{id}/interrupt` | Stop the session's active execution |
| GET | `/api/event` | Server-sent events (long-lived) |

`/api/session/{id}` exists but the app does not need it.

### `/api/session/{id}/interrupt` shape

The app posts with no body. A successful response is:

```json
{ "interrupted": true }
```

`false` means no execution owned by this service was active; the row is then
hidden until its next update. The endpoint is `interrupt`, not the older
`abort` route: native opencode 2.2.0 exposes only the former.

### `/api/session` shape

```json
{
  "data": [
    {
      "id": "ses_…",
      "parentID": "ses_…",              // optional; subagent sessions
      "title": "Windows sticky for OpenCode sessions",
      "outcome": "succeeded",           // or "failed"
      "location": { "directory": "C:\\Users\\…\\workspace\\blank" },
      "time": {
        "created": 1793111741293,
        "updated": 1793111996605,
        "idle": 1793111816310,
        "viewed": 1793111816310
      }
    }
  ],
  "cursor": { "previous": "…", "next": "…" }
}
```

Times are Unix milliseconds. Missing fields are tolerated; entries without an
`id` are skipped.

### `/api/session/{id}/permission` shape

```json
{ "data": [] }
```

A non-empty array means at least one permission is pending. The app only uses
the count, and only for top-level sessions.

### `/api/event` shape

```
data: {"id":"evt_…","created":1793111975256,"type":"session.tool.success",
       "location":{"directory":"…"},"data":{"sessionID":"ses_…"}}

: heartbeat
```

Observed types include `server.connected`, `session.step.started`,
`session.step.streamed`, `session.step.ended`, `session.tool.called`,
`session.tool.input.started`, `session.tool.input.ended`,
`session.tool.progress`, `session.tool.success`, `session.reasoning.started`,
`session.reasoning.delta`, `session.usage.updated`, `shell.exited`,
`shell.deleted`, `message.updated`, `message.part.updated`. The client does not
depend on a fixed list:

- Every event with a `sessionID` updates that session's `LastEventAt`.
- Every event schedules a debounced refresh (300 ms).
- `session.step.started` opens a step (`StepOpen`, `StepStartedAt`);
  `session.step.ended` closes it. This is what keeps a row green while a tool
  runs, because a long tool emits nothing between `session.tool.called` and
  `session.tool.success`. See `docs/state-model.md`.
- An event whose type contains `permission` (case-insensitive) also refreshes
  permission counts.
- Malformed frames are skipped without terminating the stream.

## Session links

Clicking a row opens the service's web UI at the session. The web app routes a
session as:

```
http://127.0.0.1:28894/server/{key}/session/{id}
```

`{key}` is the unpadded base64url (UTF-8) of the service URL with any trailing
slash removed, e.g. `http://127.0.0.1:28894` becomes
`aHR0cDovLzEyNy4wLjAuMTo0OTM3NA`. The older `/session/{id}` form is not a route
in 2.x and renders "Unrecognised route!". `SessionLink.For` owns this mapping;
the caller falls back to the service root when the link cannot be built.

## The desktop app sidecar

The Windows/macOS/Linux OpenCode desktop app (1.x) is a separate client. On
launch it forks an in-process sidecar server on a random loopback port,
authenticated with a `randomUUID()` password sent over Electron IPC. It does
not write a service file, and its sessions are not in the shared background
service:

- desktop tabs carry `"server": "sidecar"`,
- those session IDs return 404 from `GET /api/session/{id}` on the shared
  service,
- the sidecar rejects unauthenticated requests with 401.

The sticky therefore reads the shared service only. Sessions created through
the CLI or against the background service are visible even if the desktop app
has opened them. The gap and the proposed upstream fix are documented in
[upstream-desktop-sidecar.md](upstream-desktop-sidecar.md).

## Version drift

This app was developed against native opencode 2.2.0 and WSL 2.1.4. The client
parses defensively: missing or renamed fields become null/empty rather than
exceptions. Options shows each service's version so drift is visible.

## Limits

- One page of sessions per service; no cursor pagination.
- Remote services are not supported.
- The `session.permission` field is not used; the permission endpoint is the
  source of truth.
- No approvals, prompt sending, session deletion, or history edits. The only
  write is the interrupt request.
