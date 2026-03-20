# VIIDII PROJECT ROADMAP

**Status:** Phase 1 (MVP Hardening) — 5-day sprint  
**After Phase 1:** 2026 Lock-in (Systems Architect Foundation) — 21 theory deliverables  
**Repository:** https://github.com/intisor/VIIDII

---

## Phase 1: MVP Hardening & Stabilization (5 Days)

**Goal:** Eliminate ghost calls, fix thread-pool starvation, add database persistence, add offline fallback, add attendance verification.

**What we are NOT doing:** Rewriting engagement engine, SignalR hub architecture, or recap system. They work. We're hardening + extending.

---

## Phase 1 Tasks (Day 1–5)

### 📌 Day 1: Complete WebRTC State Machine JS Integration

**Epic:** Broadcast DFA state changes from C# to JavaScript. Ensure every peer connection state is synchronized across all clients. Eliminate ghost-call conditions.

**What is broken today:**
- `PeerConnectionContext.cs` defines the 6-state DFA and is enforced in C#.
- JavaScript (`session.js`) does NOT know about these state changes.
- Student A reconnects → C# transitions peer to `Signaling`, but Student B's JS still thinks peer is `Connected`.
- Result: One-way audio, dropped calls, "phantom" peer objects that don't respond.

**What we are building:**

**Part A — Server-side state broadcast:**
- `[NEW]` `Services/PeerStateService.cs` — Manages a `ConcurrentDictionary<peerId, PeerState>` shared across all sessions. Provides `PublishStateChangeAsync(peerId, newState)` to broadcast to all connected clients.
- `[MODIFY]` `Services/SessionService.cs` — When `TryTransitionPeer()` succeeds, call `_peerStateService.PublishStateChangeAsync(peerId, newState)`.
- `[MODIFY]` `Hubs/SessionHub.cs` — Add `NotifyPeerStateChanged(string peerId, PeerState newState)` hub method. Broadcast to all session participants via group.
- `[MODIFY]` `Program.cs` — Register `PeerStateService` as singleton.

**Part B — Client-side state tracking + guard:**
- `[MODIFY]` `wwwroot/js/session.js` — Add `window.viidii.peerStates = new Map()` to track local peer state. Add `transitionPeerState(peerId, newState)` function that validates transitions before allowing operations.
- `[MODIFY]` `wwwroot/js/session.js` — Wrap all `peer.call(peerId, stream)` with `if (canStartCall(peerId))` check using local state.
- `[MODIFY]` `Services/SessionJsInterop.cs` — Add `[JSInvokable] OnPeerStateChanged(string peerId, string newState)` method. Invoked when hub broadcasts state.
- `[MODIFY]` `Components/Shared/ParticipantPanel.razor` — Show per-student state badge:
  - 🟢 Connected (green circle)
  - 🟡 Connecting (yellow circle)
  - 🔴 Disconnected (red circle)
  - ⚠️ Degraded (orange circle)

**Files to change:**
```
[NEW]    Services/PeerStateService.cs
[MODIFY] Services/SessionService.cs → call PublishStateChangeAsync()
[MODIFY] Hubs/SessionHub.cs → add NotifyPeerStateChanged hub method
[MODIFY] wwwroot/js/session.js → add peerStates map, transitionPeerState()
[MODIFY] Services/SessionJsInterop.cs → add [JSInvokable] OnPeerStateChanged()
[MODIFY] Components/Shared/ParticipantPanel.razor → show state badge
[MODIFY] Program.cs → register PeerStateService
```

**Acceptance Criteria:**
- ✅ Zero ghost calls in 5-student test where one student disconnects/reconnects 3 times
- ✅ `ParticipantPanel` shows correct state badge for each peer
- ✅ `session.js` can answer: "Can I call peer X?" correctly even after network drop
- ✅ No console errors about invalid state transitions
- ✅ Load test: 20 simultaneous peers, each transitions 5 times. All states sync within 100ms.

