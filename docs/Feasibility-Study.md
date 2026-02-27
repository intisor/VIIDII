# Feasibility & Architecture Study: VIIDII (Edu Edition)
**Version:** 2.0 | **Date:** February 2026 | **Author:** Antigravity (Principal Systems Architect)

---

## 1. Existing Architecture — Honest Assessment

Before proposing any new architecture, we must be precise about what already exists and where the actual risk is.

### What the Code Already Does Well

**`SessionHub.cs` (510 lines):**
The hub already uses `ConcurrentDictionary<string, DateTime> _lastSeen` and `ConcurrentDictionary<string, string> _connectionMatricNos` — the most critical concurrency primitives are already in place. The `ISessionLecturer()` check is synchronized. The `OnDisconnectedAsync` cleanup is guarded and removes entries from the static dictionaries cleanly.

**`SessionService.cs` (429 lines):**
The internal `_sessions` store is a `ConcurrentDictionary<string, Session>`. `CalculateAttendanceScore()` implements a full event-sourced timeline processing loop with grace logic for `BatteryLow`/`DataFinished → Disconnected` status transitions. This scoring engine is production-quality and should not be rewritten.

**`ParticipantPingService.cs` (65 lines):**
A `BackgroundService` that runs on a randomized 2–5 minute interval. Uses `IHubContext<SessionHub>` (not the hub itself) for thread-safe SignalR calls from the background thread. Correctly reads `Session.StudentStatus` before marking `InActive`. The `TryGetLastSeen` static method bridges the gap between the background service and hub state.

**`EngagementModal.razor` (179 lines):**
Implements a 30-second countdown using `System.Threading.Timer` with `InvokeAsync(StateHasChanged)` — the correct threading pattern for Blazor. Guards against showing on lecturer via `IsStudent` parameter check.

**`SessionRecap.razor` (348 lines):**
Full end-of-session analytics: timeline events, average attendance score, `ParticipantsAbove70Percent` stat, most common penalty reason. `chartInterop.js` renders a Chart.js bar chart with green/yellow/red coloring based on score thresholds.

---

## 2. Threat Model — The FUTA Concurrency Problem

### 2.1 Thread-Pool Starvation Risk (CSC305 — System Programming)

**The Problem (Actual Code Reference):**
In `SessionHub.cs::StartSession()` (line 101), there is a `GetMatricNoForConnectionAsync()` call containing a `Task.Delay(100)` retry if the MatricNo is not immediately available. In a 100-student mass join scenario, 100 SignalR connections call `StartSession` within the same 500ms window. Each fire a `Task.Delay(100)`, consuming 100 thread-pool slots simultaneously.

ASP.NET Core's thread pool starts with `Environment.ProcessorCount * 2` threads and grows slowly (1 thread/500ms). With 100 blocked tasks, the pool starvation cascade looks like:

```
t=0ms:   100 connections call StartSession()
t=0ms:   100 Task.Delay(100) scheduled
t=100ms: 100 continuations queue for thread-pool
t=100ms: Thread pool has ~16 threads (8-core machine) — 84 tasks wait
t=600ms: Pool grows to ~17 threads (1 new thread/500ms cap)
t=42s:   All 100 tasks finally resolve (queued 84/16 at 500ms per thread growth)
```

**Result:** New SignalR circuit requests time out. Students see "Connecting..." for 42 seconds.

**Solution (Phase 1):** Replace `Task.Delay(100)` retry with a zero-allocation path — cache the MatricNo at connection time via a hub filter or connection ID header, eliminating the need for the async retry entirely.

**Solution (Phase 2 / Scale):** Use `Channel<T>` (bounded, `FullMode.Wait`) to queue join events and process them on a dedicated worker, rate-limiting the `SessionService.JoinSession` call to prevent dictionary mutation bursts:

```csharp
private static readonly Channel<(string SessionId, string MatricNo, string ConnectionId)> 
    _joinChannel = Channel.CreateBounded<(string, string, string)>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait });
```

---

### 2.2 WebRTC Ghost-Call Race Condition (CSC309 — Finite Automata)

