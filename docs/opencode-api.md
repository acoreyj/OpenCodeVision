# opencode API used by this app

The app only reads local HTTP endpoints. It never writes sessions, edits the
database, or installs a plugin.

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
| GET | `/api/event` | Server-sent events (long-lived) |

`/api/session/{id}` exists but the app does not need it.

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

Observed types include `server.connected`, `session.tool.success`,
`shell.exited`, `message.updated`, `message.part.updated`. The client does not
depend on a fixed list:

- Every event with a `sessionID` updates that session's `LastEventAt`.
- Every event schedules a debounced refresh (300 ms).
- An event whose type contains `permission` (case-insensitive) also refreshes
  permission counts.
- Malformed frames are skipped without terminating the stream.

## Version drift

This app was developed against native opencode 2.2.0 and WSL 2.1.4. The client
parses defensively: missing or renamed fields become null/empty rather than
exceptions. Options shows each service's version so drift is visible.

## Limits

- One page of sessions per service; no cursor pagination.
- Remote services are not supported.
- The `session.permission` field is not used; the permission endpoint is the
  source of truth.
- No writes, approvals, or prompt sending.