**Priority:** Critical  
**Effort:** 5 (Full day)  
**Category:** 🏗️ Portfolio Build  
**FUTA Course:** CSC309 (Automata Theory — modeling systems as DFAs)  
**Content Output:** 📹 YouTube/Twitch — "I Modeled WebRTC as a Finite State Machine to Fix Ghost Calls"  
**Assignee:** @intisor

---

### 📌 Day 2: SignalR Optimization + Sapa Mode (Power-Saving)

**Epic:** Eliminate thread-pool starvation on mass joins (100+ students). Add power-saving mode that reduces bandwidth and CPU on low-battery students.

**What is broken today:**
- `SessionHub.GetMatricNoForConnectionAsync()` contains `Task.Delay(100)` retry that fires **per hub method call**.
- When 100 students join simultaneously, each call triggers the delay → 100 threads blocked for 100ms.
- Result: Thread-pool starvation, "Connecting..." spinner for 42 seconds.
- No power-saving: Video stays HD even when student flags "BatteryLow".

**What we are building:**

**Part A — Eliminate Task.Delay retry:**
- `[NEW]` `Hubs/MatricNoCachingFilter.cs` — Implements `IHubFilter`. On `OnConnectedAsync()`, reads MatricNo from query string (or auth claim) and caches immediately in `_connectionMatricNos`.
- `[MODIFY]` `Hubs/SessionHub.cs` — Remove the entire `Task.Delay(100)` loop from `GetMatricNoForConnectionAsync()`. Assume MatricNo is always cached (filter guarantees it).
- `[MODIFY]` `Program.cs` — Register `MatricNoCachingFilter` as a global Hub filter. Add query string with MatricNo to SignalR endpoint config.
- `[MODIFY]` `Components/Pages/LecturerSessionView.razor` & `Components/Pages/JoinSession.razor` — Pass MatricNo to SignalR hub connection via query string: `connection.start({ MatricNo: currentMatricNo })`.

**Part B — Sapa Mode (power-saving):**
- `[NEW]` `Services/QualityService.cs` — Monitors `RTCPeerConnection.getStats()` for bitrate, RTT, packet loss. Decides quality tier:
  - Tier 1: HD (1080p, 2.5 Mbps) — normal
  - Tier 2: SD (720p, 800 kbps) — degraded network or battery low
  - Tier 3: Audio-only (no video) — critical battery or network
  - Exposes `GetRecommendedQualityTierAsync(peerId)`.
- `[MODIFY]` `wwwroot/js/session.js` — Add `activateSapaMode()` function:
  - Stops all incoming `MediaStreamTrack` objects (stops video decode).
  - Removes video element from DOM.
  - Starts polling `RTCPeerConnection.getStats()` every 5 seconds.
  - Calls `window.viidii.onQualityChange(tier)` → C# interop.
- `[MODIFY]` `Services/SessionJsInterop.cs` — Add:
  - `ActivateSapaModeAsync()` → calls JS `activateSapaMode()`.
  - `ApplyQualityTierAsync(tier)` → tells JS which bitrate/resolution to target.
  - `GetPeerStatsAsync(peerId)` → reads peer RTCStats from JS.
- `[MODIFY]` `Components/Shared/VideoStage.razor` — Add Sapa Mode banner (🔋 "Saving battery. Video off." with toggle to re-enable).
- `[MODIFY]` `Components/Shared/ParticipantPanel.razor` — Show 🔋 icon next to student name if in Sapa Mode.
- `[MODIFY]` `Components/Shared/IssueButtons.razor` — "BatteryLow" button calls `ActivateSapaModeAsync()` automatically.
- `[MODIFY]` `Hubs/SessionHub.cs` — Add `NotifyQualityTierChanged(peerId, tier)` hub method broadcast when lecturer or student triggers quality change.

