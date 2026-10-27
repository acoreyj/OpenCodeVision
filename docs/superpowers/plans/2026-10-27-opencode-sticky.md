# OpenCode Sticky Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single-window Windows tray app that shows every top-level opencode session, native and WSL, with a live state color, always-on-top and flash options, and per-service toggles.

**Architecture:** A UI-free `OpenCodeSticky.Core` class library owns discovery, HTTP/SSE, status derivation, settings and the session monitor. A thin WPF app (`OpenCodeSticky.App`) owns the window, tray icon and options dialog. Core is tested with xUnit; the UI is verified by running it against the live services on this machine and reading the diagnostics log.

**Tech Stack:** .NET 8, WPF, System.Windows.Forms (tray only), System.Text.Json, xUnit. No third-party NuGet packages in shipped projects.

**Spec:** `docs/superpowers/specs/2026-10-27-opencode-sticky-design.md`

## Global Constraints

- Target `net8.0` for Core and tests; `net8.0-windows` for the app. Publish `win-x64`.
- No third-party packages in `OpenCodeSticky.Core` or `OpenCodeSticky.App`.
- Auth is HTTP Basic: username `opencode`, password from the service file's `password`.
- Native service file: `%USERPROFILE%\.local\state\opencode\service.json`.
- WSL service file: `~/.local/state/opencode/service.json`, read via `wsl.exe`.
- `wsl.exe -l -q` prints UTF-16LE without a BOM.
- Only top-level sessions (`parentID` absent).
- Recency window: 30 minutes. Blue expires from "blocked" after 24 hours. Offline rows linger 5 minutes.
- Discovery rescan: 60 s. Full refresh: 10 s. SSE-triggered refresh: 300 ms debounce. Working grace: 20 s. Idle tolerance: 2 s.
- Palette: grey `#8A8A8A` idle, green `#3FB950` working, amber `#D29922` approval, blue `#388BFD` waiting, red `#F85149` errored.
- Settings file: `%APPDATA%\OpenCodeSticky\settings.json`.

## Review Focus

- A machine with no WSL distros and no native service must still launch and show an empty list without crashing or spinning.
- A malformed or missing service file must skip that service and leave the others working.
- A service that restarts with a new password must recover on the next rescan, not stay dead forever.
- Sessions with missing time fields or no `idle` must render as idle, not as an exception or permanent green.
- Clock skew or future timestamps must display as elapsed `0s`, never a negative duration.

---

### Task 1: Solution, models, status evaluation

**Files:**
- Create: `.gitignore`, `OpenCodeSticky.sln`
- Create: `src/OpenCodeSticky.Core/OpenCodeSticky.Core.csproj`
- Create: `src/OpenCodeSticky.Core/SessionState.cs`
- Create: `src/OpenCodeSticky.Core/SessionInfo.cs`
- Create: `src/OpenCodeSticky.Core/SessionRow.cs`
- Create: `src/OpenCodeSticky.Core/ServiceModels.cs`
- Create: `src/OpenCodeSticky.Core/StatusEvaluator.cs`
- Create: `src/OpenCodeSticky.Core/ElapsedFormatter.cs`
- Create: `tests/OpenCodeSticky.Core.Tests/OpenCodeSticky.Core.Tests.csproj`
- Test: `tests/OpenCodeSticky.Core.Tests/StatusEvaluatorTests.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/ElapsedFormatterTests.cs`

**Interfaces:**
- Produces: `enum SessionState { Idle, Working, WaitingApproval, WaitingForYou, Errored }`
- Produces: `record SessionInfo { required string Id; string? ParentId; string Title; string Directory; string? Outcome; int PendingPermissions; DateTimeOffset? Created, Updated, Idle, Viewed, LastEventAt, PendingSince; }`
- Produces: `record SessionRow(string ServiceKey, string ServiceName, SessionInfo Session, SessionState State, DateTimeOffset StateSince, bool IsBlocked, bool IsOffline)`
- Produces: `record ServiceSessions(string Key, string Name, IReadOnlyList<SessionInfo> Sessions)`
- Produces: `StatusEvaluator.Evaluate(SessionInfo, bool viewedLocally, DateTimeOffset now) -> SessionState`
- Produces: `StatusEvaluator.StateSince(SessionInfo, SessionState, DateTimeOffset now) -> DateTimeOffset`
- Produces: `StatusEvaluator.IsBlocked(SessionState, DateTimeOffset since, DateTimeOffset now) -> bool`
- Produces: `StatusEvaluator.BuildRows(IEnumerable<ServiceSessions>, IReadOnlySet<string> viewedLocally, DateTimeOffset now) -> IReadOnlyList<SessionRow>`
- Produces: `ElapsedFormatter.Format(TimeSpan) -> string`

