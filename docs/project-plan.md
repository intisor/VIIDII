# Project: VIIDII Phase 1 — MVP Hardening & Stabilization

**Description:** Harden the existing VIIDII WebRTC video conferencing MVP against FUTA campus network reality. Eliminate ghost calls, fix thread-pool starvation on mass joins, add database persistence, enable offline file sharing, and implement cryptographic attendance verification. A 5-day sprint that transforms a working prototype into a production-ready platform.

**Owner:** @intisor

**Visibility:** public

**Repository:** https://github.com/intisor/VIIDII

**Target Framework:** .NET 10

**Sprint Duration:** 5 days

---

## Milestone: Day 1 — WebRTC State Machine JS Integration

**Due Date:** 2026-02-XX (Day 1 of sprint)

**Description:** Broadcast DFA state changes from C# to JavaScript. Eliminate ghost-call conditions where one peer thinks it's connected but the other doesn't. Sync WebRTC peer connection states across all clients in real-time.

### Task: Architect WebRTC State Machine JS Integration

**Priority:** Critical

**Complexity:** 5

**Assignee:** @intisor

**Category:** 🏗️ Portfolio Build

**FUTA Course:** CSC309 (Automata Theory)

**Content Output:** 📹 YouTube/Twitch — "I Modeled WebRTC as a Finite State Machine to Kill Ghost Calls"

**Description:**

The `PeerConnectionContext.cs` DFA model exists and is enforced in C#, but JavaScript doesn't know about state transitions. This causes:
- Student A reconnects → C# transitions peer to `Signaling`
- Student B's JS still thinks peer is `Connected`
- Result: One-way audio or dropped calls ("ghost calls")

**Implementation Details:**

**Part A — Server-side state broadcast:**
- `[NEW]` `Services/PeerStateService.cs` — ConcurrentDictionary<peerId, PeerState>. Provides PublishStateChangeAsync(peerId, newState).
- `[MODIFY]` `Services/SessionService.cs` — When TryTransitionPeer() succeeds, call _peerStateService.PublishStateChangeAsync().
- `[MODIFY]` `Hubs/SessionHub.cs` — Add NotifyPeerStateChanged(peerId, newState) hub method. Broadcast to session participants.
- `[MODIFY]` `Program.cs` — Register PeerStateService as singleton.

**Part B — Client-side state tracking:**
- `[MODIFY]` `wwwroot/js/session.js` — Add window.viidii.peerStates Map. Add transitionPeerState(peerId, newState) function.
- `[MODIFY]` `wwwroot/js/session.js` — Wrap all peer.call(peerId, stream) with canStartCall(peerId) guard.
- `[MODIFY]` `Services/SessionJsInterop.cs` — Add [JSInvokable] OnPeerStateChanged(peerId, newState).
- `[MODIFY]` `Components/Shared/ParticipantPanel.razor` — Show per-student state badge (🟢 Connected, 🟡 Connecting, 🔴 Disconnected, ⚠️ Degraded).

**Acceptance Criteria:**
- Zero ghost calls in 5-student test where one student disconnects/reconnects 3 times
- ParticipantPanel shows correct state badge for each peer
- session.js correctly answers "Can I call peer X?" after network drop
- No console errors about invalid state transitions
- Load test: 20 simultaneous peers, 5 transitions each. All states sync within 100ms.

**Files to Change:**
- `[NEW]` Services/PeerStateService.cs
- `[MODIFY]` Services/SessionService.cs
- `[MODIFY]` Hubs/SessionHub.cs
- `[MODIFY]` wwwroot/js/session.js
- `[MODIFY]` Services/SessionJsInterop.cs
- `[MODIFY]` Components/Shared/ParticipantPanel.razor
- `[MODIFY]` Program.cs

---

## Milestone: Day 2 — SignalR Optimization & Sapa Mode

**Due Date:** 2026-02-XX (Day 2 of sprint)

**Description:** Eliminate thread-pool starvation on mass joins (100+ students). Add power-saving "Sapa Mode" that reduces bandwidth and CPU on low-battery students.

### Task: Fix SignalR Join Storm + Implement Sapa Mode

**Priority:** Critical

**Complexity:** 5