**Files to change:**
```
[NEW]    Hubs/MatricNoCachingFilter.cs
[NEW]    Services/QualityService.cs
[MODIFY] Hubs/SessionHub.cs → remove Task.Delay, add NotifyQualityTierChanged
[MODIFY] wwwroot/js/session.js → activateSapaMode(), quality polling
[MODIFY] Services/SessionJsInterop.cs → ActivateSapaModeAsync, ApplyQualityTierAsync, GetPeerStatsAsync
[MODIFY] Components/Shared/VideoStage.razor → add Sapa Mode banner
[MODIFY] Components/Shared/ParticipantPanel.razor → show 🔋 icon
[MODIFY] Components/Shared/IssueButtons.razor → wire BatteryLow to Sapa Mode
[MODIFY] Program.cs → register MatricNoCachingFilter, QualityService
[MODIFY] Components/Pages/LecturerSessionView.razor → pass MatricNo to SignalR connection
[MODIFY] Components/Pages/JoinSession.razor → pass MatricNo to SignalR connection
```

**Acceptance Criteria:**
- ✅ 100-student mass join completes in <3 seconds (no thread-pool delay)
- ✅ Sapa Mode reduces CPU usage by ≥80% when enabled (Chrome Task Manager)
- ✅ Bandwidth monitor correctly identifies "Degraded" network (high RTT/loss)
- ✅ Quality tier auto-downgraded to SD when packet loss >10%
- ✅ Load test: 100 simultaneous joins + 5 random quality tier changes. No thread hangs.

**Priority:** Critical  
**Effort:** 5 (Full day)  
**Category:** 🏗️ Portfolio Build  
**FUTA Course:** CSC305 (System Programming — thread pools, concurrency, kernel I/O scheduling)  
**Content Output:** 📹 YouTube/Twitch — "How I Fixed Thread-Pool Starvation in ASP.NET SignalR (100 Users)"  
**Assignee:** @intisor

---

### 📌 Day 3: EF Core + PostgreSQL — Persistent Data Layer

**Epic:** Replace in-memory mocks with real database. Survive server restarts. Enable real-world session history and reporting.

**What is broken today:**
- All user, session, and message data lives in `MockApiService` (static list) or `SessionService` (in-memory dict).
- Server restart = data loss.
- No way to retrieve past sessions or attendance records.

**What we are building:**

**Step A — Repository abstraction (wraps existing code, no breaking changes):**
- `[NEW]` `Services/Interfaces/IUserRepository.cs` → `GetAllAsync()`, `GetByMatricNoAsync(matricNo)`, `CreateAsync(user)`, `UpdateAsync(user)`
- `[NEW]` `Services/Interfaces/ISessionRepository.cs` → `GetAllAsync()`, `GetByIdAsync(sessionId)`, `CreateAsync(session)`, `UpdateAsync(session)`, `EndAsync(sessionId)`
- `[NEW]` `Services/Repositories/MockUserRepository.cs` — Wraps `MockApiService.GetUsers()`. Implements `IUserRepository`.
- `[NEW]` `Services/Repositories/InMemorySessionRepository.cs` — Wraps `SessionService._sessions`. Implements `ISessionRepository`.

**Step B — EF Core models & DbContext:**
- `[NEW]` `Data/Models/UserEntity.cs` — Maps `User` domain model to EF table. Includes hashed password, role, department, level.
- `[NEW]` `Data/Models/SessionEntity.cs` — Maps `Session` to table. Includes lecturer ID, title, start/end times, status, participant IDs (as JSON array).
- `[NEW]` `Data/Models/MessageEntity.cs` — Maps message to table. Includes session ID, sender MatricNo, content, timestamp.
- `[NEW]` `Data/Models/AttendanceLogEntity.cs` — Attendance event log (for Phase 1 Task 5 later). Tracks join, ping-response, battery-low flags.
- `[NEW]` `Data/ViidiiDbContext.cs` — DbContext with DbSets for users, sessions, messages, attendance logs. Connection string from config.
- `[NEW]` `Data/Configuration/UserEntityTypeConfiguration.cs` — Fluent API for `UserEntity` (string lengths, uniqueness constraints).
- `[NEW]` `Data/Configuration/SessionEntityTypeConfiguration.cs` — Fluent API for `SessionEntity` (composite keys, relationships).