- [ ] **Step 1: Initialize the repository and projects**

```powershell
git init
dotnet new sln -n OpenCodeSticky
dotnet new classlib -n OpenCodeSticky.Core -o src/OpenCodeSticky.Core -f net8.0
dotnet new xunit -n OpenCodeSticky.Core.Tests -o tests/OpenCodeSticky.Core.Tests -f net8.0
dotnet sln add src/OpenCodeSticky.Core src/OpenCodeSticky.Core.Tests
dotnet add tests/OpenCodeSticky.Core.Tests reference src/OpenCodeSticky.Core
```

Delete the template placeholders `src/OpenCodeSticky.Core/Class1.cs` and `tests/OpenCodeSticky.Core.Tests/UnitTest1.cs`.

`.gitignore`: `bin/`, `obj/`, `.vs/`, `*.user`, `publish/`, `artifacts/`, `*.log`.

- [ ] **Step 2: Write the failing evaluator tests**

Create `SessionInfo` and stubs only as needed to compile. In `StatusEvaluatorTests.cs`, with `now = 2026-10-27T12:00:00Z`:

```csharp
[Fact] public void PendingPermission_IsWaitingApproval()
  // session idle, outcome succeeded, PendingPermissions = 1 -> WaitingApproval
[Fact] public void FailedAndIdle_IsErrored()
  // outcome "failed", Idle = now-5m, Updated = Idle -> Errored
[Fact] public void IdleAndUnviewed_IsWaitingForYou()
  // Idle = now-5m, Viewed = now-10m -> WaitingForYou
[Fact] public void IdleAndViewed_IsIdle()
  // Viewed == Idle -> Idle
[Fact] public void RecentActivity_IsWorking()
  // Idle = now-10m, Updated = now-1s, LastEventAt = now-1s -> Working
[Fact] public void RecentActivityWithNoIdle_IsWorking()
  // Idle = null, Updated = now-3s -> Working
[Fact] public void StaleSessionWithoutIdle_IsIdle()
  // Idle = null, Updated = now-2h, Outcome = "succeeded" -> Idle
[Fact] public void LocalViewMark_OverridesBlue()
  // Idle = now-5m, Viewed = null, viewedLocally = true -> Idle
[Fact] public void BlueBlocksFor24HoursThenStops()
  // since = now-23h -> true; since = now-25h -> false
[Fact] public void AmberBlocksAlwaysAndRedNever()
[Fact] public void BuildRows_ExcludesSubagentSessions()
  // session with ParentId = "ses_parent" is absent
[Fact] public void BuildRows_KeepsOldBlockedSession()
  // updated now-2d, idle now-2d, unviewed -> present and blocked
[Fact] public void BuildRows_DropsOldQuietSession()
  // updated now-2h, viewed == idle -> absent
[Fact] public void BuildRows_SortsBlockedLongestFirstThenRecent()
  // rows: blue(since -1h), amber(since -5m), working(updated -1s)
  // expected order: blue, amber, working
```

`ElapsedFormatterTests`:

```csharp
[Fact] public void Format_BucketsSecondsMinutesHoursDays()
  // 0s -> "0s", 42s -> "42s", 5m -> "5m", 3h -> "3h", 2d -> "2d"
[Fact] public void Format_NegativeSpan_IsZero()
  // TimeSpan.FromSeconds(-5) -> "0s"
```

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests`
Expected: compile failure or `NotImplementedException` for `StatusEvaluator`.

- [ ] **Step 4: Implement the models and evaluator**

Exact decision table:

```
Evaluate:
  1. PendingPermissions > 0                                  -> WaitingApproval
  2. lastActivity = max(Updated, LastEventAt); quiet = Idle != null && (Updated == null || Updated <= Idle + 2s)
     if lastActivity != null && now - lastActivity <= 20s && !quiet -> Working
  3. Outcome == "failed"                                     -> Errored
  4. Viewed != null && Idle != null && Viewed >= Idle        -> Idle
  5. viewedLocally                                           -> Idle
  6. Idle != null                                            -> WaitingForYou
  7. otherwise                                               -> Idle