**Assignee:** @intisor

**Category:** 🏗️ Portfolio Build

**FUTA Course:** CSC305 (System Programming — thread pools, concurrency)

**Content Output:** 📹 YouTube/Twitch — "How I Fixed Thread-Pool Starvation in ASP.NET SignalR (100 Users)"

**Description:**

**Problem A — Thread-Pool Starvation:**
SessionHub.GetMatricNoForConnectionAsync() contains Task.Delay(100) retry that runs per hub method call. When 100 students join simultaneously, 100 threads block for 100ms → thread-pool exhaustion → "Connecting..." for 42 seconds.

**Problem B — No Power-Saving:**
Video stays HD even when student flags "BatteryLow". Students with 5% battery drain in 30 minutes.

**Implementation Details:**

**Part A — Eliminate Task.Delay:**
- `[NEW]` `Hubs/MatricNoCachingFilter.cs` — IHubFilter. On OnConnectedAsync(), reads MatricNo from query string and caches immediately.
- `[MODIFY]` `Hubs/SessionHub.cs` — Remove Task.Delay(100) loop from GetMatricNoForConnectionAsync(). Assume MatricNo always cached.
- `[MODIFY]` `Program.cs` — Register MatricNoCachingFilter as global Hub filter.
- `[MODIFY]` `Components/Pages/LecturerSessionView.razor` & `JoinSession.razor` — Pass MatricNo to SignalR connection via query string.

**Part B — Sapa Mode (power-saving):**
- `[NEW]` `Services/QualityService.cs` — Monitors RTCPeerConnection.getStats(). Decides quality tier: HD (1080p, 2.5Mbps), SD (720p, 800kbps), Audio-only.
- `[MODIFY]` `wwwroot/js/session.js` — Add activateSapaMode() function. Stops incoming video tracks. Polls getStats() every 5s.
- `[MODIFY]` `Services/SessionJsInterop.cs` — Add ActivateSapaModeAsync(), ApplyQualityTierAsync(tier), GetPeerStatsAsync(peerId).
- `[MODIFY]` `Components/Shared/VideoStage.razor` — Add Sapa Mode banner (🔋 "Saving battery. Video off.").
- `[MODIFY]` `Components/Shared/ParticipantPanel.razor` — Show 🔋 icon for students in Sapa Mode.
- `[MODIFY]` `Components/Shared/IssueButtons.razor` — BatteryLow button calls ActivateSapaModeAsync().
- `[MODIFY]` `Hubs/SessionHub.cs` — Add NotifyQualityTierChanged(peerId, tier) hub method.

**Acceptance Criteria:**
- 100-student mass join completes in <3 seconds (no thread delay)
- Sapa Mode reduces CPU usage by ≥80% when enabled
- Bandwidth monitor correctly identifies "Degraded" network
- Quality tier auto-downgrades to SD when packet loss >10%
- Load test: 100 simultaneous joins + 5 random quality changes. No thread hangs.

**Files to Change:**
- `[NEW]` Hubs/MatricNoCachingFilter.cs
- `[NEW]` Services/QualityService.cs
- `[MODIFY]` Hubs/SessionHub.cs
- `[MODIFY]` wwwroot/js/session.js
- `[MODIFY]` Services/SessionJsInterop.cs
- `[MODIFY]` Components/Shared/VideoStage.razor
- `[MODIFY]` Components/Shared/ParticipantPanel.razor
- `[MODIFY]` Components/Shared/IssueButtons.razor
- `[MODIFY]` Program.cs
- `[MODIFY]` Components/Pages/LecturerSessionView.razor
- `[MODIFY]` Components/Pages/JoinSession.razor

---

## Milestone: Day 3 — EF Core + PostgreSQL Persistence

**Due Date:** 2026-02-XX (Day 3 of sprint)

**Description:** Replace in-memory mocks with real database. Survive server restarts. Enable session history and attendance records.

### Task: Migrate from MockApiService to EF Core + PostgreSQL

**Priority:** Critical

**Complexity:** 5

**Assignee:** @intisor

**Category:** 🏗️ Portfolio Build

**FUTA Course:** SEN204 (Requirements Engineering — data persistence, entity modeling)