**Step C — EF Core repositories (backed by database):**
- `[NEW]` `Services/Repositories/EfUserRepository.cs` — Implements `IUserRepository` using EF Core + database.
- `[NEW]` `Services/Repositories/EfSessionRepository.cs` — Implements `ISessionRepository` using EF Core + database.
- `[NEW]` `Services/Repositories/EfMessageRepository.cs` — Implements `IMessageRepository` (new interface) using EF.
- `[NEW]` `Services/Repositories/EfAttendanceLogRepository.cs` — Logs attendance events.

**Step D — Migrations & seeding:**
- `[NEW]` Database migrations (auto-generated by EF Core):
  - `Migrations/20260203_InitialCreate.cs` — Creates all tables.
- `[NEW]` `Data/DatabaseSeeder.cs` — On startup (Development mode only), reads `MockApiService` and seeds DB with test users if DB is empty.
- `[MODIFY]` `Program.cs` — Register EF Core, DbContext, repositories. Call seeder on startup.

**Step E — Integrate repositories into services:**
- `[MODIFY]` `Hubs/SessionHub.cs` — Inject `IUserRepository`, `ISessionRepository`. Replace `MockApiService.GetLecturers()` with `_userRepository.GetAllAsync()`.
- `[MODIFY]` `Services/SessionService.cs` — Inject `ISessionRepository`. Persist sessions to DB on `CreateSession()`, `JoinSession()`, `EndSession()`.
- `[MODIFY]` `Components/Pages/SessionRecap.razor` — Inject `ISessionRepository`, `IUserRepository`. Load session data from DB (can retrieve past sessions).
- `[MODIFY]` `Components/Pages/Dashboard.razor` — Load sessions from DB instead of in-memory.
- `[MODIFY]` `Components/Pages/LecturerHome.razor` — Load past sessions from DB.

**Files to change:**
```
[NEW]    Services/Interfaces/IUserRepository.cs
[NEW]    Services/Interfaces/ISessionRepository.cs
[NEW]    Services/Interfaces/IMessageRepository.cs
[NEW]    Services/Repositories/MockUserRepository.cs
[NEW]    Services/Repositories/InMemorySessionRepository.cs
[NEW]    Services/Repositories/EfUserRepository.cs
[NEW]    Services/Repositories/EfSessionRepository.cs
[NEW]    Services/Repositories/EfMessageRepository.cs
[NEW]    Services/Repositories/EfAttendanceLogRepository.cs
[NEW]    Data/Models/UserEntity.cs
[NEW]    Data/Models/SessionEntity.cs
[NEW]    Data/Models/MessageEntity.cs
[NEW]    Data/Models/AttendanceLogEntity.cs
[NEW]    Data/ViidiiDbContext.cs
[NEW]    Data/Configuration/UserEntityTypeConfiguration.cs
[NEW]    Data/Configuration/SessionEntityTypeConfiguration.cs
[NEW]    Data/DatabaseSeeder.cs
[NEW]    Migrations/20260203_InitialCreate.cs (auto-generated)
[MODIFY] Hubs/SessionHub.cs → inject IUserRepository, use repos
[MODIFY] Services/SessionService.cs → inject ISessionRepository, persist
[MODIFY] Components/Pages/SessionRecap.razor → load from DB
[MODIFY] Components/Pages/Dashboard.razor → load from DB
[MODIFY] Components/Pages/LecturerHome.razor → load from DB
[MODIFY] Program.cs → register EF Core, DbContext, repos, seeder
[MODIFY] appsettings.json → add PostgreSQL connection string
[NEW]    appsettings.Development.json → local Postgres connection
[NEW]    appsettings.Production.json → prod Postgres connection
```