```

```
StateSince:
  WaitingApproval -> PendingSince ?? Idle ?? Updated ?? now
  Working         -> Idle + 2s (when Idle exists) ?? Created ?? Updated ?? now
  others          -> Idle ?? Updated ?? Created ?? now
```

```
IsBlocked: WaitingApproval always; WaitingForYou while now - since < 24h; Errored/Working/Idle never.
```

`BuildRows`: skip `ParentId != null`; carry `Key` and `Name` onto each row; include when blocked or `(Updated ?? LastEventAt ?? Created) >= now - 30m`; sort blocked first by `StateSince` ascending, then by `(Updated ?? LastEventAt ?? Created)` descending, ties by `Session.Title`.

`ElapsedFormatter.Format`: negative or `<1s` -> `0s`; `<60s` -> `Ns`; `<60m` -> `Nm`; `<24h` -> `Nh`; else `Nd`.

- [ ] **Step 5: Run the tests until green**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests`
Expected: all pass.

- [ ] **Step 6: Commit**

```powershell
git add .gitignore OpenCodeSticky.sln src tests docs
git commit -m "feat(core): session models and status evaluation"
```

---

### Task 2: Service discovery

**Files:**
- Create: `src/OpenCodeSticky.Core/Discovery/ServiceFile.cs`
- Create: `src/OpenCodeSticky.Core/Discovery/ServiceDiscovery.cs`
- Create: `src/OpenCodeSticky.Core/Discovery/IWslGateway.cs`
- Create: `src/OpenCodeSticky.Core/Discovery/WslGateway.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/ServiceDiscoveryTests.cs`

**Interfaces:**
- Produces: `record ServiceCandidate(string Key, string DisplayName, Uri Url, string Password, Uri? AlternateUrl = null)`
- Produces: `record ServiceFile(Uri Url, string Password, string? Version, int? Pid)`
- Produces: `ServiceFileParser.Parse(string json) -> ServiceFile` (throws `FormatException`)
- Produces: `IWslGateway.ListDistrosAsync(CancellationToken) -> Task<IReadOnlyList<string>>`, `ReadServiceFileAsync(string distro, CancellationToken) -> Task<string?>`, `GetDistroAddressAsync(string distro, CancellationToken) -> Task<string?>`
- Produces: `ServiceDiscovery(IWslGateway wsl, string nativeStatePath)`, `DiscoverAsync(CancellationToken) -> Task<IReadOnlyList<ServiceCandidate>>`
- Produces: `WslGateway.DecodeDistroList(byte[] bytes) -> IReadOnlyList<string>` (static, testable)

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Parse_ValidServiceFile_ReturnsUrlAndPassword()
  // {"url":"http://127.0.0.1:28894","password":"pw","version":"2.2.0"} -> Url, pw, 2.2.0
[Fact] public void Parse_MissingUrl_ThrowsFormatException()
[Fact] public void Parse_InvalidJson_ThrowsFormatException()
[Fact] public void DecodeDistroList_Utf16NoBom_ReturnsNames()
  // Encoding.Unicode.GetBytes("Ubuntu\r\n") -> ["Ubuntu"]
[Fact] public async Task Discover_NativeAndTwoDistros_ReturnsThreeCandidates()
  // keys: "native", "wsl:Ubuntu", "wsl:Debian"; display: "Windows", "Ubuntu", "Debian"
[Fact] public async Task Discover_NoNativeFileNoDistros_ReturnsEmpty()
[Fact] public async Task Discover_DistroWithoutServiceFile_IsSkipped()
[Fact] public async Task Discover_OneMalformedDistroFile_DoesNotHideTheOthers()
[Fact] public async Task Discover_WslCandidate_GetsDistroIpAlternateUrl()
  // service URL http://127.0.0.1:49374 + address 172.20.1.5 -> alternate http://172.20.1.5:49374
[Fact] public async Task Discover_NoDistroAddress_LeavesAlternateNull()
```

`FakeWslGateway` returns scripted distro names and file bodies; native path points at a temp file.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter ServiceDiscovery`
Expected: types missing.