**The Problem (Actual Code Reference):**
`DOCUMENTATION.md` Fix #3 reveals the root cause: `StateHasChanged()` is async (queues a render, returns immediately), but `CallStudentAsync()` is called after `await Task.Delay(500)` to wait for the DOM. This is a **timing hack**, not a state machine. It works 95% of the time.

The 5% failure mode is:
1. Student disconnects between `StateHasChanged()` and the 500ms delay
2. Lecturer calls `CallStudentAsync(peerId)` on a peer that no longer exists
3. PeerJS emits an error, the `call.on("close")` handler in `session.js` fires
4. `activeCalls.delete(studentPeerId)` removes the entry
5. `session.js` has no knowledge that C# `SessionState.AddStudentPeer()` still holds the stale `peerId`
6. The lecturer's `ParticipantPanel` shows the student as connected; the student sees "not connected"

This is the classic ghost-call state.

**Solution — DFA Implementation:**

The WebRTC connection lifecycle must be a Deterministic Finite Automaton with these properties:
- **Finite States:** `Idle, Signaling, Connecting, Connected, Degraded, Disconnected`
- **Deterministic:** One and only one state at a time per peer connection
- **No epsilon transitions:** Every state change requires an explicit trigger

```csharp
// New file: Models/PeerConnectionState.cs
public enum PeerConnectionState
{
    Idle,
    Signaling,      // PeerId sent to hub, awaiting lecturer's call
    Connecting,     // Lecturer called peer, ICE negotiation in progress  
    Connected,      // Stream flowing, RTCPeerConnection.connectionState == "connected"
    Degraded,       // ICE disconnected but reconnecting, quality downgraded
    Disconnected    // Peer destroyed, cleanup complete
}

public class PeerConnectionContext
{
    public string PeerId { get; set; }
    public string UserId { get; set; }
    public PeerConnectionState State { get; set; }
    public DateTime LastTransition { get; set; }
    public int ReconnectAttempts { get; set; }
    
    public bool CanTransitionTo(PeerConnectionState next) => (State, next) switch
    {
        (PeerConnectionState.Idle,         PeerConnectionState.Signaling)    => true,
        (PeerConnectionState.Signaling,    PeerConnectionState.Connecting)   => true,
        (PeerConnectionState.Connecting,   PeerConnectionState.Connected)    => true,
        (PeerConnectionState.Connected,    PeerConnectionState.Degraded)     => true,
        (PeerConnectionState.Degraded,     PeerConnectionState.Connecting)   => ReconnectAttempts < 3,
        (PeerConnectionState.Degraded,     PeerConnectionState.Disconnected) => true,
        (PeerConnectionState.Disconnected, PeerConnectionState.Signaling)    => true, // Manual rejoin
        _ => false
    };
}
```

The JavaScript `session.js` mirrors this DFA:
```javascript
const PeerState = { IDLE: 'idle', SIGNALING: 'signaling', CONNECTING: 'connecting', 
                    CONNECTED: 'connected', DEGRADED: 'degraded', DISCONNECTED: 'disconnected' };
const peerStates = new Map(); // peerId -> PeerState

function transitionState(peerId, fromState, toState) {
    const current = peerStates.get(peerId) ?? PeerState.IDLE;
    if (current !== fromState) {
        console.warn(`[DFA] Rejected: ${peerId} tried ${fromState}→${toState} but is in ${current}`);
        return false;
    }
    peerStates.set(peerId, toState);
    console.log(`[DFA] ${peerId}: ${fromState} → ${toState}`);
    return true;
}
```

This eliminates ghost calls: `callStudent()` only executes if `transitionState(peerId, SIGNALING, CONNECTING)` returns `true`.

---

### 2.3 `MockApiService` — The Ticking Time Bomb (SEN204 — Requirements Engineering)

**The Problem:**
`MockApiService.cs` is referenced in `SessionHub.cs` lines 151, 202, 273, 299; `SessionService.cs` lines 26, 31, 77; `SessionRecap.razor` line 268. Every reference is a `static` method call on in-memory data. The data never persists.

