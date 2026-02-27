# Product Requirements Document (PRD): VIIDII (Edu Edition)
**Version:** 2.0 | **Date:** February 2026 | **Status:** Active Development

---

## 1. Product Vision — The Afro-Pragmatic Reality

VIIDII (Virtual Interactive Intelligent Demonstration Interface for Instruction) is NOT another Zoom clone. It is an infrastructure-aware, African-campus-first collaboration platform engineered around the systemic constraints of Nigerian university life:

- **Power:** Students bring phones at varying battery levels; a 2-hour CSC309 lecture is a drain test.
- **Network:** The FUTA campus Wi-Fi serves 5,000+ students on shared infrastructure. A single router in LT1 handles 200 connections. 3G/4G is rationed — "data don finish" is a real error state, not a UX edge case.
- **Attendance:** Proxy attendance is a normalized academic crime. Static QR codes printed on whiteboards are useless. The system must be cryptographically adversarial.

The existing codebase (v1.0 MVP) has already proven the core architecture is sound. The `SessionHub`, `SessionService`, engagement tracking pipeline (`ParticipantPingService` → `EngagementModal` → `ConfirmActive` → `CalculateAttendanceScore`), and `SessionRecap` with `chartInterop.js` are real, working infrastructure. Phase 1 is about hardening and scaling what exists — not rebuilding from zero.

---

## 2. Existing Baseline (What Has Already Been Built)

> This section ensures zero duplicated effort. Do not re-architect what already works.

| Component | File(s) | Status | Notes |
|---|---|---|---|
| SignalR Hub | `SessionHub.cs` (510 ln) | ✅ Stable | Uses `ConcurrentDictionary` for connection→MatricNo mapping |
| Session State | `SessionService.cs` (429 ln) | ✅ Stable | Full `CreateSession/JoinSession/EndSession/CalculateAttendanceScore` |
| Engagement Loop | `ParticipantPingService.cs` + `EngagementModal.razor` | ✅ Stable | Random 2–5 min ping interval; 30s countdown modal |
| Session Recap | `SessionRecap.razor` + `chartInterop.js` | ✅ Stable | Timeline, bar chart, breakdown table |
| P2P File Sharing | `session.js` / `sessionInterop.js` | ✅ Stable | Up to 50MB via WebRTC DataChannel |
| User Auth | `MockApiService.cs` + `AuthService.cs` | ⚠️ Critical Debt | Hardcoded in-memory users, no persistence |
| DB Layer | None | ❌ Missing | All state dies on server restart |
| WebRTC DFA | `session.js` / `sessionInterop.js` | ⚠️ Fragile | No formal state machine; ghost-call race conditions exist |
| QR Attendance | None | ❌ Missing | No cryptographic check-in mechanism |
| Adaptive Quality | `session.js` / `VideoStage.razor` | ❌ Missing | Fixed video quality regardless of network conditions |

---

## 3. Core Feature Specifications (Phase 1 MVP)

