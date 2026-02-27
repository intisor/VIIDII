# Phase 1: MVP Stabilization & Hardening — VIIDII (Edu Edition)

> **Sprint duration:** 5 days  
> **Goal:** Harden the existing codebase against the FUTA campus network reality.  
> **What we are NOT doing:** Rewriting the engagement engine, SignalR hub, or recap system — they already work.

---

## Dependency Map

```
Task 3 (EF Core) must start before Tasks 1 & 2 are considered "production-ready"
Task 1 (DFA) enables Task 4 (LAN Mode, since offline signaling is a DFA state)
Task 5 (QR) is independent — can be built in parallel with Tasks 1–4
```

---

## Task 1: Architect the WebRTC State Machine (Day 1)

**What is broken today:**  
`session.js` uses `Task.Delay(500)` timing hacks to wait for the DOM before calling `callStudent()`. WebRTC peer connections have no formal lifecycle — they're managed via ad-hoc booleans and a partially implemented `activeCalls` Map that is not in sync with the C# `SessionState`. Ghost calls (one peer thinks they're connected, the other doesn't) are the #1 user-facing bug on 3G connections.

**What we are building:**  
A strict 6-state DFA enforced in both C# (`PeerConnectionContext.cs`) and JavaScript (`session.js`). Every WebRTC lifecycle event (peer created → signaling → ICE connecting → stream received → network drop → reconnect → destroy) maps to a validated state transition. No transition fires unless its preconditions are met.

**Exact files to change:**
- `[NEW]` `Models/PeerConnectionContext.cs` — DFA state + transition validator
- `[MODIFY]` `Services/SessionService.cs` — replace `AddStudentPeer()` with `TryTransitionPeer()` that validates DFA rules
- `[MODIFY]` `Hubs/SessionHub.cs` — `SendPeerId` broadcasts DFA state changes; add `NotifyPeerStateChange` hub method
- `[MODIFY]` `wwwroot/js/session.js` — add `peerStates` Map, `transitionState()` guard, replace all raw `peer.call()` with DFA-guarded wrappers
- `[MODIFY]` `Services/SessionJsInterop.cs` — add `NotifyPeerStateAsync(peerId, state)` JS interop call
- `[MODIFY]` `Components/Shared/ParticipantPanel.razor` — show per-student connection state badge (Signaling/Connecting/Connected/Degraded/Disconnected)

**Acceptance:**
- Zero ghost calls in a 5-student test where one student disconnects and reconnects 3 times
- `activeCalls` Map in JS is always consistent with `ParticipantPanel` badge

- **Category:** `🏗️ Portfolio Build`
- **FUTA Course:** `CSC309` — Automata Theory (DFA/NFA, state transitions, well-defined formal language)
- **Content Output:** `📹 YouTube/Twitch` — "I Modeled WebRTC as a Finite State Machine to Kill Ghost Calls"
- **Lock-in Day:** `1`

---

## Task 2: Fix the SignalR Join Storm + Sapa Mode (Day 2)

**What is broken today:**  
`SessionHub.GetMatricNoForConnectionAsync()` (lines 37–70 in `SessionHub.cs`) contains a `Task.Delay(100)` retry that runs when the MatricNo is not immediately cached. In a 100-student mass join during O-Week registration, 100 simultaneous `Task.Delay(100)` calls cause thread-pool starvation (see Feasibility Study §2.1 for the math). Students see "Connecting..." for up to 42 seconds.

The existing `IssueButtons.razor` → `FlagIssue("BatteryLow")` path updates `ParticipantPanel` for the lecturer — but does nothing to save the student's phone. There is no power-saving mode.

**What we are building:**

**Part A — Eliminate the Task.Delay retry:**
- Cache the MatricNo at `OnConnectedAsync()` via a claim/query-string parameter instead of reading it lazily
- Add a SignalR Hub Filter `MatricNoCachingFilter` that populates `_connectionMatricNos` before any hub method runs
- Remove the `Task.Delay(100)` retry entirely from `GetMatricNoForConnectionAsync()`