**Immediate Business Risk:** Any lecturer who creates 5 active sessions before the server is restarted loses all attendance records permanently. The `CalculateAttendanceScore()` engine operates on RAM state — when the process dies, the scores die.

**Migration Plan:**

**Step 1 — Repository Interface Layer:**
```csharp
// Services/Interfaces/IUserRepository.cs
public interface IUserRepository
{
    Task<User?> GetByMatricNoAsync(string matricNo);
    Task<IReadOnlyList<User>> GetAllAsync();
    Task<IReadOnlyList<User>> GetByRoleAsync(Role role);
    Task<bool> ExistsAsync(string matricNo);
}

// Services/Interfaces/ISessionRepository.cs  
public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(string sessionId);
    Task<Session> CreateAsync(Session session);
    Task UpdateAsync(Session session);
    Task<IReadOnlyList<Session>> GetActiveByLecturerAsync(string lecturerId);
}
```

**Step 2 — Make `MockApiService` implement `IUserRepository` (Temporary):**
This allows the existing code to compile while EF Core is wired up incrementally. No big-bang replacement.

**Step 3 — EF Core `ViidiiDbContext`:**
```csharp
public class ViidiiDbContext : DbContext
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ParticipantEventEntity> ParticipantEvents => Set<ParticipantEventEntity>();
    public DbSet<AttendanceLogEntity> AttendanceLogs => Set<AttendanceLogEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
}
```

---

## 3. The LAN Offline Architecture (Zero-Internet Mode)

### 3.1 Why PeerJS Fails Offline

PeerJS's default configuration sends SDP offers/answers to `0.peerjs.com:443`. When this server is unreachable (no internet), `new Peer()` in `session.js` throws a connection error and the peer never initializes. The whole WebRTC system is held hostage by a cloud signaling server.

**This is the most critical availability risk for FUTA.**

### 3.2 The Fix — ASP.NET as the Signaling Server

The ASP.NET host already runs a SignalR hub. We extend it to serve as a **WebRTC signaling endpoint**:

```csharp
// In Program.cs — add a PeerJS-compatible HTTP API
app.MapPost("/peerjs/id", () => Guid.NewGuid().ToString("N")[..16]);
app.MapGet("/peerjs/{peerId}/offer", ...);  // WebRTC offer relay via SignalR
app.MapPost("/peerjs/{peerId}/answer", ...); // WebRTC answer relay via SignalR
```

**PeerJS Configuration for Offline Mode (in `session.js`):**
```javascript
function createPeer(isOfflineMode) {
    const config = isOfflineMode ? {
        host: window.location.hostname, // Same LAN IP as ASP.NET server
        port: 7231,
        path: '/peerjs',
        config: { iceServers: [] } // No STUN/TURN needed on LAN
    } : {
        // Default cloud PeerJS config
    };
    return new Peer(config);
}
```

### 3.3 The "Catch-Up" Protocol for Late Joiners

When a student joins 20+ minutes late, they missed the file distribution. The distributed catch-up works as follows:

```
 LateStudent joins → [SessionHub] broadcasts "LatePeerJoined" to session group
 EarliestActivePeer receives event → opens DataChannel to LateStudent
 EarliestActivePeer.sendCatchUpPacket({ type: 'chatHistory', payload: messages[] })
 EarliestActivePeer.sendCatchUpPacket({ type: 'fileManifest', payload: files[] })
 LateStudent requests specific files → EarliestActivePeer streams chunks
 LateStudent displays "Synced from peer: [name]" in MessagingPanel
```

This requires no server round-trip. The `MessagingPanel.razor` message history is held in the existing client-side `_messages` list. The catch-up protocol just needs a JS interop function to serialize and transmit it over the DataChannel.

---

## 4. Database Schema — EF Core Code-First (PostgreSQL)

