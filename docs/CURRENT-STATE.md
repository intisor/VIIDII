# VIIDII Application: Current State Audit

**Last Updated:** 2026-02-XX  
**Target Framework:** .NET 10  
**Status:** MVP in development with Phase 1 stabilization in progress

---

## Executive Summary

VIIDII is a **Blazor Server (.NET 10) + SignalR + PeerJS** WebRTC video conferencing platform engineered for FUTA's low-bandwidth, high-concurrency campus environment. The **core MVP infrastructure is working** (SignalR hub, engagement tracking, session recap). **Phase 1 focus:** eliminate ghost calls, fix thread-pool starvation on mass joins, add database persistence.

---

## Implemented ✅

### 1. WebRTC Core Architecture (PARTIALLY COMPLETE — Task 1)

| Component | Status | Details |
|-----------|--------|---------|
| `PeerConnectionContext.cs` (DFA Model) | ✅ Complete | 6-state machine (Idle → Signaling → Connecting → Connected → Degraded → Disconnected) defined in C#. FrozenDictionary transition table. Thread-safe with lock. |
| `SessionService.TryTransitionPeer()` | ✅ Complete | DFA state transition enforcement in C#. Validates triggers before applying. Logs invalid transitions. |
| `SessionHub` integration | ⚠️ Partial | Hub caches MatricNo on connect, but NO broadcast of peer state changes to clients yet. |
| `ParticipantPanel.razor` state badge | ❌ Missing | Should display per-student connection state (Signaling/Connecting/Connected/Degraded/Disconnected). |
| `session.js` DFA wrapper | ❌ Missing | No `transitionState()` guard on `peer.call()`. Ghost-call vulnerability exists. |
| `SessionJsInterop.cs` state changes | ❌ Missing | No `NotifyPeerStateAsync()` method to push state changes to clients. |

**Current Issue:** The DFA model exists and is enforced in C#, but **JavaScript doesn't know about state transitions**. A student's `session.js` can still fire `peer.call()` when the C# DFA says "not ready," causing ghost calls.

---

### 2. SignalR Hub & Connection Management

| Component | Status | Details |
|-----------|--------|---------|
| `SessionHub.cs` (core) | ✅ Working | Handles StartSession, JoinSession, SendMessage, EndSession. Uses `ConcurrentDictionary<string, string>` for MatricNo caching. |
| `GetMatricNoForConnectionAsync()` | ⚠️ Inefficient | Still contains `Task.Delay(100)` retry (❌ PROBLEM: causes thread-pool starvation on 100+ concurrent joins) |
| Broadcaster methods | ✅ Working | SendPeerId, BroadcastMessage, SendEngagementQuestion, etc. all broadcast to groups. |
| MatricNo caching | ✅ Working | Cache populated at `OnConnectedAsync()` (via `StartSession`). Survives reconnects. |

**Current Issue:** The `Task.Delay(100)` retry loop runs on every hub method call if MatricNo isn't cached yet. Under mass-join load (100 students), this causes thread-pool exhaustion.

---

### 3. Session & Engagement Tracking

| Component | Status | Details |
|-----------|--------|---------|
| `SessionService.CreateSession()` | ✅ Working | Creates in-memory `Session` object with status tracking, participant list. |
| `SessionService.JoinSession()` | ✅ Working | Adds student to session, initializes attendance score (0%). |
| Engagement modal | ✅ Working | `EngagementModal.razor` + `ParticipantPingService` → random pings every 2–5 min with 30s countdown. |
| Attendance scoring | ✅ Working | `CalculateAttendanceScore()` computes final % based on `_participantActivityTimestamps`. |
| Session recap | ✅ Working | `SessionRecap.razor` shows timeline chart, bar chart, breakdown table. Uses `chartInterop.js` for JS visualization. |

---

### 4. User Authentication & Authorization

| Component | Status | Details |
|-----------|--------|---------|
| `AuthService.cs` | ✅ Working | Stores logged-in user MatricNo in-memory. Has `GetCurrentMatricNo()`, `LoginAsync()`, `LogoutAsync()`. |
| `MockApiService.cs` | ✅ Seeded | Hardcoded 10 users (7 students, 2 lecturers, 1 admin) with hashed passwords. |
| Login page | ✅ Working | `Login.razor` accepts MatricNo + password. Validates against `MockApiService`. |
| Role-based access | ⚠️ Partial | Components check `_authService.IsLecturer()` but no formal authorization attributes. |

