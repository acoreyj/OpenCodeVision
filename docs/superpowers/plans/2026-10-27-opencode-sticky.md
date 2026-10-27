# OpenCode Sticky Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single-window desktop tray app for Windows, Linux and macOS that shows every top-level opencode session, native and WSL, with a live state color, always-on-top and flash options, and per-service toggles.

**Architecture:** A UI-free `OpenCodeSticky.Core` class library owns discovery, HTTP/SSE, status derivation, settings and the session monitor. A thin Avalonia app (`OpenCodeSticky.App`) owns the window, tray icon and options dialog. Core is tested with xUnit; the UI is verified by running it against the live services and reading the diagnostics log.

**Tech Stack:** .NET 8, Avalonia 11.3.22, System.Text.Json, xUnit. No third-party NuGet packages besides Avalonia.

**Spec:** `docs/superpowers/specs/2026-10-27-opencode-sticky-design.md`

## Global Constraints

- Target `net8.0` for Core, tests and the Avalonia app. Publish `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64`.
- No third-party packages in `OpenCodeSticky.Core`; only Avalonia 11.3.22 in the app.
- Auth is HTTP Basic: username `opencode`, password from the service file's `password`.
- Native service file: `%USERPROFILE%\.local\state\opencode\service.json` on Windows, `~/.local/state/opencode/service.json` on Linux/macOS.
- WSL is probed only on Windows; `ServiceDiscovery` accepts a null gateway elsewhere.
- `wsl.exe -l -q` prints UTF-16LE without a BOM.
- Only top-level sessions (`parentID` absent).
- Recency window: 30 minutes. Blue expires from "blocked" after 24 hours. Offline rows linger 5 minutes.
- Discovery rescan: 60 s. Full refresh: 10 s. SSE-triggered refresh: 300 ms debounce. Working grace: 20 s. Idle tolerance: 2 s.
- Palette: grey `#8A8A8A` idle, green `#3FB950` working, amber `#D29922` approval, blue `#388BFD` waiting, red `#F85149` errored.
- Settings file: `<app-data>/OpenCodeSticky/settings.json` (`%APPDATA%` on Windows, `~/.config` on Linux, `~/Library/Application Support` on macOS).

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
- Amendment (multi-OS): `IWslGateway` is optional (`null` skips WSL), and the native display name follows the host OS (Windows/Linux/macOS).

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

### Task 6: Avalonia shell

**Files:**
- Create: `src/OpenCodeSticky.App/OpenCodeSticky.App.csproj`
- Create: `src/OpenCodeSticky.App/Program.cs`
- Create: `src/OpenCodeSticky.App/App.axaml`, `App.axaml.cs`
- Create: `src/OpenCodeSticky.App/MainWindow.axaml`, `MainWindow.axaml.cs`
- Create: `src/OpenCodeSticky.App/ViewModels/RowViewModel.cs`
- Create: `src/OpenCodeSticky.App/ViewModels/ServiceHeaderViewModel.cs`
- Create: `src/OpenCodeSticky.App/DiagnosticsLog.cs`
- Modify: `OpenCodeSticky.sln` (add the app project, reference Core)

**Interfaces:**
- Consumes: `SessionMonitor.Rows`, `Services`, `Changed`, `BlockedStarted`; `Settings`, `SettingsStore`
- Produces: `MainWindow(SessionMonitor monitor, Settings settings, SettingsStore store, DiagnosticsLog log)`
- Produces: `RowViewModel` with `Project`, `Title`, `Elapsed`, `StateBrush`, `FlashOpacity`, `IsOffline`
- Produces: diagnostics log at `<app-data>/OpenCodeSticky/sticky.log`

- [ ] **Step 1: Create the project**

Create the csproj by hand (no template install needed):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>OpenCodeSticky</AssemblyName>
    <RootNamespace>OpenCodeSticky.App</RootNamespace>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.22" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.22" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.22" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenCodeSticky.Core\OpenCodeSticky.Core.csproj" />
  </ItemGroup>