- [ ] **Step 3: Implement discovery**

`ServiceDiscovery` reads the native JSON if the file exists; runs `wsl.ListDistrosAsync`; for each non-empty name reads `ReadServiceFileAsync`; skips null files and `FormatException`/`JsonException`; builds `Key = "native"` or `"wsl:" + distro`, `DisplayName = "Windows"` or distro. Never throws for one bad source. For WSL candidates, asks `GetDistroAddressAsync` and, when it returns an address, sets `AlternateUrl` to `http://<address>:<port from the service URL>`.

`WslGateway` runs `wsl.exe -l -q` with `StandardOutputEncoding` unset, captures bytes via `RedirectStandardOutput` stream, then `DecodeDistroList`; runs `wsl.exe -d <distro> -e sh -lc "cat \"$HOME/.local/state/opencode/service.json\""` and returns UTF-8 text (`null` on non-zero exit); runs `wsl.exe -d <distro> -e hostname -I` and returns the first whitespace-separated token (`null` on failure). Set a 10 s timeout per process.

- [ ] **Step 4: Run the tests until green**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter ServiceDiscovery`
Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(core): native and WSL service discovery"
```

---

### Task 3: opencode client and SSE

**Files:**
- Create: `src/OpenCodeSticky.Core/OpenCodeClient.cs`
- Create: `src/OpenCodeSticky.Core/SseParser.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/OpenCodeClientTests.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/SseParserTests.cs`

**Interfaces:**
- Produces: `interface IOpenCodeClient : IDisposable` with `GetInfoAsync`, `GetSessionsAsync`, `GetPendingPermissionCountAsync(string sessionId, CancellationToken)`, `IAsyncEnumerable<ServerEvent> SubscribeAsync(CancellationToken)`
- Produces: `record ServerInfo(string Version, int Pid)`
- Produces: `record ServerEvent(string Type, DateTimeOffset Created, string? SessionId)`
- Consumes: `SessionInfo` from Task 1
- Produces: `OpenCodeClient(Uri baseUrl, string password, HttpMessageHandler? handler = null)`
- Produces: `OpenCodeException(int StatusCode, string Message) : Exception`
- Produces: `SseParser.ReadDataAsync(TextReader, CancellationToken) -> IAsyncEnumerable<string>`

- [ ] **Step 1: Write the failing tests**

`SseParserTests`:

```csharp
[Fact] public async Task ReadsTwoFramesFromOneStream()
  // "data: {\"a\":1}\n\ndata: {\"b\":2}\n\n" -> 2 payloads in order
[Fact] public async Task IgnoresHeartbeatComments()
  // ": heartbeat\n\ndata: x\n\n" -> only "x"
[Fact] public async Task SkipsEmptyDataLines()
```

`OpenCodeClientTests` with a stub `HttpMessageHandler` returning canned responses:

```csharp
[Fact] public async Task SendSetsBasicAuthHeader()
  // GET /api/session -> request.Headers.Authorization == Basic base64("opencode:pw")
[Fact] public async Task GetSessions_ParsesNestedTimes()
  // payload with time {created,updated,idle,viewed} -> DateTimeOffset.FromUnixTimeMilliseconds values
[Fact] public async Task GetSessions_TolerantOfMissingTimesAndTitle()
  // {"data":[{"id":"ses_1"}]} -> one SessionInfo, null times, empty title
[Fact] public async Task GetSessions_SkipsEntriesWithoutId()
[Fact] public async Task GetPendingPermissionCount_CountsDataArray()
  // {"data":[{},{}]} -> 2
[Fact] public async Task NonSuccessStatus_ThrowsOpenCodeException()
  // 401 -> OpenCodeException with StatusCode
```

Use this live payload shape as the fixture:

```json
{"data":[{"id":"ses_e82362c9affeZIBhljTUk5mC0G","title":"Windows sticky",
 "outcome":"succeeded","location":{"directory":"C:\\work"},
 "time":{"created":1793111741293,"updated":1793111996605,"idle":1793111816310,"viewed":1793111816310}}]}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter "Sse|OpenCodeClient"`
Expected: types missing.

- [ ] **Step 3: Implement**