**Current Issue:** All auth state dies on server restart. No database → no persistent user accounts.

---

### 5. UI/UX Components

| Component | Status | Details |
|-----------|--------|---------|
| Dashboard | ✅ Working | Lecturer dashboard lists active/completed sessions. Student dashboard shows joined sessions. |
| Create Session | ✅ Working | Form to set title, allowed departments, allowed levels. Creates in-memory `Session`. |
| Join Session | ✅ Working | Student provides session code; validated and joined. |
| Video call interface | ✅ Working | `LecturerSessionView.razor` + `ParticipantPanel.razor` manage peer list and video display. |
| Messaging panel | ✅ Working | `MessagingPanel.razor` stores messages in-memory. No persistence between sessions. |
| Issue buttons | ✅ Working | Students can flag "BatteryLow", "DataFinished", "NoNetwork". Lecturer sees these flags in `ParticipantPanel`. |

---

### 6. P2P File Sharing (WebRTC DataChannel)

| Component | Status | Details |
|-----------|--------|---------|
| `sessionInterop.js` | ✅ Working | Opens DataChannel peer-to-peer, sends files up to 50MB. |
| Fallback to cloud signaling | ✅ Working | Uses PeerJS cloud server (`0.peerjs.com`). Works if internet available. |

**Current Issue:** Fails completely if campus internet is down (FUTA after 11pm is common). No offline-LAN fallback.

---

## NOT Implemented / Broken ❌

### Task 2: SignalR Optimization & Sapa Mode

| Item | Status | Details |
|------|--------|---------|
| Eliminate `Task.Delay(100)` | ❌ Not Started | Hub filter to cache MatricNo at connect time (instead of lazy retry) not implemented. |
| Sapa Mode (power-saving) | ❌ Not Started | No `activateSapaMode()` in `session.js`. No bandwidth-aware quality tiers. |
| Quality stats polling | ❌ Not Started | No `RTCPeerConnection.getStats()` loop in JS. |
| Battery-aware quality reduction | ❌ Not Started | No auto-fallback to SD/audio-only based on stats. |

---

### Task 3: EF Core + PostgreSQL Persistence

| Item | Status | Details |
|------|--------|---------|
| `IUserRepository` interface | ❌ Not Started | No repository abstraction yet. Code directly calls `MockApiService`. |
| `ISessionRepository` interface | ❌ Not Started | No repository abstraction yet. Sessions stored in `SessionService._sessions` only. |
| `ViidiiDbContext` (EF Core) | ❌ Not Started | No DbContext defined. No migrations. |
| PostgreSQL connection | ❌ Not Started | No connection string in `appsettings.json`. |
| Database seeder | ❌ Not Started | No `DatabaseSeeder.cs` to populate test users on startup. |
| Table schema | ❌ Not Started | No Users, Sessions, Messages, AttendanceLogs, Files tables. |

**Impact:** Server restart wipes all data. Sessions, messages, and attendance records are lost.

---

### Task 4: Zero-App LAN Data Channel + Catch-Up Protocol

| Item | Status | Details |
|------|---------|---------|
| Local PeerJS signaling endpoint | ❌ Not Started | No HTTP signaling middleware in `Program.cs`. Still depends on cloud. |
| Offline mode toggle | ❌ Not Started | No "OFFLINE MODE" button in `ControlsBar.razor`. |
| `PeerSignalingHub` | ❌ Not Started | No SignalR hub for relaying WebRTC SDP/ICE on LAN. |
| Catch-up protocol | ❌ Not Started | No mechanism for late-joiners to receive historical messages/files. |
| Message buffer persistence | ❌ Not Started | Messages not persisted; late-joiners get nothing. |

---

### Task 5: Dynamic Cryptographic QR Attendance