**Acceptance Criteria:**
- ✅ `dotnet ef migrations add InitialCreate` succeeds with no errors
- ✅ Server restart does not lose session data
- ✅ `SessionRecap.razor` can load recap for sessions that ended before current server instance
- ✅ Create 3 sessions, end them, restart server → all 3 visible in Dashboard
- ✅ Load test: 50 concurrent sessions, 500 messages → DB commits complete in <1ms per operation (no N+1 queries)

**Priority:** Critical  
**Effort:** 5 (Full day)  
**Category:** 🏗️ Portfolio Build  
**FUTA Course:** SEN204 (Requirements Engineering — data persistence, entity modeling, system integration)  
**Content Output:** 📝 Substack — "Migrating from In-Memory Mocks to EF Core + PostgreSQL in a Production Blazor App"  
**Assignee:** @intisor

---

### 📌 Day 4: Zero-App LAN Data Channel + Catch-Up Protocol

**Epic:** Enable offline file sharing when campus internet is down. Late-joining students receive message history.

**What is broken today:**
- File sharing depends on PeerJS cloud signaling server (`0.peerjs.com`).
- Campus internet down (common after 11pm) → PeerJS fails → file sharing entirely broken.
- Late-joining student misses all prior messages. Must ask lecturer to re-send files.

**What we are building:**

**Part A — Local PeerJS signaling via ASP.NET:**
- `[NEW]` `Hubs/PeerSignalingHub.cs` — SignalR hub that relays WebRTC SDP and ICE candidates between peers on the LAN.
  - Methods: `SendOfferAsync(peerId, sdpOffer)`, `SendAnswerAsync(peerId, sdpAnswer)`, `SendIceCandidateAsync(peerId, candidate)`.
  - Broadcasts to target peer group.
- `[MODIFY]` `wwwroot/js/session.js` — `createPeer()` factory check:
  - If `window.viidiiOfflineMode === true`: Use local `PeerSignalingHub` via SignalR for signaling.
  - Else: Use cloud `0.peerjs.com`.
  - Graceful fallback if cloud fails → auto-switch to local.
- `[MODIFY]` `Components/Shared/ControlsBar.razor` — Add "OFFLINE MODE" toggle button.
  - When clicked: Sets `window.viidiiOfflineMode = true`, recreates all peer connections with local signaling.

**Part B — Catch-up protocol for late-joiners:**
- `[MODIFY]` `Components/Shared/MessagingPanel.razor` — Maintain `_localMessageBuffer` (already implied in `_messages`) serializable to JSON.
- `[MODIFY]` `Hubs/SessionHub.cs` — Add `NotifyLatePeerJoinedAsync(sessionId, latePeerId)` hub method.
  - Lecturer broadcasts this when a new student joins mid-session.
- `[MODIFY]` `wwwroot/js/session.js` — Add catch-up sender/receiver:
  - On `LatePeerJoined` signal: Lecturer's peer opens DataChannel to late peer.
  - Sends: `{ type: 'catchup', messages: [...], fileManifest: [...] }`.
  - Late peer's JS: Receives, invokes `window.viidii.onCatchUpReceived(data)`.
- `[MODIFY]` `Services/SessionJsInterop.cs` — Add `[JSInvokable] OnCatchUpReceivedAsync(JsonElement data)`.
  - Deserializes messages, pushes to `MessagingPanel`.
  - Triggers file download if file manifest provided.
- `[MODIFY]` `Components/Shared/MessagingPanel.razor` — Handle incoming catch-up messages (show them with timestamp like "📩 Catch-up: 5 prior messages received").