- `OpenCodeClient` sets `Authorization: Basic base64("opencode:" + password)` and `Accept: application/json` on every request; base URL is the service URL.
- `GetSessionsAsync` returns `data` mapped to `SessionInfo`, using `DateTimeOffset.FromUnixTimeMilliseconds`; missing fields become null/empty; entries with blank `id` are skipped.
- `GetPendingPermissionCountAsync` calls `/api/session/{id}/permission` and returns `data.Count`.
- `SubscribeAsync` performs `GET /api/event` with `ResponseHeadersRead`, reads with `SseParser`, parses each payload as JSON, and yields `ServerEvent(type, createdMs, data.sessionID)` when `type` is non-empty.
- `OpenCodeException(StatusCode, Message)` is thrown for non-success responses.
- `SseParser` understands `data:` lines, joins multi-line data, ignores `:` comments and blank frames, and stops cleanly on end of stream.

- [ ] **Step 4: Run until green**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter "Sse|OpenCodeClient"`
Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(core): opencode HTTP client and SSE reader"
```

---

### Task 4: Settings

**Files:**
- Create: `src/OpenCodeSticky.Core/Settings.cs`
- Create: `src/OpenCodeSticky.Core/SettingsStore.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `Settings` with `bool AlwaysOnTop = true`, `bool FlashOnBlock = true`, `double? WindowLeft`, `double? WindowTop`, `Dictionary<string,bool> ServiceEnabled`, `bool IsServiceEnabled(string key)`, `void SetServiceEnabled(string key, bool enabled)`
- Produces: `SettingsStore(string path)`, `Load() -> Settings`, `Save(Settings)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Load_MissingFile_ReturnsDefaults()
  // AlwaysOnTop true, FlashOnBlock true, enabled service defaults true
[Fact] public void SaveThenLoad_RoundTripsEverything()
[Fact] public void Load_CorruptJson_ReturnsDefaults()
[Fact] public void Save_WritesAtomically()
  // no .tmp file remains in the directory after Save
[Fact] public void SetServiceEnabled_FalseIsRemembered()
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter SettingsStore`

- [ ] **Step 3: Implement**

Serialize with `System.Text.Json` and `WriteIndented = true`. `Save` writes `<path>.tmp`, then `File.Move(tmp, path, overwrite: true)`. `Load` catches `IOException`, `JsonException` and returns defaults. Create the parent directory on save.

- [ ] **Step 4: Run until green**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter SettingsStore`
Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(core): persisted settings"
```

---

### Task 5: Session monitor

**Files:**
- Create: `src/OpenCodeSticky.Core/SessionMonitor.cs`
- Create: `src/OpenCodeSticky.Core/ServiceStatus.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/SessionMonitorTests.cs`
- Test: `tests/OpenCodeSticky.Core.Tests/Fakes.cs`

**Interfaces:**
- Consumes: `ServiceDiscovery`, `ServiceCandidate`, `IOpenCodeClient`, `Settings`, `StatusEvaluator`
- Produces: `record ServiceStatus(string Key, string DisplayName, Uri Url, string? Version, bool Enabled, bool Online)`
- Produces: `record BlockedSessionEventArgs(string ServiceKey, string SessionId)`
- Produces: `SessionMonitor(ServiceDiscovery discovery, Func<ServiceDescriptor, IOpenCodeClient> clientFactory, Settings settings, TimeProvider time, Action<string>? log = null)`
- Produces: `Rows -> IReadOnlyList<SessionRow>`, `Services -> IReadOnlyList<ServiceStatus>`, `event Changed`, `event BlockedStarted`, `StartAsync`, `RescanAsync`, `SetServiceEnabled(string, bool)`, `MarkViewed(string serviceKey, string sessionId)`, `RefreshNowAsync`, `IAsyncDisposable`
- Produces: `ServiceDescriptor(string Key, string DisplayName, Uri Url, string Password, string? Version, Uri? AlternateUrl = null)`

- [ ] **Step 1: Write the failing tests**

Use `FakeOpenCodeClient` (scripted `Sessions`, `Permissions`, `Info`, optional `Throw`) and `MutableTimeProvider`.

```csharp
[Fact] public async Task Start_BuildsRowsFromEnabledServices()
[Fact] public async Task DisabledService_HasNoRowsAndNoRequests()
[Fact] public async Task SetServiceEnabled_OffRemovesRowsImmediately()
[Fact] public async Task Offline_KeepsLastRowsForFiveMinutesThenDrops()
  // advance 4m -> rows still present with IsOffline; advance past 5m -> gone