**Part B — Sapa Mode for VideoStage:**
- New SignalR event: `ActivateSapaMode` (triggered when lecturer's hub receives `FlagIssue("BatteryLow")`)
- New C# method: `SessionJsInterop.ActivateSapaModeAsync()` → calls `session.activateSapaMode()`
- JS: `activateSapaMode()` stops all incoming video `MediaStreamTrack.stop()`, removes video DOM element from `VideoStage.razor`, shows battery-save banner
- Quality stats polling (`RTCPeerConnection.getStats()` every 5s) auto-triggers SD/AudioOnly tiers based on RTT and packet loss

**Exact files to change:**
- `[MODIFY]` `Hubs/SessionHub.cs` — remove `Task.Delay(100)`, add Hub Filter, add `ActivateSapaMode` broadcast in `FlagIssue()`
- `[NEW]` `Hubs/MatricNoCachingFilter.cs` — `IHubFilter` that reads MatricNo from query string on connect
- `[MODIFY]` `Program.cs` — register `MatricNoCachingFilter`, add query-string auth to SignalR endpoint
- `[MODIFY]` `wwwroot/js/session.js` — add `activateSapaMode()`, quality polling loop, `applyQualityTier()`
- `[MODIFY]` `Services/SessionJsInterop.cs` — add `ActivateSapaModeAsync()`, `GetPeerStatsAsync(peerId)`
- `[MODIFY]` `Components/Shared/VideoStage.razor` — add Sapa Mode banner; hide video element when in sapa state
- `[MODIFY]` `Components/Shared/IssueButtons.razor` — BatteryLow button also calls `ActivateSapaModeAsync()`

**Acceptance:**
- 100-student join load test completes in under 3 seconds (no thread delay)
- Sapa Mode reduces incoming video CPU usage by ≥ 80% (Chrome Task Manager)
- Lecturer's `ParticipantPanel` shows 🔋 icon for students in Sapa Mode

- **Category:** `🏗️ Portfolio Build`
- **FUTA Course:** `CSC305` — System Programming (thread pools, concurrency primitives, kernel-level I/O scheduling)
- **Content Output:** `📹 YouTube/Twitch` — "How I Fixed Thread Pool Starvation in ASP.NET SignalR (100 Users)"
- **Lock-in Day:** `2`

---

## Task 3: EF Core + PostgreSQL — Rip Out MockApiService (Day 3)

**What is broken today:**  
`MockApiService.cs` is a static, hardcoded in-memory user list. It is called directly in `SessionHub.cs` (4 places), `SessionService.cs` (3 places), and `SessionRecap.razor` (1 place). Every server restart wipes all session history, attendance records, and engagement scores. The `CalculateAttendanceScore()` engine is brilliant but processes ephemeral RAM state.

**What we are building:**

**Step A — Repository interfaces (no breaking changes):**
- `IUserRepository` → `MockUserRepository` (wraps existing `MockApiService` — keeps tests green)
- `ISessionRepository` → `InMemorySessionRepository` (wraps existing `SessionService._sessions`)
- All `MockApiService.GetUsers()` calls → `_userRepository.GetAllAsync()` calls

**Step B — EF Core implementation:**
- `ViidiiDbContext` with all 5 tables (see Feasibility Study §4 for DDL)
- `EfUserRepository`, `EfSessionRepository` implementations
- EF Core Code-First migration: `dotnet ef migrations add InitialCreate`
- Connection string in `appsettings.json` → `appsettings.Production.json`

**Step C — Seeder (Development only):**
- `DatabaseSeeder.cs` — reads from `MockApiService` and seeds the DB on startup in Development mode
- This ensures existing test credentials (Lec001, 123456, etc.) still work

**Exact files to change:**
- `[NEW]` `Services/Interfaces/IUserRepository.cs`
- `[NEW]` `Services/Interfaces/ISessionRepository.cs`
- `[NEW]` `Services/Repositories/EfUserRepository.cs`
- `[NEW]` `Services/Repositories/EfSessionRepository.cs`
- `[NEW]` `Data/ViidiiDbContext.cs`
- `[NEW]` `Data/DatabaseSeeder.cs`
- `[NEW]` `Data/Migrations/` (EF Core generated)
- `[MODIFY]` `Hubs/SessionHub.cs` — inject `IUserRepository`; replace all `MockApiService.GetUsers()` calls
- `[MODIFY]` `Services/SessionService.cs` — inject `IUserRepository`; replace `MockApiService.GetLecturers()`
- `[MODIFY]` `Components/Pages/SessionRecap.razor` — inject `IUserRepository`; replace `MockApiService.GetUsers()`
- `[MODIFY]` `Program.cs` — register EF Core, `ViidiiDbContext`, repositories; add seeder call

**Acceptance:**
- Server restart does not lose any session or user data
- `SessionRecap.razor` loads recap for sessions that ended before the current server instance started
- `dotnet ef migrations add InitialCreate` runs without errors

- **Category:** `🏗️ Portfolio Build`
- **FUTA Course:** `SEN204` — Requirements Engineering (data persistence, system integration, entity modeling)
- **Content Output:** `📝 Substack` — "Migrating from In-Memory Mocks to EF Core + PostgreSQL in a Production Blazor App"
- **Lock-in Day:** `3`

---

## Task 4: "Zero-App" LAN Data Channel + Catch-Up Protocol (Day 4)

**What is broken today:**  
The existing P2P file sharing in `session.js` uses PeerJS's cloud signaling server (`0.peerjs.com`). In a FUTA LT with no active internet (common after 11pm when the campus fiber drops), `new Peer()` fails and students cannot receive files at all. There is no fallback.

There is also no mechanism for late-joining students to receive files that were distributed before they joined — they must ask the lecturer to re-send, which is disruptive.

**What we are building:**

**Part A — Local PeerJS Signaling via ASP.NET:**
- Add an in-process PeerJS-compatible HTTP signaling endpoint to `Program.cs` using lightweight middleware
- `session.js::createPeer()` checks `window.viidiiOfflineMode` (set by `ControlsBar.razor` toggle) and uses local host config vs. cloud config
- `ControlsBar.razor` gets a new "OFFLINE MODE" toggle button that sets `window.viidiiOfflineMode = true` and reinitializes PeerJS

**Part B — The Catch-Up Protocol:**
- `MessagingPanel.razor` maintains a client-side `_localChatBuffer` (already exists implicitly in `_messages`) serializable to JSON
- New SignalR hub method: `NotifyLatePeerJoined(sessionId, latePeerId)`
- `session.js` listens for `LatePeerJoined` → opens DataChannel to late peer → sends `{type: 'catchup', messages: [...], fileManifest: [...]}`
- Late peer's `session.js` receives catch-up packet → invokes `window.viidii.onCatchUpReceived(data)`
- `SessionJsInterop.cs` exposes `[JSInvokable] OnCatchUpReceived()` → pushes messages to `MessagingPanel`

**Exact files to change:**
- `[MODIFY]` `Program.cs` — add PeerJS-compatible signaling middleware routes
- `[NEW]` `Hubs/PeerSignalingHub.cs` — thin hub that relays WebRTC SDP/ICE via SignalR for LAN mode
- `[MODIFY]` `Hubs/SessionHub.cs` — add `NotifyLatePeerJoined` method
- `[MODIFY]` `wwwroot/js/session.js` — `createPeer()` factory, catch-up sender/receiver, DataChannel control protocol
- `[MODIFY]` `Services/SessionJsInterop.cs` — add `[JSInvokable] OnCatchUpReceived(JsonElement data)`
- `[MODIFY]` `Components/Shared/ControlsBar.razor` — add Offline Mode toggle button
- `[MODIFY]` `Components/Shared/MessagingPanel.razor` — handle catch-up message injection

**Acceptance:**
- File transfer of a 10MB PDF completes to 5 students with no internet (router disconnected)
- Late-joining student receives full chat history within 5 seconds of joining
- "Files received from peer: [name]" message appears in their chat

- **Category:** `🏗️ Portfolio Build`
- **FUTA Course:** `CSC307` — Data Communications (P2P protocols, DataChannel framing, LAN topology, ICE candidates)
- **Content Output:** `🐦 Twitter/X` — "Shipped offline campus file sharing. No internet. No app. Just WebRTC Data Channels 🔥"
- **Lock-in Day:** `4`

---

## Task 5: Dynamic Cryptographic QR Attendance (Day 5)

**What is broken today:**  
Attendance is tracked via `ParticipantPingService` → `EngagementModal` ping-response loop. This system correctly measures engagement time, but it has no physical presence verification. A student can share their session code (via existing `JoinSession` flow), sit in the hostel, and score 100% engagement. There is no proof they are physically in the Lecture Theatre.

**What we are building:**  
A rotating HMAC-SHA256 QR code displayed on the lecturer's screen that proves physical presence in the LT. The QR token rotates every 15 seconds. One scan per student per session. Replayed tokens are rejected with HTTP 401.

**Exact files to change:**
- Add NuGet: `QRCoder` (C# QR code generation) to `VIIDII.csproj`
- `[NEW]` `Services/AttendanceTokenService.cs` — HMAC generation, validation, time-window check, one-time-use enforcement (backed by `AttendanceLogs` DB table from Task 3)
- `[NEW]` `Components/Pages/AttendanceScan.razor` — the landing page at `/attend?token=[x]` that a student hits after scanning; validates token, records log, shows "Checked in ✅" or "Token expired ❌"
- `[MODIFY]` `Components/Pages/LecturerSessionView.razor` — add QR code display panel that refreshes every 15 seconds using a `System.Threading.Timer`; uses `QRCoder.QRCodeGenerator` to generate SVG; displays time-remaining countdown
- `[MODIFY]` `Program.cs` — register `AttendanceTokenService` as singleton; load `Attendance:SecretKey` from config
- `[MODIFY]` `appsettings.json` — add `"Attendance": { "SecretKey": "[generate on first run]", "WindowSeconds": 15 }`
- `[MODIFY]` `Components/Pages/SessionRecap.razor` — add a "QR Check-In" column to the attendance breakdown table showing ✅/❌ per student

**QR Rotation in Blazor:**
```csharp
// In LecturerSessionView.razor @code
private System.Threading.Timer? _qrTimer;
private string _currentQrSvg = string.Empty;

protected override async Task OnInitializedAsync()
{
    await RefreshQrAsync();
    _qrTimer = new System.Threading.Timer(async _ => 
    {
        await RefreshQrAsync();
        await InvokeAsync(StateHasChanged);
    }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
}

private async Task RefreshQrAsync()
{
    var token = AttendanceTokenService.GenerateToken(SessionId);
    var url = $"{NavigationManager.BaseUri}attend?token={token}&session={SessionId}";
    _currentQrSvg = QrCodeHelper.GenerateSvg(url);
}
```

**Acceptance:**
- QR refreshes visually every 15 seconds (countdown shown)
- Scanning the old QR after expiry returns "Token expired" page
- Same student scanning twice returns "Already checked in" page
- `SessionRecap.razor` shows QR check-in status per student alongside engagement score

- **Category:** `🏗️ Portfolio Build`
- **FUTA Course:** `SEN307` — Software Security (HMAC, cryptographic authentication, replay attack prevention)
- **Content Output:** `💼 LinkedIn` — "Built A Cryptographic Attendance System for My University — Here's the HMAC Math"
- **Lock-in Day:** `5`

---

## Definition of Done

All 5 tasks are considered complete when:

- [ ] Full 5-student test session runs without ghost calls (Task 1)
- [ ] 100-connection load test completes in < 3s with no thread errors (Task 2)
- [ ] Server restart retains all session/user data (Task 3)
- [ ] File transfer works with router disconnected (Task 4)
- [ ] QR scan 1: ✅ "Checked in". QR scan 2: ❌ "Already checked in". Old token: ❌ "Expired" (Task 5)
- [ ] `dotnet build` → 0 errors, 0 warnings
- [ ] `SessionRecap.razor` shows data that survived a server restart (Tasks 3 + 5)