```sql
-- Users Table (replacing MockApiService._users static list)
CREATE TABLE "Users" (
    "MatricNo"      TEXT NOT NULL PRIMARY KEY,
    "Name"          TEXT NOT NULL,
    "PasswordHash"  TEXT NOT NULL,
    "Role"          INTEGER NOT NULL,  -- enum: Student=0, Lecturer=1, Admin=2
    "Department"    INTEGER,           -- nullable enum
    "Level"         INTEGER,           -- nullable enum
    "CreatedAt"     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Sessions Table (replacing SessionService._sessions ConcurrentDictionary)
CREATE TABLE "Sessions" (
    "SessionId"     TEXT NOT NULL PRIMARY KEY,
    "LecturerId"    TEXT NOT NULL REFERENCES "Users"("MatricNo"),
    "Title"         TEXT NOT NULL,
    "Status"        INTEGER NOT NULL, -- Active=0, Started=1, Ended=2
    "StartTime"     TIMESTAMPTZ,
    "EndTime"       TIMESTAMPTZ,
    "CreatedAt"     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "AllowedDepts"  INTEGER[] NOT NULL DEFAULT '{}',  -- array of Departments enum values
    "AllowedLevels" INTEGER[] NOT NULL DEFAULT '{}'   -- array of Levels enum values
);

-- Participant Events Table (replacing Session.ParticipantEvents in-memory dict)
-- This is the heart of the attendance scoring engine
CREATE TABLE "ParticipantEvents" (
    "Id"            BIGSERIAL PRIMARY KEY,
    "SessionId"     TEXT NOT NULL REFERENCES "Sessions"("SessionId"),
    "MatricNo"      TEXT NOT NULL REFERENCES "Users"("MatricNo"),
    "Status"        INTEGER NOT NULL,  -- StudentStatus enum: Active=0, InActive=1, etc.
    "OccurredAt"    TIMESTAMPTZ NOT NULL,
    INDEX("SessionId", "MatricNo", "OccurredAt")  -- Critical for CalculateAttendanceScore scan
);

-- Attendance Logs Table (new — for cryptographic QR check-in)
CREATE TABLE "AttendanceLogs" (
    "Id"            BIGSERIAL PRIMARY KEY,
    "SessionId"     TEXT NOT NULL REFERENCES "Sessions"("SessionId"),
    "MatricNo"      TEXT NOT NULL REFERENCES "Users"("MatricNo"),
    "TokenHash"     TEXT NOT NULL,          -- HMAC token that was scanned
    "ScannedAt"     TIMESTAMPTZ NOT NULL,
    "IpAddress"     TEXT NOT NULL,
    "IsValid"       BOOLEAN NOT NULL,       -- false = replay attack attempt
    UNIQUE("SessionId", "MatricNo"),        -- One valid check-in per student per session
    UNIQUE("TokenHash", "MatricNo")         -- One-time token use enforcement
);

-- Messages Table (replacing MessageService in-memory list)
CREATE TABLE "Messages" (
    "Id"            TEXT NOT NULL PRIMARY KEY,  -- Guid
    "SessionId"     TEXT NOT NULL REFERENCES "Sessions"("SessionId"),
    "UserId"        TEXT NOT NULL REFERENCES "Users"("MatricNo"),
    "UserName"      TEXT NOT NULL,
    "Content"       TEXT NOT NULL,
    "ParentId"      TEXT,                        -- null = top-level post; non-null = comment
    "IsFile"        BOOLEAN NOT NULL DEFAULT FALSE,
    "IsLecturerPost" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt"     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Message Reactions Table
CREATE TABLE "MessageReactions" (
    "MessageId"     TEXT NOT NULL REFERENCES "Messages"("Id"),
    "MatricNo"      TEXT NOT NULL REFERENCES "Users"("MatricNo"),
    "Emoji"         TEXT NOT NULL,
    PRIMARY KEY ("MessageId", "MatricNo", "Emoji")
);
```

---

## 5. Adaptive Quality — RTCPeerConnection Stats API

The `RTCPeerConnection.getStats()` API returns per-transport metrics every 5 seconds. The quality ladder controller reads:

```javascript
async function pollNetworkQuality(pc, peerId) {
    const stats = await pc.getStats();
    let rtt = 0, lossRate = 0;
    
    stats.forEach(report => {
        if (report.type === 'remote-inbound-rtp' && report.kind === 'video') {
            rtt = report.roundTripTime * 1000;  // ms
            const lost = report.packetsLost ?? 0;
            const received = report.packetsReceived ?? 1;
            lossRate = (lost / (lost + received)) * 100;
        }
    });
    
    const tier = rtt < 100 && lossRate < 1   ? 'HD'
               : rtt < 300 && lossRate < 5   ? 'SD'
               : rtt < 500                   ? 'AudioOnly'
               :                              'SapaMode';
    
    applyQualityTier(pc, peerId, tier);
}

function applyQualityTier(pc, peerId, tier) {
    const sender = pc.getSenders().find(s => s.track?.kind === 'video');
    if (!sender) return;
    
    const params = sender.getParameters();
    if (!params.encodings?.length) return;
    
    const [enc] = params.encodings;
    
    if (tier === 'HD')        { enc.maxBitrate = 1_500_000; enc.active = true; }
    else if (tier === 'SD')   { enc.maxBitrate = 500_000;  enc.active = true; }
    else if (tier === 'AudioOnly') { enc.active = false; }  // Kill video sender
    else /* SapaMode */       { 
        enc.active = false;
        // Also stop receiving: set preference for video=inactive via SDP renegotiation
        dotNetRef.invokeMethodAsync('OnSapaModeActivated', peerId);
    }
    
    sender.setParameters(params);
}
```

**Battery integration:** The existing `FlagIssue("BatteryLow")` call in `IssueButtons.razor` triggers `SessionHub.FlagIssue()`. This can be extended to simultaneously trigger `SapaMode` on the student's `VideoStage.razor` via a new SignalR event `ActivateSapaMode`.

---

## 6. Security Analysis — QR Attendance Anti-Spoofing

### Token Design

```
token = HMACSHA256_BASE64(
    key   = appsettings["Attendance:SecretKey"],  // 32-byte random, stored in secrets
    input = $"{sessionId}:{Math.Floor(unixTimeSeconds / 15)}"
)
```

**Window validation (server):**
```csharp
var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var validWindows = new[] { now / 15 - 1, now / 15, now / 15 + 1 }; // ±15s grace
var isValid = validWindows.Any(w => Hmac.Verify(token, $"{sessionId}:{w}", secretKey));
```

### Attack Vectors & Mitigations

| Attack | Mitigation |
|---|---|
| **Hostel WhatsApp QR share:** Student screenshottts QR, sends to friend | 15-second rotation + one-time use makes the screenshot stale before friend can open it |
| **QR replay by same student:** Scan twice to get two attendance records | `UNIQUE("SessionId", "MatricNo")` in `AttendanceLogs` returns HTTP 409 on second scan |
| **Automated scanner bot:** Script polls `/attend?token=...` rapidly | Rate limit: 3 requests/minute per IP via `AspNetCoreRateLimit` middleware |
| **Token brute-force:** Try all possible HMAC values | HMAC-SHA256 has 2^256 space; brute force is computationally infeasible |
| **Clock skew:** Student's phone is 20s behind server | ±1 window tolerance (±15s) covers typical NTP drift |
| **QR screenshot during screen share:** Lecturer shares screen while QR is visible | Document: Lecturer should NOT screen-share while QR attendance is active; add warning banner |

---

## 7. Risks & Mitigations Summary

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| FUTA Wi-Fi mass reconnect storm (100 students) | High | Critical | Task 2: Remove `Task.Delay` retry; implement join queue |
| Ghost WebRTC calls on 3G drop | High | High | Task 1: DFA state machine in C# + JS |
| Data loss on server restart | Critical | Critical | Task 3: EF Core + PostgreSQL migration |
| PeerJS cloud failure kills offline LAN mode | Medium | Critical | Task 4: Local PeerJS signaling via SignalR |
| QR proxy attendance | High | Medium | Task 5: HMAC rotating tokens + one-time use |
| Student phone death in 2-hour lecture | Very High | Low | Task 2 extension: `SapaMode` adaptive quality |