[Fact] public async Task OfflineRows_AreNotBlocked()
[Fact] public async Task BlockedStarted_FiresOncePerTransition()
  // first refresh with a blocked session does not fire; next transition into blocked does
[Fact] public async Task MarkViewed_DowngradesWaitingForYou()
[Fact] public async Task Rescan_PicksUpNewServiceAndNewPassword()
  // initially 401 -> service offline; rescan with working client -> online
[Fact] public async Task PrimaryFails_AlternateSucceeds_ServiceStaysOnline()
  // client for the primary URL throws; alternate client returns sessions; rows appear
[Fact] public async Task Refresh_FetchesPermissionCounts()
  // client.PermissionRequests increments; pending -> WaitingApproval row
[Fact] public async Task Rows_AreSortedBlockedFirst()
[Fact] public async Task NoServices_DoesNotThrow()
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests --filter SessionMonitor`

- [ ] **Step 3: Implement**

- Per enabled service, keep a runtime: descriptor, client, version, last sessions, last rows, `LastOnlineAt`, `LastError`, `Online`. Build the descriptor from the discovered candidate; `GetInfoAsync` supplies `Version` for `ServiceStatus` and a failing info call counts as offline.
- `RefreshNowAsync`: for each runtime, `GetSessionsAsync`; on success update sessions and `LastOnlineAt`; on failure mark offline and remember the error without throwing.
- Connection fallback: each runtime starts on `Url`. When that fails and `AlternateUrl` is set, retry once with a descriptor whose URL is the alternate (same password); on success keep using it. `RescanAsync` resets to `Url`. Dispose replaced clients.
- Permission counts: fetch for every cached top-level session on a full refresh, and on any SSE event whose type contains `permission` (case-insensitive). Store as `SessionInfo.PendingPermissions`; set `PendingSince` on 0→>0 transitions and clear it when the count returns to 0.
- SSE: one task per enabled service; any event updates `LastEventAt` for its `SessionId` and schedules a debounced (300 ms) session-list refresh. Reconnect after 2 s on stream errors while enabled.
- Rows: online services go through `StatusEvaluator.BuildRows`. Offline services reuse their last rows with `IsOffline = true` and `IsBlocked = false`, sorted after online rows; drop them once `now - LastOnlineAt > 5m`.
- `BlockedStarted`: after the first successful refresh, compare the blocked set and raise for each false→true transition.
- `MarkViewed`: add the session id to a local `HashSet<string>` consulted by the evaluator.
- `RescanAsync`: re-run discovery, add new services, replace changed URLs/passwords by key, keep enabled state from `Settings`, drop services no longer found.
- Background loops use `TimeProvider.CreateTimer` (60 s rescan, 10 s refresh). Tests call `RefreshNowAsync` directly and never rely on real time.
- `log` receives one line per state change: `"<serviceKey> <sessionId> <old> -> <new>"`.

- [ ] **Step 4: Run until green**

Run: `dotnet test tests/OpenCodeSticky.Core.Tests`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(core): session monitor with discovery, polling and SSE"
```

---

### Task 6: WPF shell

**Files:**
- Create: `src/OpenCodeSticky.App/OpenCodeSticky.App.csproj`
- Create: `src/OpenCodeSticky.App/App.xaml`, `App.xaml.cs`
- Create: `src/OpenCodeSticky.App/MainWindow.xaml`, `MainWindow.xaml.cs`
- Create: `src/OpenCodeSticky.App/RowViewModel.cs`
- Create: `src/OpenCodeSticky.App/DiagnosticsLog.cs`
- Modify: `OpenCodeSticky.sln` (add the app project, reference Core)

**Interfaces:**
- Consumes: `SessionMonitor.Rows`, `Services`, `Changed`, `BlockedStarted`
- Produces: `MainWindow(SessionMonitor monitor, Settings settings, SettingsStore store, DiagnosticsLog log)`
- Produces: `diagnostics log` path `%APPDATA%\OpenCodeSticky\sticky.log`

- [ ] **Step 1: Create the project and the window**

```powershell
dotnet new wpf -n OpenCodeSticky.App -o src/OpenCodeSticky.App -f net8.0
dotnet sln add src/OpenCodeSticky.App
dotnet add src/OpenCodeSticky.App reference src/OpenCodeSticky.Core
```