**Content Output:** 📝 Substack — "Migrating from In-Memory Mocks to EF Core + PostgreSQL in a Production Blazor App"

**Description:**

All user, session, and message data lives in static MockApiService or in-memory SessionService. Server restart = data loss. No way to retrieve past sessions or attendance.

**Implementation Details:**

**Step A — Repository abstraction (wraps existing code):**
- `[NEW]` `Services/Interfaces/IUserRepository.cs` — GetAllAsync(), GetByMatricNoAsync(matricNo), CreateAsync(user), UpdateAsync(user)
- `[NEW]` `Services/Interfaces/ISessionRepository.cs` — GetAllAsync(), GetByIdAsync(sessionId), CreateAsync(session), UpdateAsync(session), EndAsync(sessionId)
- `[NEW]` `Services/Repositories/MockUserRepository.cs` — Wraps MockApiService. Implements IUserRepository.
- `[NEW]` `Services/Repositories/InMemorySessionRepository.cs` — Wraps SessionService._sessions. Implements ISessionRepository.

**Step B — EF Core models:**
- `[NEW]` `Data/Models/UserEntity.cs` — Maps User to EF table
- `[NEW]` `Data/Models/SessionEntity.cs` — Maps Session to table
- `[NEW]` `Data/Models/MessageEntity.cs` — Maps message to table
- `[NEW]` `Data/Models/AttendanceLogEntity.cs` — Attendance event log
- `[NEW]` `Data/ViidiiDbContext.cs` — DbContext with DbSets
- `[NEW]` `Data/Configuration/UserEntityTypeConfiguration.cs` — Fluent API
- `[NEW]` `Data/Configuration/SessionEntityTypeConfiguration.cs` — Fluent API

**Step C — EF Core repositories:**
- `[NEW]` `Services/Repositories/EfUserRepository.cs` — Implements IUserRepository using EF
- `[NEW]` `Services/Repositories/EfSessionRepository.cs` — Implements ISessionRepository using EF
- `[NEW]` `Services/Repositories/EfMessageRepository.cs` — Implements IMessageRepository using EF
- `[NEW]` `Services/Repositories/EfAttendanceLogRepository.cs` — Logs attendance events

**Step D — Migrations & seeding:**
- `[NEW]` `Migrations/20260203_InitialCreate.cs` — EF Core auto-generated migration
- `[NEW]` `Data/DatabaseSeeder.cs` — Seeds test users on startup (Development mode)

**Step E — Integrate into services:**
- `[MODIFY]` `Hubs/SessionHub.cs` — Inject IUserRepository, ISessionRepository
- `[MODIFY]` `Services/SessionService.cs` — Inject ISessionRepository, persist to DB
- `[MODIFY]` `Components/Pages/SessionRecap.razor` — Load from DB
- `[MODIFY]` `Components/Pages/Dashboard.razor` — Load from DB
- `[MODIFY]` `Program.cs` — Register EF Core, DbContext, repositories, seeder
- `[MODIFY]` `appsettings.json` — Add PostgreSQL connection string
- `[NEW]` `appsettings.Development.json` — Local Postgres connection
- `[NEW]` `appsettings.Production.json` — Prod Postgres connection

**Acceptance Criteria:**
- dotnet ef migrations add InitialCreate succeeds with no errors
- Server restart does not lose session data
- SessionRecap.razor can load recap for sessions that ended before current server instance
- Create 3 sessions, end them, restart server → all 3 visible in Dashboard
- Load test: 50 concurrent sessions, 500 messages → DB commits in <1ms per operation