**Files to change:**
```
[NEW]    Hubs/PeerSignalingHub.cs
[MODIFY] wwwroot/js/session.js → createPeer() with offline fallback, catch-up protocol
[MODIFY] Services/SessionJsInterop.cs → [JSInvokable] OnCatchUpReceivedAsync
[MODIFY] Components/Shared/ControlsBar.razor → add Offline Mode toggle
[MODIFY] Components/Shared/MessagingPanel.razor → handle catch-up injection
[MODIFY] Hubs/SessionHub.cs → add NotifyLatePeerJoinedAsync
[MODIFY] Program.cs → register PeerSignalingHub
```

**Acceptance Criteria:**
- ✅ File transfer (10MB PDF) completes to 5 students with internet disconnected (router unplugged)
- ✅ Late-joining student receives full chat history within 5 seconds
- ✅ Message says "📩 Files received from peer: document.pdf"
- ✅ Toggle "Offline Mode" on/off without restart
- ✅ Fallback to cloud signaling if local fails gracefully

**Priority:** High  
**Effort:** 4 (Most of day)  
**Category:** 🏗️ Portfolio Build  
**FUTA Course:** CSC307 (Data Communications — P2P protocols, DataChannel framing, LAN topology, ICE candidates)  
**Content Output:** 🐦 Twitter/X — "Shipped offline campus file sharing. No internet. No app. Just WebRTC Data Channels 🔥"  
**Assignee:** @intisor

---

### 📌 Day 5: Dynamic Cryptographic QR Attendance

**Epic:** Prove physical presence in lecture theatre via rotating HMAC QR codes. Block proxy attendance.

**What is broken today:**
- Attendance based only on engagement modal ping responses.
- Student A can share session code with Student B (at home) → both score 100% engagement.
- No way to verify either is physically present.

**What we are building:**

**Core idea:** Lecturer's screen displays a QR code that rotates every 15 seconds. Code is HMAC-SHA256(secret + timestamp). One scan per student per session. Replayed tokens are rejected.

- `[NEW]` NuGet: Add `QRCoder` to `VIIDII.csproj` via package management tool.
- `[NEW]` `Services/AttendanceTokenService.cs` — Singleton service:
  - `GenerateTokenAsync(sessionId)` → creates HMAC-SHA256 token for current 15-second window.
  - `ValidateAndRecordAsync(sessionId, token, studentMatricNo)` → checks token is valid, not replayed, records to DB.
  - Uses `Attendance:SecretKey` from config (generated on first run if not present).
  - Maintains `ConcurrentDictionary<(sessionId, matricNo), DateTime>` of last-seen tokens to prevent replay.
- `[NEW]` `Components/Pages/AttendanceScan.razor` — Razor page `/attend?token=[x]`:
  - Query parameter: `token` (the scanned value).
  - Component receives token, calls `AttendanceTokenService.ValidateAndRecordAsync()`.
  - Shows: "✅ Checked in!" or "❌ Token expired (15 seconds old)" or "❌ Already checked in once this session".
  - Stores result in `AttendanceLogs` table for audit.
- `[MODIFY]` `Components/Pages/LecturerSessionView.razor` — Add QR display panel:
  - Uses `System.Threading.Timer` to refresh token every 15 seconds.
  - Renders QR code SVG using `QRCoder.QRCodeGenerator`.
  - Shows countdown: "QR expires in: 14s".
  - Displays full URL: `https://[campus-ip]/attend?token=[token]`.
- `[MODIFY]` `Program.cs` — Register `AttendanceTokenService`. Load `Attendance:SecretKey` from config.
- `[MODIFY]` `appsettings.json` — Add:
  ```json
  "Attendance": {
    "SecretKey": "[auto-generated on first run]",
    "WindowSeconds": 15,
    "MaxReplaysPerStudent": 1
  }
  ```
- `[MODIFY]` `Components/Pages/SessionRecap.razor` — Add "QR Check-In" column to attendance table:
  - ✅ if student scanned once
  - ❌ if student never scanned
  - Multiple scan count if student tried to game the system