Set `UseWindowsForms=true` and `ApplicationManifest` left default; `OutputType=WinExe`, `AssemblyName=OpenCodeSticky`.

Window requirements:

- 320 px wide, `SizeToContent="Height"`, `ResizeMode="CanMinimize"`, `MaxHeight` set on load to 60% of the working area.
- Header: title `opencode` and a minimize button; `Esc` hides the window. The gear is added in Task 7.
- A `ListBox` bound to a `CollectionViewSource` grouped by `ServiceName`, with a group header template showing the service name.
- Row template: 4 px state-colored bar, project folder (tail of `Directory`), title with `TextTrimming="CharacterEllipsis"`, elapsed text, `ToolTip` showing full title and path. Offline rows at 40% opacity with an `offline` suffix.
- `Topmost = settings.AlwaysOnTop` on load. Restore `Left`/`Top` from settings, clamped to `SystemParameters.VirtualScreen*`; default to the top-right of the primary working area.
- Clicking a row calls `monitor.MarkViewed(...)` and opens `new Uri(serviceUrl, "/session/" + id)` with `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`; the service URL comes from `monitor.Services` by key. If opening the session URL throws, open the service root instead. Desktop-app deep links are out of scope; the browser is the target.
- `Changed` rebuilds the grouped view model collection without replacing the collection instance (avoid flicker). Update elapsed text on a 1 s `DispatcherTimer`.
- `BlockedStarted` starts a 6 s `DispatcherTimer` that toggles `IsFlashing` on that row every 500 ms; clicking the row stops it early. Only runs when `settings.FlashOnBlock` is true and the window `IsVisible`.
- Hiding the window saves `Left`/`Top` to settings.

- [ ] **Step 2: Wire startup and diagnostics**

`App.xaml.cs` on startup:

1. Create `SettingsStore(Path.Combine(Environment.GetFolderPath(ApplicationData), "OpenCodeSticky", "settings.json"))` and load.
2. Create `DiagnosticsLog` at the same folder; truncate to empty when it exceeds 1 MB at startup; `Write(string)` appends `"HH:mm:ss.fff <line>"`.
3. Create `WslGateway`, `ServiceDiscovery(wsl, Path.Combine(profile, ".local", "state", "opencode", "service.json"))`.
4. Create `SessionMonitor(discovery, d => new OpenCodeClient(d.Url, d.Password), settings, TimeProvider.System, log.Write)`.
5. Start the monitor, show `MainWindow`, and log one line per service found and per state change.

`Fakes` and Core tests must stay untouched.

- [ ] **Step 3: Build**

Run: `dotnet build OpenCodeSticky.sln -c Debug`
Expected: build succeeds with no new warnings. Fix any binding-related build issues rather than suppressing them.

- [ ] **Step 4: Run against the live services and verify the data path**

Prerequisites: opencode running natively. Run:

```powershell
Start-Process dotnet -ArgumentList 'run --project src/OpenCodeSticky.App'
Start-Sleep -Seconds 12
Get-Content "$env:APPDATA\OpenCodeSticky\sticky.log" -Tail 20
Get-Process OpenCodeSticky | Select-Object MainWindowTitle, Responding
```

Expected: `sticky.log` lists `native` and at least one row; the process is responding and the window title is `opencode`. Capture the log tail in the task notes.

- [ ] **Step 5: Commit**

```powershell
git add src OpenCodeSticky.sln
git commit -m "feat(app): always-on-top session window with live rows"
```

---

### Task 7: Options and tray

**Files:**
- Create: `src/OpenCodeSticky.App/OptionsWindow.xaml`, `OptionsWindow.xaml.cs`
- Create: `src/OpenCodeSticky.App/TrayIcon.cs`
- Modify: `src/OpenCodeSticky.App/MainWindow.xaml`, `MainWindow.xaml.cs`
- Modify: `src/OpenCodeSticky.App/App.xaml.cs`

**Interfaces:**
- Consumes: `SessionMonitor.Services`, `SetServiceEnabled`, `RescanAsync`; `Settings`, `SettingsStore`
- Produces: `OptionsWindow(SessionMonitor monitor, Settings settings, SettingsStore store, Action applyAlwaysOnTop)`
- Produces: `TrayIcon(Window window, SessionMonitor monitor, Action openOptions, Action quit)` with `UpdateBadge(IReadOnlyList<SessionRow> rows)`