**Files to Change:**
- `[NEW]` Services/Interfaces/IUserRepository.cs
- `[NEW]` Services/Interfaces/ISessionRepository.cs
- `[NEW]` Services/Interfaces/IMessageRepository.cs
- `[NEW]` Services/Repositories/MockUserRepository.cs
- `[NEW]` Services/Repositories/InMemorySessionRepository.cs
- `[NEW]` Services/Repositories/EfUserRepository.cs
- `[NEW]` Services/Repositories/EfSessionRepository.cs
- `[NEW]` Services/Repositories/EfMessageRepository.cs
- `[NEW]` Services/Repositories/EfAttendanceLogRepository.cs
- `[NEW]` Data/Models/UserEntity.cs
- `[NEW]` Data/Models/SessionEntity.cs
- `[NEW]` Data/Models/MessageEntity.cs
- `[NEW]` Data/Models/AttendanceLogEntity.cs
- `[NEW]` Data/ViidiiDbContext.cs
- `[NEW]` Data/Configuration/UserEntityTypeConfiguration.cs
- `[NEW]` Data/Configuration/SessionEntityTypeConfiguration.cs
- `[NEW]` Data/DatabaseSeeder.cs
- `[NEW]` Migrations/20260203_InitialCreate.cs
- `[MODIFY]` Hubs/SessionHub.cs
- `[MODIFY]` Services/SessionService.cs
- `[MODIFY]` Components/Pages/SessionRecap.razor
- `[MODIFY]` Components/Pages/Dashboard.razor
- `[MODIFY]` Program.cs
- `[MODIFY]` appsettings.json
- `[NEW]` appsettings.Development.json
- `[NEW]` appsettings.Production.json

---

## Milestone: Day 4 — Zero-App LAN Data Channel + Catch-Up Protocol

**Due Date:** 2026-02-XX (Day 4 of sprint)

**Description:** Enable offline file sharing when campus internet is down. Late-joining students receive full message history.

### Task: Implement LAN Signaling + Catch-Up Protocol

**Priority:** High

**Complexity:** 4

**Assignee:** @intisor

**Category:** 🏗️ Portfolio Build

**FUTA Course:** CSC307 (Data Communications — P2P protocols, DataChannel, LAN topology)

**Content Output:** 🐦 Twitter/X — "Shipped offline campus file sharing. No internet. No app. Just WebRTC Data Channels 🔥"

**Description:**

File sharing depends on PeerJS cloud signaling server. Campus internet down (common after 11pm) → file sharing broken. Late-joining students miss all prior messages and files.

**Implementation Details:**

**Part A — Local PeerJS signaling:**
- `[NEW]` `Hubs/PeerSignalingHub.cs` — SignalR hub that relays WebRTC SDP and ICE candidates on LAN. Methods: SendOfferAsync(), SendAnswerAsync(), SendIceCandidateAsync().
- `[MODIFY]` `wwwroot/js/session.js` — createPeer() factory checks window.viidiiOfflineMode. Uses local PeerSignalingHub vs. cloud 0.peerjs.com. Graceful fallback.
- `[MODIFY]` `Components/Shared/ControlsBar.razor` — Add "OFFLINE MODE" toggle button.

**Part B — Catch-up protocol:**
- `[MODIFY]` `Components/Shared/MessagingPanel.razor` — Maintain _localMessageBuffer serializable to JSON.
- `[MODIFY]` `Hubs/SessionHub.cs` — Add NotifyLatePeerJoinedAsync(sessionId, latePeerId) hub method.
- `[MODIFY]` `wwwroot/js/session.js` — Add catch-up sender/receiver. On LatePeerJoined: send {type: 'catchup', messages: [...], fileManifest: [...]}.
- `[MODIFY]` `Services/SessionJsInterop.cs` — Add [JSInvokable] OnCatchUpReceivedAsync(JsonElement data).
- `[MODIFY]` `Components/Shared/MessagingPanel.razor` — Handle catch-up message injection.

**Acceptance Criteria:**
- 10MB PDF file transfer completes to 5 students with internet disconnected
- Late-joining student receives full chat history within 5 seconds
- Message says "📩 Files received from peer: document.pdf"
- Toggle Offline Mode on/off without restart
- Graceful fallback to cloud signaling if local fails

**Files to Change:**
- `[NEW]` Hubs/PeerSignalingHub.cs
- `[MODIFY]` wwwroot/js/session.js
- `[MODIFY]` Services/SessionJsInterop.cs
- `[MODIFY]` Components/Shared/ControlsBar.razor
- `[MODIFY]` Components/Shared/MessagingPanel.razor
- `[MODIFY]` Hubs/SessionHub.cs
- `[MODIFY]` Program.cs

---

## Milestone: Day 5 — Dynamic Cryptographic QR Attendance