</Project>
```

```powershell
dotnet sln add src/OpenCodeSticky.App
dotnet add src/OpenCodeSticky.App reference src/OpenCodeSticky.Core
```

- [ ] **Step 2: App bootstrap**

`Program.cs`: `[STAThread] Main` calls `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`; `BuildAvaloniaApp` = `AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()`.

`App.axaml`: `RequestedThemeVariant="Dark"`, `FluentTheme`, and resources:

```xml
<SolidColorBrush x:Key="WindowBackground" Color="#1B1B1B" />
<SolidColorBrush x:Key="PanelBackground" Color="#232323" />
<SolidColorBrush x:Key="MutedText" Color="#8B8B8B" />
<SolidColorBrush x:Key="StateIdle" Color="#8A8A8A" />
<SolidColorBrush x:Key="StateWorking" Color="#3FB950" />
<SolidColorBrush x:Key="StateApproval" Color="#D29922" />
<SolidColorBrush x:Key="StateWaiting" Color="#388BFD" />
<SolidColorBrush x:Key="StateErrored" Color="#F85149" />
```

`App.axaml.cs` startup:

1. `var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCodeSticky");`
2. Settings store at `root/settings.json`; diagnostics log at `root/sticky.log`.
3. Native service file: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "opencode", "service.json")`.
4. `IWslGateway? wsl = OperatingSystem.IsWindows() ? new WslGateway() : null;`
5. `new ServiceDiscovery(wsl, nativePath)` and `new SessionMonitor(discovery, d => new OpenCodeClient(d.Url, d.Password), settings, TimeProvider.System, log.Write)`.
6. Set `desktop.MainWindow` to the window, then `_ = monitor.StartAsync(CancellationToken.None)` (fire and forget; errors are logged by the monitor's service status).

- [ ] **Step 3: Main window**

`MainWindow.axaml`: `Width="320"`, `MinWidth="320"`, `MaxWidth="320"`, `CanResize="False"`, `SizeToContent="Height"`, `Background="{StaticResource WindowBackground}"`, `ShowInTaskbar="True"`, `Title="opencode"`.

Layout: a header row (title + minimize button; gear added in Task 7), then `ScrollViewer` wrapping an `ItemsControl` named `Items`. The window's `MaxHeight` is set in code on `Opened` to 60% of `Screens.Primary.WorkingArea.Height / RenderScaling`.

`ItemsControl.ItemTemplate` is not used; instead declare two `DataTemplate`s in `Window.Resources`:

- `ServiceHeaderViewModel`: small caps service name, top margin 8, muted color.
- `RowViewModel`: `Border` with `PointerPressed="OnRowPressed"`, grid of 4 px bar (`Background="{Binding StateBrush}" Opacity="{Binding FlashOpacity}"`), project + title, elapsed. Title `TextTrimming="CharacterEllipsis"`, `ToolTip.Tip` = full title + path. When `IsOffline`, whole border opacity 0.4.

Code-behind:

- Constructor stores dependencies and subscribes `monitor.Changed` (marshalled with `Dispatcher.UIThread.Post(Rebuild)`), `monitor.BlockedStarted`, and a 1 s `DispatcherTimer` that refreshes `Elapsed` on every row.
- `Rebuild()` walks `monitor.Rows` in order, emits a header whenever `ServiceKey` changes, then a `RowViewModel`. Keep one `ObservableCollection<object> Items` and update it in place.
- `OnRowPressed` marks the row viewed, stops its flash, and launches `new Uri(serviceUrl + "/session/" + id)` via `TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync`. Resolve the service URL from `monitor.Services` by key; on failure open the service root.
- `BlockedStarted` looks up the row and, when `settings.FlashOnBlock && IsVisible`, toggles `FlashOpacity` between 1.0 and 0.45 every 500 ms for 6 s.
- Restore `Position` from settings, clamped to `Screens.All` working areas; default top-right of the primary screen. Save `Position` on `Closing`.
- `Topmost = settings.AlwaysOnTop`.
- `Esc` hides the window (Task 7 turns this into hide-to-tray; until then it closes only when the app is quit).

- [ ] **Step 4: Build and run against the live services**

```powershell
dotnet build OpenCodeSticky.sln -c Debug
Start-Process dotnet -ArgumentList 'run --project src/OpenCodeSticky.App'
Start-Sleep -Seconds 12
Get-Content "$env:APPDATA\OpenCodeSticky\sticky.log" -Tail 20
Get-Process OpenCodeSticky | Select-Object MainWindowTitle, Responding
```

Expected: build succeeds; log lists the discovered services and recent rows; the process is responding with title `opencode`.

- [ ] **Step 5: Commit**

```powershell
git add src OpenCodeSticky.sln
git commit -m "feat(app): cross-platform Avalonia session window"
```

---

### Task 7: Options and tray

**Files:**
- Create: `src/OpenCodeSticky.App/OptionsWindow.axaml`, `OptionsWindow.axaml.cs`
- Create: `src/OpenCodeSticky.App/AppTrayIcon.cs`
- Create: `src/OpenCodeSticky.App/Assets/tray-*.png` (five state icons)
- Create: `packaging/make-icons.ps1`
- Modify: `src/OpenCodeSticky.App/App.axaml` (AvaloniaResource), `App.axaml.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `SessionMonitor.Services`, `SetServiceEnabled`, `RescanAsync`; `Settings`, `SettingsStore`
- Produces: `OptionsWindow(SessionMonitor monitor, Settings settings, SettingsStore store, Action applyAlwaysOnTop)`
- Produces: `AppTrayIcon(MainWindow window, SessionMonitor monitor, Action openOptions, Action quit)` with `Update(IReadOnlyList<SessionRow> rows)`