- [ ] **Step 1: Build the options dialog**

- `WindowStartupLocation="CenterOwner"`, width 380, size to content.
- Add the gear button to the `MainWindow` header; it opens `OptionsWindow` with `ShowDialog()` (single instance; if already open, activate it).
- General: `CheckBox` "Always on top" bound to `settings.AlwaysOnTop`; `CheckBox` "Flash when a session starts blocking" bound to `settings.FlashOnBlock`. Both save immediately on change and call the apply callback for topmost.
- Streams: `ItemsControl` over `monitor.Services`, each row showing display name, `v{Version}` (or `unknown`), `online`/`offline`, and a `CheckBox` bound to a per-item wrapper. Toggling calls `monitor.SetServiceEnabled(key, value)`, `settings.SetServiceEnabled`, and saves.
- `Rescan` button calls `monitor.RescanAsync`, shows `Scanning…` while running, and refreshes the list from `monitor.Services`.
- Close button dismisses; no other effects.

- [ ] **Step 2: Build the tray icon**

- `NotifyIcon` with `Text = "opencode"`, context menu: Show/Hide, Options, Quit; double-click toggles the window.
- Icon is generated at runtime: 16×16 (and 32×32) bitmap, dark rounded square `#1F1F1F`, center dot in the most urgent state color (amber > blue > red > green > grey). Rebuild only when the urgency color changes.
- `UpdateBadge` takes the current rows, computes the worst state among online rows, and rebuilds the icon if the color changed.
- Closing the window hides it (`e.Cancel = true; Hide();`) and updates the tray tooltip; Quit cancels the monitor and shuts down.
- Window `StateChanged`: minimizing keeps the taskbar button (do not hide).
- In `App.xaml.cs`, create the tray after `MainWindow`, wire `openOptions` to the same dialog used by the gear, and wire `quit` to cancel the monitor, save settings and `Shutdown()`.

- [ ] **Step 3: Verify by hand**

Run the app. Check:

1. Gear opens options; both toggles change behavior immediately.
2. Turning `native` off removes its rows within one refresh; turning it back on restores them.
3. `wsl:Ubuntu` appears and can be toggled independently.
4. Always-on-top keeps the window above a maximized browser.
5. Closing hides to tray; tray double-click shows it; Quit exits.
6. Tray dot turns amber or blue when a session blocks.

Record the outcome in the task notes; if a check fails, fix it before committing.

- [ ] **Step 4: Commit**

```powershell
git add src
git commit -m "feat(app): options dialog and tray icon"
```

---

### Task 8: Publish and end-to-end check

**Files:**
- Create: `README.md`
- Modify: `src/OpenCodeSticky.App/OpenCodeSticky.App.csproj` (publish properties)

**Interfaces:**
- Produces: `publish/OpenCodeSticky.exe`, single file, self-contained `win-x64`

- [ ] **Step 1: Add publish properties and publish**

In the app project:

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

Run:

```powershell
dotnet publish src/OpenCodeSticky.App -c Release -o publish
Get-ChildItem publish
```

Expected: exactly `OpenCodeSticky.exe` (plus any `.pdb` you choose to delete). Record the file size.

- [ ] **Step 2: Smoke-test the artifact**

Run `publish\OpenCodeSticky.exe`; wait 12 s; check `%APPDATA%\OpenCodeSticky\sticky.log` shows discovery and rows; confirm the process responds; quit from the tray.

- [ ] **Step 3: Write the README**

Include: what it does, the palette, how to run, where settings and logs live, that it requires opencode to have run at least once, and the known limits (local and WSL only, no notifications).

- [ ] **Step 4: Commit**

```powershell
git add README.md src
git commit -m "chore: single-file publish and README"
```

---

## Execution notes

- Run `dotnet test` before every commit; UI tasks additionally need the live-app checks in their own steps.
- The live machine currently has two local services: native 2.2.0 and Ubuntu 2.1.4. Both must appear without configuration.
- If the permission event or the session state semantics behave differently from the evaluator's table, adjust `StatusEvaluator` and its tests first, then the monitor. Do not special-case in the UI.
- Keep `StatusEvaluator` free of HTTP and time access so it stays fully unit-tested.