**Due Date:** 2026-02-XX (Day 5 of sprint)

**Description:** Prove physical presence in lecture theatre via rotating HMAC QR codes. Block proxy attendance.

### Task: Implement QR Attendance Tokens

**Priority:** High

**Complexity:** 4

**Assignee:** @intisor

**Category:** 🏗️ Portfolio Build

**FUTA Course:** Cryptography (HMAC, time-window tokens, replay detection)

**Content Output:** 🐦 Twitter/X — "Built cryptographic QR attendance with HMAC-SHA256 rotating tokens. Proxy attendance = Caught 🚫"

**Description:**

Attendance based only on engagement modal responses. Student A can share session code with Student B (at home) → both score 100% engagement. No physical presence verification.

Solution: Rotating HMAC-SHA256 QR code on lecturer's screen. Rotates every 15 seconds. One scan per student per session. Replayed tokens rejected.

**Implementation Details:**

- `[NEW]` NuGet: Add `QRCoder` to VIIDII.csproj
- `[NEW]` `Services/AttendanceTokenService.cs` — HMAC generation, validation, one-time-use enforcement. Uses Attendance:SecretKey from config.
- `[NEW]` `Components/Pages/AttendanceScan.razor` — Razor page `/attend?token=[x]`. Validates token, records log, shows "✅ Checked in!" or "❌ Token expired".
- `[MODIFY]` `Components/Pages/LecturerSessionView.razor` — Add QR display panel. Uses System.Threading.Timer to refresh every 15 seconds. Renders SVG using QRCoder. Shows countdown "QR expires in: 14s".
- `[MODIFY]` `Components/Pages/SessionRecap.razor` — Add "QR Check-In" column to attendance table (✅ scanned, ❌ not scanned).
- `[MODIFY]` `Program.cs` — Register AttendanceTokenService.
- `[MODIFY]` `appsettings.json` — Add Attendance section with SecretKey, WindowSeconds, MaxReplaysPerStudent.
- `[NEW]` `Data/Models/AttendanceScanEntity.cs` — New table in DB
- `[MODIFY]` `Data/ViidiiDbContext.cs` — Add AttendanceLogs DbSet
- `[NEW]` `Migrations/20260205_AddAttendanceLogs.cs` — EF Core migration

**Acceptance Criteria:**
- QR code displays on lecturer screen without lag
- QR rotates exactly every 15 seconds
- Student scans QR → redirected to /attend → shows "✅ Checked in"
- Student scans old QR → shows "❌ Token expired"
- Same student scans same valid QR twice → second scan rejected
- Attendance recap shows ✅/❌ for each student
- Token generation takes <1ms

**Files to Change:**
- `[NEW]` Services/AttendanceTokenService.cs
- `[NEW]` Components/Pages/AttendanceScan.razor
- `[MODIFY]` Components/Pages/LecturerSessionView.razor
- `[MODIFY]` Components/Pages/SessionRecap.razor
- `[MODIFY]` Program.cs
- `[MODIFY]` appsettings.json
- `[NEW]` Data/Models/AttendanceScanEntity.cs
- `[MODIFY]` Data/ViidiiDbContext.cs
- `[NEW]` Migrations/20260205_AddAttendanceLogs.cs

---

## Summary

| Day | Task | Priority | Complexity | Status |
|-----|------|----------|-----------|--------|
| 1 | WebRTC DFA JS Integration | Critical | 5 | ⬜ Pending |
| 2 | SignalR Optimization + Sapa Mode | Critical | 5 | ⬜ Pending |
| 3 | EF Core + PostgreSQL | Critical | 5 | ⬜ Pending |
| 4 | LAN Data Channel + Catch-Up | High | 4 | ⬜ Pending |
| 5 | QR Attendance Tokens | High | 4 | ⬜ Pending |

**Phase 1 Goal:** Transform working MVP into production-ready platform. Eliminate ghost calls, fix thread-pool starvation, add persistence, enable offline mode, implement attendance verification.

**Go-Live:** End of Day 5 → Deploy to FUTA staging/production

---

**Last Updated:** 2026-02-XX  
**Status:** Ready for GitHub Issues generation