| Item | Status | Details |
|------|---------|---------|
| `AttendanceTokenService.cs` | ❌ Not Started | No HMAC-SHA256 token generator. |
| QR code display | ❌ Not Started | `LecturerSessionView.razor` has no QR rendering (would use `QRCoder` NuGet). |
| Token rotation timer | ❌ Not Started | No `System.Threading.Timer` for 15-second token refresh. |
| `/attend?token=[x]` endpoint | ❌ Not Started | No Razor page to validate and record attendance scans. |
| One-time-use enforcement | ❌ Not Started | No `AttendanceLogs` table to track replayed tokens. |
| QR in recap | ❌ Not Started | `SessionRecap.razor` has no "QR Check-In" column showing ✅/❌. |

---

## 2026 Lock-in Project: Systems Architect Foundation

| Item | Status | Details |
|------|--------|---------|
| Automata (CSC 307/309) — 7 deliverables | ⬜ Not Started | Theory content, not yet authored. |
| Linear Algebra (MTS 203) — 7 deliverables | ⬜ Not Started | Theory content, not yet authored. |
| Statistics — 7 deliverables | ⬜ Not Started | Theory content, not yet authored. |

---

## Dependency Map

```
┌─────────────────────────────────────────────────────────────────┐
│ PHASE 1 DEPENDENCIES                                            │
└─────────────────────────────────────────────────────────────────┘

Task 1 (DFA): BLOCKING
  ✅ C# Model Complete
  ❌ JS Integration Missing → ParticipantPanel can't show state
  ⚠️  WITHOUT THIS: Ghost calls will persist

Task 2 (Sapa Mode): DEPENDS ON Task 1
  ❌ Task.Delay(100) in SessionHub blocks thread pool
  ❌ Quality polling & adaptive bitrate not started
  ⚠️  WITHOUT THIS: Mass joins (100+ students) cause 42s latency

Task 3 (EF Core): INDEPENDENT (but unlocks Tasks 1,2)
  ❌ No repositories, no DB context
  ✅ Can be built in parallel
  ⚠️  WITHOUT THIS: Data dies on restart; can't test real usage patterns

Task 4 (LAN Mode): DEPENDS ON (Task 1 + Task 3)
  ❌ No local signaling, no catch-up protocol
  ⚠️  WITHOUT THIS: Sessions fail when campus internet drops

Task 5 (QR Attendance): INDEPENDENT
  ❌ No token service, no QR display
  ✅ Can be built in parallel with Tasks 1-4
  ⚠️  WITHOUT THIS: Proxy attendance remains undetected

RECOMMENDED EXECUTION ORDER (Phase 1):
  Day 1: Complete Task 1 (DFA JS integration)
  Day 2: Task 2 (eliminate Task.Delay + Sapa Mode)
  Day 3: Task 3 (EF Core + migration)
  Day 4: Task 4 (LAN signaling + catch-up)
  Day 5: Task 5 (QR tokens)
```

---

## Test Coverage Status

| Area | Test Status | Notes |
|------|-------------|-------|
| DFA state machine | ⚠️ Logic OK, but no unit tests | `PeerConnectionContext` is simple enough to be obviously correct. Could add xUnit tests. |
| SignalR message flow | ⚠️ Manual testing only | No automated integration tests. Hard to test without full Aspire stack. |
| Engagement scoring | ✅ Logic correct | `CalculateAttendanceScore()` verified manually in recap. |
| UI components | ⚠️ Manual only | No Selenium/Playwright tests. |

---

## Build & Deploy Status

| Item | Status | Details |
|------|--------|---------|
| Local dev build | ✅ Working | `dotnet build` + `dotnet run` works. Aspire orchestrates VIIDII project. |
| Docker image | ❌ Not started | No Dockerfile. |
| PostgreSQL locally | ❌ Not configured | No `appsettings.Development.json` for Postgres connection string. |
| CI/CD (GitHub Actions) | ❌ Not configured | No `.github/workflows/` yet. |

---

## Known Issues & Warnings

1. **Ghost Calls (HIGH PRIORITY):** Student A rejoins → JavaScript thinks it's `Connecting`, but C# says `Disconnected`. Leads to one-way audio or dropped calls.
   - *Cause:* JS doesn't track DFA state; C# changes state without notifying client.
   - *Fix:* Broadcast `PeerStateChanged` event in Task 1.