- [ ] **Step 1: Generate state icons**

`packaging/make-icons.ps1` uses `System.Drawing` to draw a 64×64 dark rounded square with a centred dot, once per state color (`idle`, `working`, `approval`, `waiting`, `errored`), writing `src/OpenCodeSticky.App/Assets/tray-<state>.png`. Run it once and commit the PNGs. Add to the csproj:

```xml
<ItemGroup>
  <AvaloniaResource Include="Assets\**" />
</ItemGroup>
```

- [ ] **Step 2: Options dialog**

Avalonia `Window`, width 380, `SizeToContent="Height"`, `WindowStartupLocation="CenterOwner"`, dark panel background.

- CheckBox "Always on top" (initial value `settings.AlwaysOnTop`) — on change: update settings, save, call `applyAlwaysOnTop`.
- CheckBox "Flash when a session starts blocking" (initial `settings.FlashOnBlock`) — on change: update settings, save.
- Streams: `ItemsControl` over `monitor.Services`, each with a CheckBox (checked = `Enabled`), display name, `v{Version}` or `unknown`, and `online`/`offline`. Toggling calls `monitor.SetServiceEnabled(key, value)`, `settings.SetServiceEnabled(key, value)`, save.
- `Rescan` button calls `monitor.RescanAsync`, disables itself while running, then reloads the list.
- Gear button in the main header opens the dialog with `ShowDialog(this)`; keep one instance.

- [ ] **Step 3: Tray**

`AppTrayIcon` creates `new TrayIcon { ToolTipText = "opencode", Icon = ... }`, attaches it with `TrayIcon.SetIcons(Application.Current!, new TrayIcons { icon })`, and builds a `NativeMenu` with Show/Hide, Options and Quit. Double-click toggles the window. Catch platform exceptions (tray unsupported on some Linux desktops), log, and continue without a tray.

- `Update(rows)`: worst state among online rows (approval > waiting > errored > working > idle) selects `tray-<state>.png`; rebuild the `WindowIcon` only when the state changes.
- Subscribe to `monitor.Changed` to call `Update`.
- Close-to-tray: in `MainWindow.Closing`, if a tray exists and the app is not quitting, cancel and `Hide()`; Quit sets a flag, disposes the tray, cancels the monitor and calls `Shutdown()`.
- Minimizing keeps the taskbar button.

- [ ] **Step 4: Verify by hand**