### 3.1 — WebRTC Deterministic Finite Automaton (DFA)
**Problem:** The current `session.js` & `sessionInterop.js` manage WebRTC state via ad-hoc `if` checks and `Task.Delay(500)` hacks (DOCUMENTATION.md, Fix #3). When a student's network drops on FUTA Wi-Fi, the existing peer reconnects, but the call object is a ghost — the lecturer's `activeCalls` Map (currently defined but improperly cleaned) doesn't know the peer is dead.

**Requirement:** Model the entire WebRTC lifecycle in C# as a strict state machine enforced at both the `SessionHub` level and the JS interop level.

```
States:     Idle → Signaling → Connecting → Connected → Degraded → Disconnected
Transitions:
  Idle       --[onSessionStarted]--> Signaling
  Signaling  --[onReceivePeerId]-->  Connecting
  Connecting --[onStreamReceived]--> Connected
  Connected  --[onNetworkDrop]-->    Degraded
  Degraded   --[onIceRestart]-->     Connecting
  Degraded   --[onTimeout(30s)]-->   Disconnected
  Disconnected --[onManualRejoin]--> Signaling
```

No state transition may be triggered unless its precondition is satisfied. Invalid transitions are logged and silently discarded.

**Acceptance Criteria:**
- Zero "ghost call" states observable in a 5-student concurrent test
- `activeCalls` Map in `session.js` is always consistent with the C# DFA state
- State transitions are broadcast to the `SessionHub` for audit logging

---

### 3.2 — Adaptive Mesh Quality Degradation
**Problem:** `VideoPlayer.razor` and `VideoStage.razor` render at whatever quality the browser negotiates. On FUTA's congested Wi-Fi, this causes buffering, dropped frames, and students muting themselves. There is no feedback loop between network quality and encoding parameters.

**Requirement:** Implement a 4-tier adaptive quality ladder triggered by RTCPeerConnection stats polling (every 5 seconds):

| Tier | Trigger | Action | CPU Impact |
|---|---|---|---|
| `HD` | RTT < 100ms, loss < 1% | 720p@30fps | Baseline |
| `SD` | RTT 100–300ms, loss 1–5% | 480p@15fps | -40% |
| `AudioOnly` | RTT > 300ms, loss > 5% | Kill video tracks | -80% |
| `SapaMode` | Battery < 20% OR user-flagged `DataFinished` | Halt all incoming video, text-only | -95% |

The existing `IssueButtons.razor` `BatteryLow` and `DataFinished` status flags feed directly into `Sapa Mode` activation. `FlagIssue` → `SessionHub.FlagIssue()` → (new) `NotifyAdaptiveDowngrade` SignalR event.

**Acceptance Criteria:**
- Quality ladder transitions are seamless (no visible black flash)
- "Sapa Mode" banner displays on `VideoStage.razor` with CPU utilization reading
- Lecturer sees per-student stream quality in `ParticipantPanel.razor`

---

### 3.3 — "Zero-App" LAN P2P Data Channel
**Problem:** The existing P2P file sharing (session.js) works over the internet via PeerJS cloud-brokered connections. In a FUTA LT with no internet, PeerJS's signaling server at `0.peerjs.com` is unreachable, and the entire system fails before a single SDP offer is exchanged.

**Requirement:** Implement a dual-path file distribution system:

**Path A (Internet Available):** Existing PeerJS DataChannel flow — lecturer sends file → binary chunks → all connected peers. ✅ Already works.

**Path B (LAN-Only / "Zero-App" Mode):**
1. Lecturer activates "Offline LAN Mode" in `ControlsBar.razor`
2. `session.js` switches PeerJS to use a **local TURN/signaling server** on the lecturer's machine (served by the ASP.NET host itself via a lightweight WebRTC signaling endpoint added to `SessionHub`)
3. File broadcast uses existing DataChannel chunking (32KB chunks), but peers discover each other via mDNS/local IP broadcast instead of the cloud PeerJS server
4. Students receive the file with a progress bar in `MessagingPanel.razor`

**The Catch-Up Protocol:** A student who joins 20 minutes late (after the PDF was already distributed) gets the file from any connected peer, not the server:
1. New student joins → signals readiness via `DataChannel` control message
2. First available peer (lowest latency peer ID) accepts the catch-up request
3. Peer sends file in chunks via its existing `activeCalls` DataChannel
4. `MessagingPanel.razor` shows "Received from [peer]" attribution

**Acceptance Criteria:**
- File transfer works with zero internet in a local network test
- 50MB PDF distributes to 10 students in under 90 seconds on LAN
- Late-joining student receives file from a peer, not the server

---

### 3.4 — Cryptographic QR Attendance System
**Problem:** The existing `ParticipantPingService` tracks engagement (ping→response) but relies entirely on SignalR connection presence. A student can join, set their laptop down, and a housemate physically in the LT can respond to pings using their phone. The existing `IssueButtons`/`EngagementModal` system has no cryptographic anchor.

**Requirement:** Implement time-rotating cryptographic QR codes displayed on `LecturerSessionView`:

**QR Generation (Server-Side):**
- Token payload: `HMACSHA256(sessionId + Math.Floor(unixTime / 15), secretKey)`
- Rotates every 15 seconds (aligned to Unix epoch multiples of 15)
- Server holds the `secretKey` in `appsettings.json` (Phase 1) / AWS Secrets Manager (Phase 2)
- C# library: `QRCoder` (NuGet)

**Student Check-In (Client-Side):**
- Student scans QR with phone camera (standard camera app, no app install)
- QR resolves to `https://[host]/attend?token=[hmac]`
- Server validates HMAC within a ±15s window (two valid tokens at any time for clock skew)
- Server records `AttendanceLog` entry with: `sessionId, matricNo, scanTimestamp, ipAddress, tokenHash`

**Anti-Spoofing:**
- One-time use: token+matricNo combination is rejected after first valid scan
- Rate limiting: max 3 scans per minute per IP to prevent automated scanners
- The existing `ParticipantPingService` ping score is **combined** with QR check-in presence for final attendance percentage

**Acceptance Criteria:**
- QR refreshes visually every 15 seconds on `LecturerSessionView`
- Replayed (expired) tokens return HTTP 401 with "Token expired" JSON
- Duplicate scans from same MatricNo return HTTP 409 "Already checked in"

---

### 3.5 — EF Core + PostgreSQL Database (Replace MockApiService)
**Problem:** `MockApiService.cs` is a static in-memory list. Every server restart wipes all user data, sessions, and attendance history. The `SessionService.CalculateAttendanceScore()` engine is sophisticated but calculates scores from ephemeral RAM state. **The data evaporates.**

**Requirement:** Migrate all persistent data to PostgreSQL via EF Core Code-First.

**Schema (See Feasibility Study §4 for full DDL):**
- `Users` — replaces `MockApiService._users`
- `Sessions` — replaces `SessionService._sessions`
- `ParticipantEvents` — replaces `Session.ParticipantEvents` in-memory Dict
- `AttendanceLogs` — new (cryptographic QR check-in records)
- `Messages` — replaces `MessageService` in-memory store

**Migration path:**
1. Implement `IUserRepository`, `ISessionRepository` interfaces
2. Create EF Core `ViidiiDbContext`
3. Inject repositories into `SessionHub` and `SessionService`
4. `MockApiService` becomes a **seeder** (runs only in Development) not a runtime dependency

**Acceptance Criteria:**
- App survives server restart with all session/user data intact
- `SessionRecap.razor` loads historical recap from DB, not live RAM state
- Zero references to `MockApiService.GetUsers()` in production code paths

---

## 4. Non-Functional Requirements (The FUTA Stress Test)

| Requirement | Target | How to Verify |
|---|---|---|
| **Concurrency** | 100 simultaneous SignalR joins without thread-pool starvation | Load test with `k6` (50 virtual users, 2 sessions) |
| **Memory** | No memory leak after 50 students join/leave 3 times | Profile with dotnet-trace over 30min test |
| **Latency** | `ConfirmActive` round-trip < 500ms on 3G simulation | Chrome DevTools throttling + SignalR timing logs |
| **Offline** | LAN file transfer works with no internet | Disconnect router during active test session |
| **Battery** | "Sapa Mode" reduces CPU usage by ≥ 80% vs baseline | Chrome Task Manager sampling |
| **Security** | HMAC QR token cannot be reused | Replay attack test: scan same QR twice |

---

## 5. User Roles & Permissions

| Action | Student | Lecturer | Admin |
|---|---|---|---|
| Create session | ❌ | ✅ | ✅ |
| Join session (with dept/level check) | ✅ | ✅ (as host) | ✅ |
| Flag battery/data issue | ✅ | ❌ | ❌ |
| Trigger "Are You There?" prompt | ❌ | ✅ | ❌ |
| View `SessionRecap` with full breakdown | ❌ (own score only) | ✅ | ✅ |
| Scan QR attendance | ✅ | ❌ | ❌ |
| Display QR code | ❌ | ✅ | ❌ |
| Admin dashboard | ❌ | ❌ | ✅ |

---

## 6. Out of Scope (Phase 1)

- Session recording / playback
- Breakout rooms
- Whiteboard/annotation
- Mobile native app (MAUI)
- Multi-TURN server federation
- AI-based lecture summarization

---

## 7. System Architecture — Full Component Map

### 7.1 Layered Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        BROWSER CLIENTS                               │
│                                                                      │
│  ┌──────────────────────┐        ┌──────────────────────┐           │
│  │   LECTURER BROWSER   │        │   STUDENT BROWSER(s) │           │
│  │                      │        │                      │           │
│  │ LecturerSessionView  │        │ StudentSessionView   │           │
│  │ VideoStage.razor     │        │ VideoStage.razor     │           │
│  │ ControlsBar.razor    │        │ EngagementModal.razor│           │
│  │ ParticipantPanel     │        │ IssueButtons.razor   │           │
│  │ MessagingPanel       │        │ MessagingPanel       │           │
│  │                      │        │                      │           │
│  │  ┌────────────────┐  │        │  ┌────────────────┐  │           │
│  │  │ sessionInterop │  │        │  │ sessionInterop │  │           │
│  │  │    .js         │◄─┼──P2P──►│  │    .js         │  │           │
│  │  │ (PeerJS/WebRTC)│  │        │  │ (PeerJS/WebRTC)│  │           │
│  │  └────────────────┘  │        │  └────────────────┘  │           │
│  └──────────┬───────────┘        └───────────┬──────────┘           │
│             │ SignalR (WebSocket)             │ SignalR               │
└─────────────┼─────────────────────────────────┼─────────────────────┘
              │                                 │
┌─────────────▼─────────────────────────────────▼─────────────────────┐
│                      ASP.NET CORE SERVER                             │
│                                                                      │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    SessionHub.cs (510 ln)                     │  │
│  │  StartSession │ JoinSession │ SendPeerId │ PromptEngagement  │  │
│  │  ConfirmActive │ FlagIssue │ UpdateTabStatus │ EndSession    │  │
│  │  ConcurrentDictionary<connectionId, matricNo>                │  │
│  └──────────────────────────┬────────────────────────────────────┘  │
│                             │ Injected Services                      │
│  ┌──────────────────┐  ┌────▼──────────────┐  ┌──────────────────┐ │
│  │  SessionService  │  │   MessageService  │  │   AuthService    │ │
│  │  (429 lines)     │  │                   │  │                  │ │
│  │  ConcurrentDict  │  │  In-memory store  │  │  MockApiService  │ │
│  │  _sessions       │  │  (→ DB Phase 1)   │  │  (→ DB Phase 1)  │ │
│  └──────────────────┘  └───────────────────┘  └──────────────────┘ │
│                                                                      │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │            ParticipantPingService (BackgroundService)         │  │
│  │  Runs every 2–5 min (randomized) │ Pings all active students  │  │
│  │  Checks _lastSeen timeout (35s)  │ Marks InActive if expired  │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │              AttendanceTokenService  [Phase 1 NEW]           │   │
│  │  HMAC-SHA256 token generation  │  15-second window validation │   │
│  │  One-time-use enforcement via AttendanceLogs table            │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                  ViidiiDbContext  [Phase 1 NEW]              │   │
│  │  Users │ Sessions │ ParticipantEvents │ AttendanceLogs │ Messages│
│  └─────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
              │
              ▼
        PostgreSQL 16
```

### 7.2 Real-Time Data Flows

**Flow A — Session Start (Lecturer Initiates)**
```
Lecturer clicks "Start Session"
  → SessionView.razor calls SessionJsInterop.StartSessionAsync()
  → JS: getUserMedia() acquires camera/mic → localStream stored
  → SignalR: hub.SendAsync("StartSession", sessionId, matricNo)
  → SessionHub.StartSession() sets session.LecturerConnectionId
  → Hub broadcasts "StartSession" to all group members
  → Students receive → OnSessionStarted() fires
  → Students: OnAfterRenderAsync() → SetupStudentPeerConnectionAsync()
  → JS: new Peer(config) → peer.on("open") → send PeerId via SignalR
  → Hub: SendPeerId() → OthersInGroup → "ReceivePeerId" to lecturer
  → Lecturer: OnReceivePeerId() → Task.Delay(500) [← DFA Task 1 fixes this]
  → JS: callStudent(peerId) → peer.call(peerId, localStream)
  → Student: peer.on("call") → call.answer() → stream flows P2P
```

**Flow B — Engagement Tracking (ParticipantPingService Loop)**
```
ParticipantPingService (every 2–5 min, random)
  → IHubContext.Clients.Client(connectionId).SendAsync("AreYouThere")
  → EngagementModal.razor: Show() → 30-second countdown timer starts
  → Student clicks "I'm Here!" → HubConnection.SendAsync("ConfirmActive")
  → SessionHub.ConfirmActive() → _lastSeen[matricNo] = UtcNow
  → SessionService.UpdateParticipantStatus(Active) → logs event with timestamp
  → Hub sends updated scores → lecturer's ParticipantPanel re-renders

  [If student ignores modal for 30s]
  → EngagementModal.AutoDismiss() → no ConfirmActive sent
  → Next PingService iteration: checks _lastSeen + 35s timeout
  → Marks student InActive → sends ReceiveParticipantStatuses to lecturer
```

**Flow C — Session Recap Build**
```
Lecturer clicks "End Session"
  → SessionHub.EndSession() → session.Status = Ended, session.EndTime = UtcNow
  → Hub broadcasts "SessionEnded" → all clients navigate away
  → SessionRecap.razor loads → SessionService.CalculateAttendanceScore(sessionId)
  → Event-sourced scoring: iterates ParticipantEvents[] timeline chronologically
  → Grace logic: BatteryLow/DataFinished → subsequent Disconnected gets 50% credit
  → chartInterop.js: Chart.js bar chart (green ≥80%, yellow ≥70%, red <70%)
  → TimelineItem.razor renders each event with timestamp + Milestone badge
```

---

## 8. Adaptive Mesh Degradation — Deep Specification

### 8.1 The Problem in Detail

The FUTA LT1 Wi-Fi scenario: 80 students, one 2.4GHz router, no QoS. WebRTC's default behavior is to maintain video at whatever bitrate it negotiated at connection time, then buffer aggressively as bandwidth drops. The result is a 10-second frozen frame followed by a "Connection unstable" PeerJS error and a full reconnect — interrupting the lecture for everyone.

There is no mechanism today in `VideoPlayer.razor` or `session.js` to adapt rendering resources to available bandwidth.

### 8.2 The 4-Tier Quality Ladder

```
NETWORK STATE          TRIGGER                   ACTION                         COMPONENT
─────────────────────────────────────────────────────────────────────────────────────────
HD (Baseline)    RTT < 100ms, loss < 1%     720p@30fps, stereo audio      VideoStage: normal render
SD               RTT 100-300ms, loss 1-5%   480p@15fps, mono audio         VideoStage: normal render
AudioOnly        RTT > 300ms, loss > 5%     Kill video tracks, audio only  VideoStage: hide <video>, show 📡
SapaMode         BatteryLow flag OR          Kill ALL incoming media,        VideoStage: dark screen,
                 DataFinished flag OR        render text + slides only,      battery icon + "Saving power"
                 RTT > 500ms                 halt network polling            banner; halt getStats()
```

### 8.3 RTCPeerConnection Stats Polling (Implementation)

The stats controller runs in `session.js` on a 5-second interval per active peer connection:

```javascript
// Runs once per peer connection in the lecturer's browser
// (Lecturer has N connections, one per student)
async function pollPeerQuality(pc, peerId, dotNetRef) {
    const stats = await pc.getStats();
    let rtt = 999, packetLoss = 100, jitter = 0;

    stats.forEach(report => {
        // 'remote-inbound-rtp' gives us the stats from the RECEIVER's perspective
        if (report.type === 'remote-inbound-rtp' && report.kind === 'video') {
            rtt = (report.roundTripTime ?? 0) * 1000; // convert s → ms
            const lost = report.packetsLost ?? 0;
            const received = report.packetsReceived ?? 1;
            packetLoss = (lost / (lost + received)) * 100;
            jitter = (report.jitter ?? 0) * 1000;
        }
    });

    // Determine tier
    let tier;
    if (rtt < 100 && packetLoss < 1)       tier = 'HD';
    else if (rtt < 300 && packetLoss < 5)  tier = 'SD';
    else if (rtt < 500)                    tier = 'AudioOnly';
    else                                   tier = 'SapaMode';

    // Apply tier settings to the RTCRtpSender
    const videoSender = pc.getSenders().find(s => s.track?.kind === 'video');
    if (videoSender) {
        const params = videoSender.getParameters();
        if (params.encodings?.length) {
            const enc = params.encodings[0];
            switch (tier) {
                case 'HD':        enc.maxBitrate = 1_500_000; enc.active = true;  break;
                case 'SD':        enc.maxBitrate = 500_000;   enc.active = true;  break;
                case 'AudioOnly': enc.active = false; break; // kill video track
                case 'SapaMode':  enc.active = false;
                    // Notify C# to trigger Sapa Mode UI on this student's device
                    await dotNetRef.invokeMethodAsync('OnSapaModeActivated', peerId);
                    break;
            }
            await videoSender.setParameters(params);
        }
    }

    // Report quality back to ParticipantPanel for the per-student badge
    await dotNetRef.invokeMethodAsync('OnPeerQualityUpdated', peerId, tier, rtt, packetLoss);
}
```

**On the student's end** (`session.js` in student browser):
When `OnSapaModeActivated` fires, the student's `VideoStage.razor` receives a SignalR event `ActivateSapaMode` and calls `SessionJsInterop.ActivateSapaModeAsync()`:

```javascript
function activateSapaMode() {
    // Stop ALL incoming video tracks to save CPU and data
    const videoEl = document.getElementById('sessionVideo');
    if (videoEl?.srcObject) {
        videoEl.srcObject.getVideoTracks().forEach(t => t.stop());
    }
    // Halt the quality polling loop entirely
    if (qualityPollInterval) { clearInterval(qualityPollInterval); }
    // C# callback → VideoStage.razor shows "Sapa Mode" banner
    dotNetRef.invokeMethodAsync('OnSapaModeUIActivated');
}
```

### 8.4 Battery & Thermal Throttling — The "Sapa Mode" System

**Why "Sapa"?** In Nigerian campus slang, "sapa" means being broke — it perfectly captures the state of a student whose phone is on 5% battery trying to survive a 2-hour lecture.

**Trigger sources (multiple paths lead to Sapa Mode):**

| Source | Signal | Path |
|---|---|---|
| Student flags battery manually | `IssueButtons.razor` → "🔋 Low Battery" | `FlagIssue("BatteryLow")` → `SessionHub` → `ActivateSapaMode` SignalR event |
| Student flags data manually | `IssueButtons.razor` → "📶 Data Low" | `FlagIssue("DataFinished")` → `SessionHub` → `ActivateSapaMode` |
| Network quality collapse | RTCPeerConnection RTT > 500ms sustained for 15s | `session.js` polling loop → `dotNetRef.OnSapaModeActivated()` |
| Battery API (future) | `navigator.getBattery().level < 0.2` | JS Battery API → auto-trigger without button press |

**What Sapa Mode actually stops:**

```
STOPPED in Sapa Mode:           STILL RUNNING in Sapa Mode:
─────────────────────────────   ──────────────────────────────────
• Incoming video rendering      • SignalR connection (EngagementModal still works)
• Outgoing video encoding       • Text chat (MessagingPanel.razor)
• RTCPeerConnection stat polls  • File receipt via DataChannel (slides still arrive)
• Animation/transitions CSS     • "Are You There?" modal (attendance still tracked)
• VideoStage DOM element        • IssueButtons (can escalate to worse status)
```

**`VideoStage.razor` changes for Sapa Mode UI:**
```razor
@if (_isSapaMode)
{
    <div class="sapa-mode-overlay">
        <div class="sapa-icon">🔋</div>
        <h3>Sapa Mode Active</h3>
        <p>Video paused to save battery. You're still connected.</p>
        <p class="sapa-sub">Chat and attendance tracking continues.</p>
        <button @onclick="DeactivateSapaMode">Resume Video</button>
    </div>
}
else
{
    <video id="sessionVideo" autoplay playsinline muted="@State.IsLecturer" />
}
```

---

## 9. The "Catch-Up" Protocol — Distributed State for Late Joiners

### 9.1 The Problem

A student who joins the FUTA hotspot 20 minutes late misses:
1. The PDF handout broadcast over DataChannel
2. The first 20 minutes of chat history from `MessagingPanel`
3. Any files uploaded via P2P file sharing

The current code has no mechanism for this. There is no server-side chat history that a late joiner can request (messages are in-memory `MessageService`). There is no file registry.

### 9.2 The Protocol State Machine

```
[LATE STUDENT joins session]
         │
         ▼
[SignalR: Hub broadcasts "LatePeerJoined" to session group]
         │
         ▼
[All active peers receive event]
         │
         ▼
[First peer to respond (race condition winner) becomes "CatchUp Provider"]
         │
    ┌────┴────────────────────────────────────────────┐
    │           DataChannel Control Protocol           │
    │                                                  │
    │   Provider → Late Peer:                          │
    │   { type: 'catchup_offer',                       │
    │     messageCount: 47,                            │
    │     fileManifest: ['lecture1.pdf', 'notes.pptx'] │
    │   }                                              │
    │                                                  │
    │   Late Peer → Provider:                          │
    │   { type: 'catchup_accept',                      │
    │     wantFiles: ['lecture1.pdf']                  │
    │   }                                              │
    │                                                  │
    │   Provider → Late Peer (stream):                 │
    │   { type: 'catchup_messages', data: [...] }      │
    │   { type: 'catchup_file_chunk', name, chunk, n } │
    │   { type: 'catchup_complete' }                   │
    └──────────────────────────────────────────────────┘
         │
         ▼
[MessagingPanel.razor receives injected messages → renders full history]
[File appears in chat with "📥 From peer: [name]" label]
```

### 9.3 Why Peer-to-Peer, Not Server?

The server (`MessageService`) holds messages in-memory. In the current architecture, calling `GetMessages(sessionId)` from `SessionHub` works fine for the same server process. But:

1. When EF Core is added (Task 3), the DB becomes the canonical source and `GetMessages` becomes a DB call — fine.
2. **In LAN Offline Mode** (Task 4), the server has no connectivity to push messages; the DataChannel catch-up is the only path.
3. Large file re-delivery (a 50MB PDF) is far cheaper on the LAN (direct peer) than bouncing through the server.

The DataChannel catch-up protocol future-proofs LAN mode from day one.

### 9.4 `MessagingPanel.razor` Integration

```csharp
// New [JSInvokable] method in SessionViewBase.cs or SessionJsInterop.cs
[JSInvokable]
public async Task OnCatchUpDataReceived(JsonElement data)
{
    await InvokeAsync(() =>
    {
        var type = data.GetProperty("type").GetString();
        switch (type)
        {
            case "catchup_messages":
                // Prepend historical messages to _messages list in MessagingPanel
                var historical = data.GetProperty("data")
                    .Deserialize<List<Post>>(JsonOptions) ?? [];
                _messages.InsertRange(0, historical);
                break;
            case "catchup_file_chunk":
                // Forward to existing file reassembly logic in session.js
                break;
            case "catchup_complete":
                _messages.Add(new Post
                {
                    content = "✅ Synced with peer — you're caught up.",
                    UserName = "VIIDII",
                    createdAt = DateTime.UtcNow
                });
                StateHasChanged();
                break;
        }
    });
}
```

---

## 10. Database Schema — Detailed (EF Core Code-First)

### 10.1 Entity Relationship Diagram

```
Users ──────────────────────────────────────────────────┐
  │                                                      │
  │ (LecturerId)                                         │ (MatricNo)
  ▼                                                      ▼
Sessions ──────────────── ParticipantEvents ──────── AttendanceLogs
  │                              │
  │ (SessionId)                  │ (SessionId)
  ▼                              ▼
Messages ─── MessageReactions
```

### 10.2 Full Entity Definitions (C# EF Core)

```csharp
// Data/Entities/UserEntity.cs
public class UserEntity
{
    [Key] public string MatricNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Role Role { get; set; }
    public User.Departments? Department { get; set; }
    public User.Levels? Level { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<SessionEntity> HostedSessions { get; set; } = [];
    public ICollection<ParticipantEventEntity> Events { get; set; } = [];
    public ICollection<AttendanceLogEntity> AttendanceLogs { get; set; } = [];
}

// Data/Entities/SessionEntity.cs
public class SessionEntity
{
    [Key] public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string LecturerId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public SessionStatus Status { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int[] AllowedDepartments { get; set; } = [];
    public int[] AllowedLevels { get; set; } = [];

    // Navigation
    public UserEntity Lecturer { get; set; } = null!;
    public ICollection<ParticipantEventEntity> ParticipantEvents { get; set; } = [];
    public ICollection<AttendanceLogEntity> AttendanceLogs { get; set; } = [];
    public ICollection<MessageEntity> Messages { get; set; } = [];
}

// Data/Entities/ParticipantEventEntity.cs
// This is the core of the event-sourced attendance engine
public class ParticipantEventEntity
{
    [Key] public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string MatricNo { get; set; } = string.Empty;
    public Session.StudentStatus Status { get; set; } // Active, InActive, BatteryLow, DataFinished, Disconnected
    public DateTime OccurredAt { get; set; }

    // Navigation
    public SessionEntity Session { get; set; } = null!;
    public UserEntity Participant { get; set; } = null!;
}

// Data/Entities/AttendanceLogEntity.cs (NEW — QR check-in)
public class AttendanceLogEntity
{
    [Key] public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string MatricNo { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;  // SHA256 of the HMAC token
    public DateTime ScannedAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public bool IsValid { get; set; }  // false = replay attack logged but rejected
}

// Data/Entities/MessageEntity.cs
public class MessageEntity
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ParentId { get; set; }  // null = post; non-null = comment
    public bool IsFile { get; set; }
    public bool IsLecturerPost { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public SessionEntity Session { get; set; } = null!;
    public ICollection<ReactionEntity> Reactions { get; set; } = [];
}
```

### 10.3 Critical EF Core Indexes (Performance)

```csharp
// In ViidiiDbContext.OnModelCreating()
modelBuilder.Entity<ParticipantEventEntity>()
    .HasIndex(e => new { e.SessionId, e.MatricNo, e.OccurredAt })
    .HasDatabaseName("IX_ParticipantEvents_Session_Participant_Time");
// This index is critical: CalculateAttendanceScore() scans all events for a session
// ordered by timestamp. Without this index, a 100-student 2-hour session
// with events every 30s = 400 rows scanned — acceptable.
// At 1000-student institution scale (future), this index is mandatory.

modelBuilder.Entity<AttendanceLogEntity>()
    .HasIndex(e => new { e.SessionId, e.MatricNo }).IsUnique()
    .HasDatabaseName("IX_AttendanceLogs_Session_Student_Unique");
// Enforces one QR check-in per student per session at the DB level,
// independent of application-level checks (defense in depth).

modelBuilder.Entity<AttendanceLogEntity>()
    .HasIndex(e => new { e.TokenHash, e.MatricNo }).IsUnique()
    .HasDatabaseName("IX_AttendanceLogs_Token_Student_Unique");
// Prevents same token being used by two different students (shared token attack).
```

---

## 11. FUTA Course Theory Mappings

Every Phase 1 feature has a direct theoretical grounding in the FUTA Computer Science / Software Engineering curriculum. This makes VIIDII a living portfolio project that demonstrates applied understanding of core courses.

| Phase 1 Task | Feature | FUTA Course | Theoretical Concept Applied |
|---|---|---|---|
| Task 1 | WebRTC DFA | **CSC309** — Theory of Computation | Deterministic Finite Automaton: 6 states, defined alphabet (WebRTC events), transition function δ(state, event) → state. The ghost-call bug is provably a non-determinism violation. |
| Task 2 (Part A) | SignalR join storm fix | **CSC305** — System Programming | Thread pool architecture, kernel-level thread scheduling, the C10K problem. `Task.Delay(100) × 100` is a textbook starvation scenario. `Channel<T>` applies CSP (Communicating Sequential Processes) theory. |
| Task 2 (Part B) | Sapa Mode / Adaptive Quality | **CSC305** — System Programming | Process scheduling priorities, CPU throttling, real-time system resource management. WebRTC's `maxBitrate` maps directly to bandwidth allocation concepts in OS scheduling. |
| Task 3 | EF Core / PostgreSQL | **SEN204** — Requirements Engineering & **SEN301** — Database Systems | Entity-Relationship modeling, normalization (1NF→3NF for the ParticipantEvents table), ACID transactions for attendance data integrity, EF Core Code-First = schema-as-code. |
| Task 4 | LAN Data Channel / Catch-Up | **CSC307** — Data Communications | P2P network topology vs. client-server, DataChannel framing (chunked binary transfer = application-layer segmentation), ICE/STUN/TURN as NAT traversal mechanisms (direct campus LAN maps to RFC 8445). |
| Task 5 | HMAC QR Attendance | **SEN307** — Software Security | HMAC (Hash-based Message Authentication Code): keyed one-way function, time-based token = TOTP variant (RFC 6238 analogue), replay attack prevention via nonce consumption, rate limiting = anti-DoS. |

### 11.1 CSC309 Deep Link — The DFA Proof

The WebRTC connection lifecycle can be formally described as:

```
M = (Q, Σ, δ, q₀, F)  where:

Q = { Idle, Signaling, Connecting, Connected, Degraded, Disconnected }
Σ = { sessionStarted, peerIdReceived, streamReceived, networkDrop,
      iceRestart, timeout30s, manualRejoin }
q₀ = Idle
F = { Connected }  (accepting state = healthy call)

δ (transition table):
┌──────────────┬─────────────────┬──────────────┐
│ Current State│ Input Event     │ Next State   │
├──────────────┼─────────────────┼──────────────┤
│ Idle         │ sessionStarted  │ Signaling    │
│ Signaling    │ peerIdReceived  │ Connecting   │
│ Connecting   │ streamReceived  │ Connected    │
│ Connected    │ networkDrop     │ Degraded     │
│ Degraded     │ iceRestart      │ Connecting   │
│ Degraded     │ timeout30s      │ Disconnected │
│ Disconnected │ manualRejoin    │ Signaling    │
│ *            │ *               │ (same)       │← All undefined transitions
└──────────────┴─────────────────┴──────────────┘   are self-loops (rejected silently)
```

The ghost-call bug occurs when the system processes `peerIdReceived` from a peer that is already in `Disconnected` state — which the DFA explicitly rejects via the undefined transition rule (δ(Disconnected, peerIdReceived) = Disconnected).

### 11.2 CSC307 Deep Link — DataChannel as Application-Layer Segmentation

WebRTC DataChannel over SCTP implements reliable ordered delivery at the transport layer. The file chunking in `session.js` (32KB chunks) is a direct implementation of application-layer segmentation:

```
┌─────────────────────────────────────────────────────────────────┐
│ Application Layer  │ VIIDII file chunking (32KB segments)       │
│ Transport Layer    │ SCTP (Stream Control Transmission Protocol) │
│ Session Layer      │ DTLS (Datagram TLS encryption)             │
│ Network Layer      │ UDP (unreliable datagrams, ICE-selected)   │
│ Data Link Layer    │ Wi-Fi 802.11ac / 4G LTE                    │
└─────────────────────────────────────────────────────────────────┘
```

This is the OSI model applied directly to a feature you built. CSC307 exam question: "Why does WebRTC use SCTP instead of TCP for DataChannels?" Answer: TCP's head-of-line blocking is catastrophic for real-time data alongside media streams; SCTP supports multiple independent streams per connection.