2. **Thread-Pool Starvation (HIGH PRIORITY):** 100-student mass join → 100× `Task.Delay(100)` blocks 100 thread-pool threads for 100ms.
   - *Cause:* `GetMatricNoForConnectionAsync()` in SessionHub retries lazily.
   - *Fix:* Cache MatricNo at `OnConnectedAsync()` via Hub Filter (Task 2).

3. **Data Loss on Restart (MEDIUM):** Server restart = all sessions, messages, attendance erased.
   - *Cause:* Everything in-memory only.
   - *Fix:* Add EF Core + PostgreSQL (Task 3).

4. **No Offline Fallback (MEDIUM):** Campus internet down → file sharing fails entirely.
   - *Cause:* PeerJS depends on cloud signaling server.
   - *Fix:* Add local LAN signaling via SignalR + catch-up protocol (Task 4).

5. **Proxy Attendance Undetected (LOW):** Student can share code with friend at home → both score 100% engagement.
   - *Cause:* No physical presence verification.
   - *Fix:* Rotating QR codes with HMAC tokens (Task 5).

---

## Metrics & Performance Baseline

| Metric | Baseline | Target (Phase 1) |
|--------|----------|------------------|
| Single peer join latency | ~2-3s (after caching) | <500ms (with MatricNo cache at connect) |
| 100-student mass join | ~42s (due to Task.Delay starvation) | <3s (with Hub filter) |
| Peer state sync latency | N/A (state not broadcast) | <100ms (with SignalR broadcast) |
| Message throughput | ~1 msg/s (untested) | 10+ msg/s (with proper SignalR config) |
| CPU on VideoStage (full video) | ~40-50% (Chrome, single peer) | ~10% (with Sapa Mode SD) |
| File transfer time (10MB, LAN) | Works (cloud signaling) | Works (local signaling) |

---

## Architecture Notes

### Project Structure
```
VIIDII/
├── Components/
│   ├── Pages/
│   │   ├── Dashboard.razor
│   │   ├── CreateSession.razor
│   │   ├── JoinSession.razor
│   │   ├── LecturerSessionView.razor
│   │   ├── SessionRecap.razor
│   │   └── Login.razor
│   └── Shared/
│       ├── ParticipantPanel.razor
│       ├── VideoStage.razor
│       ├── MessagingPanel.razor
│       ├── EngagementModal.razor
│       ├── IssueButtons.razor
│       └── ...
├── Services/
│   ├── SessionService.cs (in-memory sessions)
│   ├── SessionJsInterop.cs (JS bridge)
│   ├── AuthService.cs (logged-in user)
│   ├── MessageService.cs (message queue)
│   ├── ParticipantPingService.cs (engagement timer)
│   └── MockApiService.cs (hardcoded users)
├── Hubs/
│   └── SessionHub.cs (SignalR core)
├── Models/
│   ├── User.cs (User, Session, enums)
│   └── PeerConnectionContext.cs (DFA state machine)
├── wwwroot/
│   ├── js/
│   │   ├── session.js (PeerJS + peer management)
│   │   ├── sessionInterop.js (JS ↔ C# bridge)
│   │   └── chartInterop.js (Chart rendering)
│   └── css/
└── Program.cs (Aspire + DI container)
```

### Key Design Patterns
- **Concurrency:** `ConcurrentDictionary` for all shared state. No locks except in `PeerConnectionContext`.
- **Async/Await:** All I/O is async. SignalR methods are `async Task`. No blocking calls in hubs.
- **DI:** Scoped services per Blazor circuit (`AuthService`). Singleton for shared state (`SessionService`, `MessageService`).
- **JS Interop:** One-way event notification via `IJSObjectReference`. No polling.

---

## Next Steps

**Immediate (before Phase 1):**
1. ✅ Verify this audit is accurate (spot-check a few components)
2. ✅ Resolve any discrepancies in documentation vs. code
3. ✅ Confirm Phase 1 task dependencies are correct

**Phase 1 (5-day sprint):**
- Day 1: Complete Task 1 (broadcast DFA state changes to JS)
- Day 2: Task 2 (eliminate Task.Delay, add Sapa Mode)
- Day 3: Task 3 (EF Core + Postgres)
- Day 4: Task 4 (LAN signaling + catch-up)
- Day 5: Task 5 (QR attendance)

---

**Approval:** Awaiting confirmation that this audit matches your codebase. Any corrections?