**Files to change:**
```
[NEW]    Services/AttendanceTokenService.cs
[NEW]    Components/Pages/AttendanceScan.razor
[MODIFY] Components/Pages/LecturerSessionView.razor → QR display + timer
[MODIFY] Components/Pages/SessionRecap.razor → add QR check-in column
[MODIFY] Program.cs → register AttendanceTokenService
[MODIFY] appsettings.json → add Attendance section
[MODIFY] appsettings.Production.json → strong SecretKey
[NEW]    Data/Models/AttendanceScanEntity.cs (new table in DB)
[MODIFY] Data/ViidiiDbContext.cs → add AttendanceLogs DbSet
[NEW]    Migrations/20260205_AddAttendanceLogs.cs (auto-generated)
```

**Acceptance Criteria:**
- ✅ QR code displays on lecturer screen without lag
- ✅ QR rotates exactly every 15 seconds
- ✅ Student scans QR → redirected to `/attend?token=...` → shows "✅ Checked in"
- ✅ Student refreshes page + scans old QR → shows "❌ Token expired"
- ✅ Same student scans same valid QR twice → second scan rejected ("❌ Already checked in")
- ✅ Attendance recap shows ✅/❌ for each student
- ✅ Token generation takes <1ms (no latency on page load)

**Priority:** High  
**Effort:** 4 (Most of day)  
**Category:** 🏗️ Portfolio Build  
**FUTA Course:** Cryptography (HMAC, time-window tokens, replay detection)  
**Content Output:** 🐦 Twitter/X — "Built cryptographic QR attendance with HMAC-SHA256 rotating tokens. Proxy attendance = Caught 🚫"  
**Assignee:** @intisor

---

## Phase 1 Summary

| Day | Task | Dependency | Status | Priority |
|-----|------|-----------|--------|----------|
| 1 | WebRTC DFA JS Integration | Independent | ⬜ Pending | Critical |
| 2 | SignalR Optimization + Sapa Mode | Day 1 (soft) | ⬜ Pending | Critical |
| 3 | EF Core + PostgreSQL | Independent | ⬜ Pending | Critical |
| 4 | LAN Data Channel + Catch-Up | Days 1, 3 | ⬜ Pending | High |
| 5 | QR Attendance Tokens | Independent | ⬜ Pending | High |

**Recommended Sprint Order:** Day 1 → 2 → 3 → 4 → 5 (but 3 and 5 can start in parallel with 1).

---

## Phase 2 & Beyond (Roadmap)

| Phase | Goal | Duration | Status |
|-------|------|----------|--------|
| **Phase 2** | Production Hardening | 2 weeks | 📋 Planned |
| **Phase 3** | Feature Expansion (adaptive bitrate, multiroom sessions) | 4 weeks | 📋 Planned |
| **2026 Lock-in** | Systems Architect Foundation (Automata, Linear Algebra, Statistics) | 10 weeks | 📋 Starting after Phase 1 |

---

## 2026 Lock-In Project: Systems Architect Foundation

**Vision:** Build an unshakable CS foundation by connecting theory to systems engineering. Turn every lecture into architecture intuition.

**Duration:** ~10 weeks (starting after Phase 1 complete)  
**Output:** 21 theory deliverables (7 per pillar) + 3 integration projects

### Pillar 1: Computational Physics — Automata & Theory of Computation (CSC 307/309)

| Phase | Deliverable | Output | Status |
|-------|-------------|--------|--------|
| 1.1 | DFA/NFA Fundamentals | Subset construction proofs | ⬜ Not Started |
| 1.2 | NFA → DFA State Explosion | Complexity analysis vs. OS process scheduling | ⬜ Not Started |
| 1.3 | Regular Expressions & Lexical Analysis | Build a basic lexer | ⬜ Not Started |
| 1.4 | Pumping Lemma Proofs | Prove non-regularity via contradiction | ⬜ Not Started |
| 1.5 | Closure Properties | Prove closure; map to microservice composability | ⬜ Not Started |
| 1.6 | DFA Minimization | Implement Hopcroft's algorithm | ⬜ Not Started |
| 1.7 | Integration Project | Build mini compiler front-end (lexer + parser boundary) | ⬜ Not Started |