1. Gear opens options; both toggles change behaviour immediately.
2. Turning `native` off removes its rows within a refresh; turning it on restores them.
3. Each WSL distro is listed separately on Windows.
4. Always-on-top keeps the window above a maximized browser.
5. Closing hides to the tray; the tray double-click shows it; Quit exits.
6. The tray dot turns amber or blue when a session blocks.

- [ ] **Step 5: Commit**

```powershell
git add src packaging
git commit -m "feat(app): options dialog and tray for all platforms"
```

---

### Task 8: Publish for Windows, Linux and macOS

**Files:**
- Create: `README.md`
- Create: `packaging/macos/Info.plist`
- Create: `packaging/linux/opencode-sticky.desktop`
- Modify: `src/OpenCodeSticky.App/OpenCodeSticky.App.csproj` (publish properties)
- Create: `scripts/package.ps1`

**Interfaces:**
- Produces: `artifacts/OpenCodeSticky-win-x64.zip`
- Produces: `artifacts/OpenCodeSticky-linux-x64.tar.gz`
- Produces: `artifacts/OpenCodeSticky-macos-x64.tar.gz`, `artifacts/OpenCodeSticky-macos-arm64.tar.gz` (each containing `OpenCodeSticky.app`)

- [ ] **Step 1: Publish properties**

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

- [ ] **Step 2: Publish each runtime**

```powershell
foreach ($rid in @('win-x64','linux-x64','osx-x64','osx-arm64')) {
  dotnet publish src/OpenCodeSticky.App -c Release -r $rid --self-contained true -o "artifacts/$rid"
}
```

Expected: four directories, each with a single executable named `OpenCodeSticky` (`.exe` on Windows). Cross-publishing from Windows is supported by Avalonia; failures must be fixed, not skipped.

- [ ] **Step 3: Package**

`scripts/package.ps1`:

- Windows: zip `artifacts/win-x64/*` to `artifacts/OpenCodeSticky-win-x64.zip`.
- Linux: copy `opencode-sticky.desktop` next to the binary, then tar.gz it via WSL (`wsl -e bash -lc 'chmod +x ... && tar -czf ...'`) so the executable bit survives. The desktop entry runs `OpenCodeSticky` and has `Terminal=false`, `Categories=Development;`.
- macOS: stage `OpenCodeSticky.app/Contents/MacOS/OpenCodeSticky`, `Contents/Info.plist` (identifier `ai.opencode.sticky`, executable `OpenCodeSticky`, `LSMinimumSystemVersion` 11.0, `NSHighResolutionCapable` true), and `Contents/Resources/app.png`. Tar.gz each architecture via WSL with `chmod +x` on the executable.

- [ ] **Step 4: Smoke-test what this machine can run**

- Windows: run `artifacts/win-x64/OpenCodeSticky.exe`, wait 12 s, check the log shows services and rows, then quit from the tray.
- Linux: from WSL Ubuntu, try `./OpenCodeSticky` with WSLg; if a display is unavailable, record that the binary starts but cannot open a window, and do not claim runtime verification.
- macOS: verify the `.app` tree and that the binary is the expected architecture (`file` in WSL). Runtime verification is not possible here and must be stated as such in the README.

- [ ] **Step 5: README**

What it does, palette, how to run each artifact, where settings and logs live on each OS, the opencode prerequisite, and a short "verified on" table (Windows runtime-tested; Linux build-tested; macOS packaged, not runtime-tested).

- [ ] **Step 6: Commit**

```powershell
git add README.md packaging scripts src
git commit -m "chore: publish Windows, Linux and macOS artifacts"
```

---

## Execution notes

- Run `dotnet test` before every commit; UI tasks additionally need the live-app checks in their own steps.
- The live machine has two local services: native 2.2.0 and Ubuntu WSL 2.1.4. Both must appear on Windows without configuration. On Linux and macOS only the native service exists; WSL probing is Windows-only.
- If the permission event or the session state semantics behave differently from the evaluator's table, adjust `StatusEvaluator` and its tests first, then the monitor. Do not special-case in the UI.
- Keep `StatusEvaluator` free of HTTP and time access so it stays fully unit-tested.
- Avalonia version is pinned to 11.3.22 in the plan; do not float it.