### Pillar 2: Mathematical Physics — Linear Algebra (MTS 203)

| Phase | Deliverable | Output | Status |
|-------|-------------|--------|--------|
| 2.1 | Eigenvalues & Eigenvectors | Compute & interpret geometrically | ⬜ Not Started |
| 2.2 | Diagonalization & Jordan Normal Form | Handle defective matrices | ⬜ Not Started |
| 2.3 | Spectral Theorem | Prove & apply for symmetric matrices | ⬜ Not Started |
| 2.4 | PageRank & Markov Chains | Implement PageRank algorithm | ⬜ Not Started |
| 2.5 | Control Theory & Stability | Analyze system stability via eigenvalues | ⬜ Not Started |
| 2.6 | PCA & Dimensionality Reduction | Implement PCA from scratch | ⬜ Not Started |
| 2.7 | Integration Project | Build distributed system simulator with spectral analysis | ⬜ Not Started |

### Pillar 3: Empirical Physics — Statistics

| Phase | Deliverable | Output | Status |
|-------|-------------|--------|--------|
| 3.1 | Probability Distributions | Master discrete & continuous distributions | ⬜ Not Started |
| 3.2 | Regression Analysis | Implement linear & logistic regression | ⬜ Not Started |
| 3.3 | Hypothesis Testing | Apply t-tests, chi-square; build confidence intervals | ⬜ Not Started |
| 3.4 | Queuing Theory | Model M/M/1, M/M/c queues | ⬜ Not Started |
| 3.5 | Monte Carlo Simulation | Estimate system metrics under uncertainty | ⬜ Not Started |
| 3.6 | Bootstrapping | Non-parametric confidence intervals | ⬜ Not Started |
| 3.7 | Integration Project | Build load-testing framework with statistical modeling | ⬜ Not Started |

---

## Success Metrics

### Phase 1 Success (End of 5-day sprint)
- ✅ 0 ghost calls in demo session (5+ students, network drops)
- ✅ 100-student mass join <3 seconds
- ✅ Server restart preserves all data
- ✅ File transfer works offline (LAN mode)
- ✅ Attendance recap shows QR verification status

### 2026 Lock-in Success (End of 10 weeks)
- ✅ 21 deliverables authored (theory + code examples)
- ✅ 3 integration projects demonstrate real-world application
- ✅ All content published (YouTube, Substack, Twitter/X, GitHub)
- ✅ Foundation knowledge integrated into VIIDII implementation (Automata → DFA, Linear Algebra → Spectral analysis, Stats → Benchmarking)

---

## Rollout & Go-Live

### Phase 1 (5 days)
- End of Day 5: Feature complete for MVP v1.1
- Day 6 (if needed): Final QA + performance testing
- Day 7: Deploy to staging + demo to stakeholders
- Week 2: Deploy to production (FUTA campus)

### Phase 2 (2 weeks after Phase 1)
- Performance optimization + security hardening
- Multi-room session support
- Adaptive bitrate refinement

### Phase 3 (4 weeks after Phase 2)
- Analytics dashboard
- Student engagement reports (for lecturers)
- Advanced attendance patterns

---

## Open Questions

1. **PostgreSQL Version:** Which version? (14, 15, 16 supported)
2. **FUTA Campus Deployment:** Will VIIDII run on-campus server or cloud (Azure)?
3. **Security:** Should sessions be encrypted end-to-end, or trust the campus network?
4. **Lock-in Timeline:** When does 2026 lock-in start? (After Phase 1? Concurrent?)

---

**Approval:** Ready to create GitHub Issues from this roadmap. Confirm any changes needed?
