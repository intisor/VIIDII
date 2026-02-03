# Bug Reports

> **For GitHub Copilot:** When you fix a bug, append your documentation below using the format in `copilot-instructions.md`

---

## BUG-001: Students Not Receiving Video Stream

**Date:** 2024-01-15  
**Severity:** ?? Critical  
**Component:** WebRTC Video Streaming

### Problem
Students joining live sessions could not see the lecturer's video stream. Peer connection established, DOM elements found, peer IDs exchanged, but video remained blank.

### Root Cause

**1. Race Condition in Call Initiation**
Lecturer was calling students in TWO places:
- In `handleStudentConnection()` when data connection opened
- In `CallStudentAsync()` when Blazor received peer ID
This created conflicts where multiple call attempts interfered.

**2. Premature Stream Rejection**
Student code rejected ANY new stream if one was already attached:
```javascript
if (isStreamAttached) {
    return; // ? Blocked all new streams including legitimate replacements
}
```

**3. Insufficient Wait Time**
Only 500ms delay between student peer ready and lecturer call. Not enough time for DOM/handlers to be ready.

**4. Poor Logging**
Minimal visibility into where WebRTC handshake was failing.

### Solution

**Fix 1: Single Call Path**
Removed automatic call in `handleStudentConnection()`, centralized through Blazor only.

**Fix 2: Smart Stream Replacement**
```javascript
// Check if truly duplicate (same ID) vs legitimate replacement (different ID)
if (remoteStream.id === attachedStreamId && isStreamAttached) {
    return; // Only block true duplicates
}
```

**Fix 3: Increased Wait Time**
Increased delay from 500ms ? 800ms in `OnReceivePeerId()`

**Fix 4: Comprehensive Logging**
Added detailed logs with `***` markers for critical events and stream track state.

**Files Modified:**
- `wwwroot/js/sessionInterop.js` - Fixed call initiation, stream handling, logging
- `Components/Pages/SessionView.razor` - Increased delay, improved logging

### Prevention
- Use centralized orchestration for WebRTC calls (avoid multiple paths)
- Allow stream replacement by comparing IDs
- Account for DOM readiness with adequate delays
- Add comprehensive logging for async operations
- Check stream track states (enabled, readyState) before use

---

## BUG-002: Session Cannot Be Established After Response Started

**Date:** 2025-01-03  
**Severity:** ?? Critical  
**Component:** Authentication / SignalR Hub

### Problem
Login functionality threw `InvalidOperationException: The session cannot be established after the response has started` when users attempted to log in. The exception occurred in `AuthService.LoginAsync()` when trying to store the MatricNo in HTTP Session. This completely blocked all user authentication.

### Root Cause
HTTP Session state cannot be modified after the HTTP response has started. In Blazor Server with Interactive Server render mode:

1. The Login page uses `@rendermode @(new InteractiveServerRenderMode(prerender: false))`
2. When the page loads, a SignalR/WebSocket connection is established for the interactive circuit
3. Once the Blazor circuit is active, the HTTP response stream has already begun
4. When `HandleLogin()` is invoked via button click, it executes within the SignalR circuit context
5. The `HttpContext` exists but its response has already started streaming
6. Calling `Session.SetString()` at this point throws the exception

**Technical Details:**
```csharp
// This fails because response already started in Blazor Server interactive mode
httpContext.Session.SetString("MatricNo", matricNo);
```

HTTP Session is designed for traditional request/response cycles, not for long-lived SignalR connections. The attempt to write session state during interactive Blazor callbacks is fundamentally incompatible with Blazor Server's architecture.

### Solution
Replaced HTTP Session storage with connection-scoped in-memory storage for SignalR authentication:

**Approach:**
1. Removed all HTTP Session read/write attempts from `AuthService`
2. Kept in-memory `_currentUser` storage in `AuthService` (scoped per Blazor circuit)
3. Added `GetCurrentMatricNo()` method to `AuthService` to expose MatricNo
4. Implemented connection-scoped dictionary in `SessionHub` to map SignalR connection IDs to MatricNo
5. Captured MatricNo in `OnConnectedAsync()` when SignalR connection establishes
6. Updated all hub methods to retrieve MatricNo from connection dictionary

**Architecture:**
- **Blazor Circuit Scope**: `AuthService._currentUser` stores authenticated user
- **SignalR Connection Scope**: `SessionHub._connectionMatricNos` maps connection ID to MatricNo
- **Fallback**: If connection mapping is missing, queries `AuthService` again

**Files Modified:**
- `Services/AuthService.cs` - Removed HTTP session storage in `LoginAsync()`, `LogoutAsync()`, `IsAuthenticated()`; added `GetCurrentMatricNo()` method
- `Hubs/SessionHub.cs` - Added `_connectionMatricNos` dictionary, `OnConnectedAsync()`, `OnDisconnectedAsync()` cleanup, `GetMatricNoForConnection()` helper; updated 11 hub methods to use connection-based MatricNo retrieval instead of HTTP session

**Code Example:**
```csharp
// SessionHub.cs - Connection-scoped storage
private static readonly ConcurrentDictionary<string, string> _connectionMatricNos = new();

public override async Task OnConnectedAsync()
{
    var matricNo = _authService.GetCurrentMatricNo();
    if (!string.IsNullOrEmpty(matricNo))
    {
        _connectionMatricNos[Context.ConnectionId] = matricNo;
    }
    await base.OnConnectedAsync();
}

private string? GetMatricNoForConnection()
{
    if (_connectionMatricNos.TryGetValue(Context.ConnectionId, out var matricNo))
        return matricNo;
    
    // Fallback
    return _authService.GetCurrentMatricNo();
}
```

### Prevention
- **Never attempt to write HTTP Session state from Blazor Server interactive components** - it will fail after the response starts
- Use **scoped services** for per-circuit state (like `AuthService` with `AddScoped`)
- Use **connection-scoped dictionaries** in SignalR hubs for per-connection state
- For Blazor Server apps, prefer:
  - In-memory circuit-scoped state for user authentication
  - SignalR connection context for hub-specific data
  - Claims-based authentication for cross-cutting concerns
- HTTP Session is only appropriate for:
  - Static server rendering (SSR) without interactivity
  - Traditional request/response patterns
  - Initial page load data (before interactive mode activates)
- When migrating from traditional ASP.NET to Blazor Server, audit all Session usage and replace with appropriate scoped storage

---

## BUG-003: Blazor Server Session Segregation & Authentication Architecture

**Date:** 2025-01-28  
**Severity:** ?? Critical (Multiple Related Issues)  
**Component:** Session Pages, Authentication, SignalR Integration

### Problem
After implementing session page segregation (lecturer vs student views), multiple critical bugs emerged:

1. **Blank Pages**: Both lecturer and student session pages showed blank screens
2. **Missing JSInvokable Methods**: JavaScript couldn't call C# methods
3. **Authentication Loss on Refresh**: Users logged out on page refresh
4. **JavaScript Interop Timing Errors**: `ProtectedLocalStorage` calls failed during component initialization
5. **Empty User IDs in SignalR**: Hub methods received empty/null MatricNo values
6. **Connection Timeouts**: "Server timeout elapsed without receiving a message"
7. **Multiple Reconnections**: SignalR constantly reconnecting

### Root Causes

#### **Issue 1: Missing Component Parameter**
`LecturerSessionView` was passing `ParticipantScores` parameter to `ParticipantSidebar` component, but the component didn't accept that parameter.

```csharp
// Caused: InvalidOperationException: Object of type 'ParticipantSidebar' 
// does not have a property matching the name 'ParticipantScores'
```

#### **Issue 2: Missing JSInvokable Methods**
When creating separate `LecturerSessionView` and `StudentSessionView` pages, JSInvokable methods from the original `SessionView` were not copied over. JavaScript was calling methods that didn't exist.

```
Error: The type 'StudentSessionView' does not contain a public invokable method 
with [JSInvokableAttribute("OnStudentPeerReady")]
```

#### **Issue 3: In-Memory Auth Not Persisting**
Original `AuthService` stored user only in `_currentUser` field (in-memory, scoped per circuit). Page refresh created new circuit, losing authentication state.

#### **Issue 4: JavaScript Interop Called Too Early**
`AuthService.InitializeAsync()` was called in `OnInitializedAsync()` and from SignalR hub, but `ProtectedLocalStorage` requires JavaScript interop which isn't available until `OnAfterRenderAsync()`.

```
JavaScript interop calls cannot be issued at this time. This is because the 
component is being statically rendered. When prerendering is enabled, JavaScript 
interop calls can only be performed during the OnAfterRenderAsync lifecycle method.
```

#### **Issue 5: Scoped Service Instance Mismatch**
`AuthService` is registered as `Scoped`. Blazor circuit gets one instance (has user), SignalR hub gets different instance (no user). They don't share `_currentUser` field!

```
Component logs: [AuthService] Returning user: Goodluck ?
Hub logs:       Sent peer ID for user  to others        ? (empty!)
```

### Solution

Comprehensive multi-part fix addressing architecture, lifecycle, and state management:

#### **Fix 1: Remove Invalid Parameter**
```csharp
// LecturerSessionView.razor - BEFORE
<ParticipantSidebar ParticipantScores="@State.ParticipantScores" ... />

// AFTER
<ParticipantSidebar Participants="@State.Participants" ... />
```

#### **Fix 2: Add All JSInvokable Methods**
Added to `LecturerSessionView.razor`:
- `OnLecturerPeerReady(string peerId)`
- `OnStudentConnected(string peerId)`
- `OnStudentDisconnected(string peerId)`
- `OnStreamTypeChanged(string streamType)`
- `OnReceivePeerId(string userId, string peerId)`

Added to `StudentSessionView.razor`:
- `OnStudentPeerReady(string peerId)`
- `OnStreamReceived()`
- `OnStreamLost()`
- `OnPeerError(string errorType)`

#### **Fix 3: Implement Persistent Authentication**
```csharp
// Services/AuthService.cs
public class AuthService
{
    private readonly ProtectedLocalStorage _protectedLocalStorage;
    private User? _currentUser;
    private bool _isInitialized = false;
    private const string AUTH_KEY = "viidii_auth_user";
    
    public async Task<(bool, string?, User?)> LoginAsync(string matricNo, string password)
    {
        // ... verify password ...
        
        _currentUser = user;
        _isInitialized = true;
        
        // ? Persist to encrypted browser storage
        await _protectedLocalStorage.SetAsync(AUTH_KEY, matricNo);
        
        return (true, null, user);
    }
    
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        
        // ? Restore from browser storage
        var result = await _protectedLocalStorage.GetAsync<string>(AUTH_KEY);
        if (result.Success && !string.IsNullOrEmpty(result.Value))
        {
            var user = MockApiService.GetUsers()
                .FirstOrDefault(u => u.MatricNo == result.Value);
            if (user != null)
            {
                _currentUser = user;
            }
        }
        
        _isInitialized = true;
    }
}
```

#### **Fix 4: Correct JavaScript Interop Lifecycle**
```csharp
// Components/Routes.razor - BEFORE
protected override async Task OnInitializedAsync()
{
    await AuthService.InitializeAsync(); // ? Too early!
}

// AFTER
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await AuthService.InitializeAsync(); // ? JS available now
        StateHasChanged();
    }
}

// SessionViewBase.cs - Moved SignalR setup to OnAfterRenderAsync
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await AuthService.InitializeAsync(); // ? Initialize auth first
        await SetupHubConnectionAsync();     // ? Then connect SignalR
        await OnRoleSpecificAfterRenderAsync();
    }
}
```

#### **Fix 5: Explicit MatricNo Parameter to Hub**
```csharp
// SessionViewBase.cs - Pass MatricNo explicitly
var matricNo = State.CurrentUser?.MatricNo;
await _hubConnection.SendAsync("StartSession", SessionId, matricNo);

// SessionHub.cs - Accept and cache MatricNo
public async Task StartSession(string sessionId, string matricNo)
{
    // ? Cache immediately for this connection
    if (!string.IsNullOrEmpty(matricNo))
    {
        _connectionMatricNos[Context.ConnectionId] = matricNo;
    }
    
    // Now all hub methods can retrieve it from cache
    var userMatricNo = await GetMatricNoForConnectionAsync();
}
```

#### **Fix 6: Update All Hub Methods**
Updated 8 hub methods to use `await GetMatricNoForConnectionAsync()`:
- `SendPeerId()`, `CreatePost()`, `CreateComment()`
- `UpdateTabStatus()`, `FlagIssue()`, `ConfirmActive()`
- `NotifyStreamChange()`, `EndSession()`

**Files Modified:**
- `Components/Pages/SessionViewBase.cs` - Base class for shared session logic; moved SignalR setup to OnAfterRenderAsync
- `Components/Pages/LecturerSessionView.razor` - Lecturer-only page with camera test, screen share, participant monitoring
- `Components/Pages/StudentSessionView.razor` - Student-only page with peer connection, engagement modal
- `Components/Routes.razor` - Moved AuthService initialization to OnAfterRenderAsync
- `Services/AuthService.cs` - Added ProtectedLocalStorage for persistent auth; InitializeAsync() method
- `Hubs/SessionHub.cs` - Removed problematic InitializeAsync call; accept explicit MatricNo parameter; updated all methods to async
- `Models/SessionState.cs` - Added ParticipantScores property
- `Program.cs` - Added testing mode timeout configurations

### Prevention

**Component Architecture:**
- Separate pages by role early to avoid conditional logic sprawl
- Create base classes for shared functionality
- Ensure all JavaScript-callable methods have `[JSInvokable]` attribute

**Authentication & State:**
- Use `ProtectedLocalStorage` or `ProtectedSessionStorage` for persistent auth in Blazor Server
- Never rely on HTTP Session in Blazor Server interactive mode
- For SignalR hubs, pass user context explicitly rather than relying on scoped services

**Blazor Lifecycle:**
- JavaScript interop (including storage APIs) only in `OnAfterRenderAsync()`
- Never call storage APIs in `OnInitializedAsync()` or from SignalR hub context
- Defer SignalR connection until after component rendering

**SignalR Integration:**
- Pass user context explicitly to hub methods
- Cache user data per connection in static dictionaries
- Don't rely on scoped services being same instance between component and hub

**Testing Checklist:**
- ? Page refresh retains authentication
- ? SignalR methods receive correct user IDs
- ? No JavaScript interop errors in console
- ? Connection remains stable (no timeout/reconnects)
- ? All JSInvokable methods respond to JavaScript calls

**Architecture Pattern:**
```
Component (Scoped) ? Explicit Parameters ? SignalR Hub ? Connection Dictionary
     ?                                           ?
ProtectedLocalStorage                    _connectionMatricNos
(Persists across refresh)                (Per-connection cache)
```

---

## BUG-002: Session Cannot Be Established After Response Started

**Date:** 2025-01-03  
**Severity:** ?? Critical  
**Component:** Authentication / SignalR Hub

### Problem
Login functionality threw `InvalidOperationException: The session cannot be established after the response has started` when users attempted to log in. The exception occurred in `AuthService.LoginAsync()` when trying to store the MatricNo in HTTP Session. This completely blocked all user authentication.

### Root Cause
HTTP Session state cannot be modified after the HTTP response has started. In Blazor Server with Interactive Server render mode:

1. The Login page uses `@rendermode @(new InteractiveServerRenderMode(prerender: false))`
2. When the page loads, a SignalR/WebSocket connection is established for the interactive circuit
3. Once the Blazor circuit is active, the HTTP response stream has already begun
4. When `HandleLogin()` is invoked via button click, it executes within the SignalR circuit context
5. The `HttpContext` exists but its response has already started streaming
6. Calling `Session.SetString()` at this point throws the exception

**Technical Details:**
```csharp
// This fails because response already started in Blazor Server interactive mode
httpContext.Session.SetString("MatricNo", matricNo);
```

HTTP Session is designed for traditional request/response cycles, not for long-lived SignalR connections. The attempt to write session state during interactive Blazor callbacks is fundamentally incompatible with Blazor Server's architecture.

### Solution
Replaced HTTP Session storage with connection-scoped in-memory storage for SignalR authentication:

**Approach:**
1. Removed all HTTP Session read/write attempts from `AuthService`
2. Kept in-memory `_currentUser` storage in `AuthService` (scoped per Blazor circuit)
3. Added `GetCurrentMatricNo()` method to `AuthService` to expose MatricNo
4. Implemented connection-scoped dictionary in `SessionHub` to map SignalR connection IDs to MatricNo
5. Captured MatricNo in `OnConnectedAsync()` when SignalR connection establishes
6. Updated all hub methods to retrieve MatricNo from connection dictionary

**Architecture:**
- **Blazor Circuit Scope**: `AuthService._currentUser` stores authenticated user
- **SignalR Connection Scope**: `SessionHub._connectionMatricNos` maps connection ID to MatricNo
- **Fallback**: If connection mapping is missing, queries `AuthService` again

**Files Modified:**
- `Services/AuthService.cs` - Removed HTTP session storage in `LoginAsync()`, `LogoutAsync()`, `IsAuthenticated()`; added `GetCurrentMatricNo()` method
- `Hubs/SessionHub.cs` - Added `_connectionMatricNos` dictionary, `OnConnectedAsync()`, `OnDisconnectedAsync()` cleanup, `GetMatricNoForConnection()` helper; updated 11 hub methods to use connection-based MatricNo retrieval instead of HTTP session

**Code Example:**
```csharp
// SessionHub.cs - Connection-scoped storage
private static readonly ConcurrentDictionary<string, string> _connectionMatricNos = new();

public override async Task OnConnectedAsync()
{
    var matricNo = _authService.GetCurrentMatricNo();
    if (!string.IsNullOrEmpty(matricNo))
    {
        _connectionMatricNos[Context.ConnectionId] = matricNo;
    }
    await base.OnConnectedAsync();
}

private string? GetMatricNoForConnection()
{
    if (_connectionMatricNos.TryGetValue(Context.ConnectionId, out var matricNo))
        return matricNo;
    
    // Fallback
    return _authService.GetCurrentMatricNo();
}
```

### Prevention
- **Never attempt to write HTTP Session state from Blazor Server interactive components** - it will fail after the response starts
- Use **scoped services** for per-circuit state (like `AuthService` with `AddScoped`)
- Use **connection-scoped dictionaries** in SignalR hubs for per-connection state
- For Blazor Server apps, prefer:
  - In-memory circuit-scoped state for user authentication
  - SignalR connection context for hub-specific data
  - Claims-based authentication for cross-cutting concerns
- HTTP Session is only appropriate for:
  - Static server rendering (SSR) without interactivity
  - Traditional request/response patterns
  - Initial page load data (before interactive mode activates)
- When migrating from traditional ASP.NET to Blazor Server, audit all Session usage and replace with appropriate scoped storage

---





## BUG-004: Student Engagement Tracking - Duplicate Models and Missing Throttling

**Date:** 2025-01-30  
**Severity:** ?? Medium  
**Component:** Engagement Tracking / Tab Visibility

### Problem
During implementation of student engagement tracking for attendance system, several issues were discovered:

1. **Duplicate Models Created**: New models (`StudentEngagementStatus`, `ParticipantInfo`, `EngagementService`) were created that duplicated existing functionality
2. **Missing Tab Visibility Throttling**: Tab status updates were sent on every visibility change without throttling, causing potential UI spam for lecturers
3. **Debugging Pain**: `StateHasChanged()` calls caused debugger to step into framework code repeatedly

### Root Cause

**Issue 1: Overlooked Existing Implementation**
The system already had complete engagement tracking implemented:
- `Session.StudentStatus` enum (in Models/User.cs) with 5 states
- `ParticipantPingService` - Background service sending "AreYouThere" every 2-5 minutes (randomized)
- All SignalR hub methods (`UpdateTabStatus`, `FlagIssue`, `ConfirmActive`)
- All UI components (`ParticipantPanel`, `EngagementModal`, `IssueButtons`)
- All JavaScript functions (`getBatteryLevel`, `getNetworkStatus`, `setupTabVisibilityListener`)

**Issue 2: Removed UX Throttling**
Original JavaScript implementation had **50-second throttle** for tab visibility changes.

**Issue 3: Framework Code Stepping**
Multiple `InvokeAsync(StateHasChanged)` calls caused debugger to step into Blazor framework internals.

### Solution

**Fix 1: Remove Duplicate Models**
Deleted duplicate files:
- Models/StudentEngagementStatus.cs
- Models/ParticipantInfo.cs
- Services/EngagementService.cs

**Fix 2: Restore Tab Visibility Throttling**
Added 50-second throttle back to StudentSessionView.razor

**Fix 3: Add DebuggerStepThrough Helper**
Created UpdateUI() helper method in SessionViewBase.cs

**Fix 4: Add ReceiveParticipantStatuses Handler**
Added missing handler to SessionViewBase.cs

**Files Modified:**
- Components/Pages/StudentSessionView.razor - Added tab visibility throttling
- Components/Pages/SessionViewBase.cs - Added UpdateUI() helper and status handler
- Program.cs - Removed duplicate EngagementService registration

### Prevention
- Search thoroughly for existing implementations before creating new ones
- Preserve throttling/debouncing patterns from original code
- Enable "Just My Code" in Visual Studio debugging settings
- Use [DebuggerStepThrough] for wrapper methods

---

## BUG-005: Video Element Not Found for 3rd+ Student Connections

**Date:** 2025-01-30  
**Severity:** ?? High  
**Component:** Blazor Server Rendering / PeerJS Integration

### Problem
When 3 or more students joined a session simultaneously, the 3rd+ students failed to establish peer connections because JavaScript couldn't find the video element.

### Root Cause
**Blazor Server Rendering Timing Issue** - For 3rd+ students, server under load, SignalR queueing delays, DOM updates not reaching browser before JavaScript executes.

**Specific Issues:**
1. C# code had no wait for DOM stability
2. JavaScript only retried once with 200ms timeout

### Solution

**Two-Layer Fix:**
1. C# Pre-Render Delay: Added 100ms delay before calling JS
2. JS Robust Retries: Implemented 5-retry loop with 300ms intervals (up to 1.5s total)

**Files Modified:**
- Components/Pages/StudentSessionView.razor - Added 100ms delay
- wwwroot/js/sessionInterop.js - Implemented 5-retry loop

### Prevention
- Always add small delay (100ms) in OnAfterRenderAsync before calling JS that needs DOM
- Implement retry logic in JavaScript for DOM element access (5 retries, 300ms each)
- Account for Blazor Server's async rendering and SignalR batching
- Test with 5+ concurrent connections to catch timing issues

---

## BUG-006: Lecturer Session Ends on Reconnection

**Date:** 2025-01-30  
**Severity:** ?? Critical  
**Component:** Session Persistence / SignalR Reconnection

### Problem
When a lecturer refreshed the page or experienced a temporary disconnection, the entire session would end for all students. This created a poor UX where a simple network hiccup or accidental refresh would disrupt the entire class.

### Root Cause
While the backend correctly preserved sessions (OnDisconnectedAsync only handled students), the frontend lacked proper reconnection handling for lecturers. Additionally, there was no visual feedback to reassure lecturers that the session continued during reconnection.

### Solution

**Fix 1: Automatic Lecturer Reconnection**
Updated SessionHub.StartSession to handle lecturer reconnection:
```csharp
if (IsSessionLecturer(sessionId, userMatricNo))
{
    session.LecturerConnectionId = Context.ConnectionId; // Update connection ID
    
    // Send current scores/statuses if session already started
    if (session.Status == SessionStatus.Started)
    {
        var currentScores = _sessionService.CalculateAttendanceScore(sessionId);
        await Clients.Caller.SendAsync("ReceiveParticipantScoreDetails", currentScores);
        var currentStatuses = _sessionService.GetParticipantStatus(sessionId);
        await Clients.Caller.SendAsync("ReceiveParticipantStatuses", currentStatuses);
    }
}
```

**Fix 2: Progressive Reconnection Strategy**
```csharp
.WithAutomaticReconnect(new[] { 
    TimeSpan.Zero,           // Immediate
    TimeSpan.FromSeconds(2),  // 2s
    TimeSpan.FromSeconds(5),  // 5s
    TimeSpan.FromSeconds(10)  // 10s
})
```

**Fix 3: Reconnection Event Handlers**
```csharp
_hubConnection.Reconnecting += OnReconnecting;  // Show "Reconnecting..."
_hubConnection.Reconnected += OnReconnected;    // Auto-rejoin session
_hubConnection.Closed += OnConnectionClosed;    // Show error
```

**Fix 4: Visual Feedback Component**
Created ConnectionStatusBanner.razor:
- Yellow banner: "Reconnecting..." with spinner
- Green banner: "Reconnected successfully - Students are still in session" (auto-dismiss 3s)
- Red banner: "Connection lost"

**Fix 5: Role-Specific Error Messages**
```csharp
if (IsLecturer)
{
    State.SetError("Reconnected but failed to rejoin. Students can still see your stream.");
}
else
{
    State.SetError("Reconnected but failed to rejoin session. Please refresh.");
}
```

**Files Modified:**
- Components/Pages/SessionViewBase.cs - Added reconnection handlers, 4-retry strategy
- Components/Shared/ConnectionStatusBanner.razor - New visual feedback component
- Models/SessionState.cs - Added ConnectionStatus and ConnectionMessage properties
- Components/Pages/LecturerSessionView.razor - Added connection status banner
- Components/Pages/StudentSessionView.razor - Added connection status banner

### Prevention
- Always preserve session state server-side independent of connection state
- Implement automatic reconnection with progressive backoff
- Provide clear visual feedback during reconnection
- Test reconnection scenarios: refresh, network drop, server restart
- Different error messages for different roles (lecturer vs student)

---

## BUG-007: Browser Tab Throttling Causes Disconnections

**Date:** 2025-01-30  
**Severity:** ?? High  
**Component:** Blazor Server / Browser Lifecycle

### Problem
When users switched to another browser tab, the Blazor Server connection would timeout after 30 seconds, causing disconnections. Modern browsers throttle background tabs, delaying JavaScript timers and network requests.

### Root Cause
Blazor Server uses SignalR/WebSocket connections that require periodic "keep-alive" messages. When a tab is backgrounded:
1. Browser throttles JavaScript timers to 1-second minimum
2. Network requests are deprioritized
3. Keep-alive messages may be delayed beyond server timeout (30s)
4. Server considers circuit dead and disconnects

### Solution

**Fix 1: Keep-Alive with Tab Visibility Check**
```csharp
private void StartKeepAliveTimer()
{
    _keepAliveTimer = new System.Threading.Timer(async _ =>
    {
        try
        {
            if (_hubConnection?.State == HubConnectionState.Connected && !_isDisposed)
            {
                bool isVisible = await IsTabVisibleAsync();
                await _hubConnection.SendAsync("KeepAlive");
                
                if (isVisible)
                {
                    Console.WriteLine($"[KeepAlive] Ping sent at {DateTime.Now:HH:mm:ss}");
                }
                else
                {
                    Console.WriteLine($"[KeepAlive] Ping sent while tab hidden at {DateTime.Now:HH:mm:ss}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KeepAlive] Error: {ex.Message}");
        }
    }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
}
```

**Fix 2: Automatic Reconnection Handlers**
Added OnReconnecting, OnReconnected, OnConnectionClosed handlers to automatically rejoin session after reconnection with MatricNo preservation.

**Fix 3: State Preservation on Reconnection**
```csharp
private async Task OnReconnected(string? connectionId)
{
    var matricNo = State.CurrentUser?.MatricNo;
    if (!string.IsNullOrEmpty(matricNo))
    {
        await _hubConnection!.SendAsync("StartSession", SessionId, matricNo);
        // Session state restored automatically
    }
}
```

**Files Modified:**
- Components/Pages/SessionViewBase.cs - Enhanced keep-alive timer, added reconnection handlers

### Prevention
- Use System.Threading.Timer (not JavaScript-based) for critical timers
- Implement automatic reconnection with state preservation
- Log tab visibility status for debugging
- Test with tab backgrounding for 2+ minutes
- Consider increasing server timeout in production (currently 5min for testing)

---

## BUG-008: Excessive UI Re-renders from Engagement Tracking

**Date:** 2025-01-30  
**Severity:** ?? Medium  
**Component:** UI Performance / State Management

### Problem
When multiple students became inactive simultaneously (not responding to "Are You There?"), the lecturer's UI would re-render multiple times in quick succession, causing performance issues and debugger stepping into framework code repeatedly.

### Root Cause
The ReceiveParticipantStatuses SignalR handler called StateHasChanged() immediately for every status update:
```
Student 1 inactive -> StateHasChanged()
Student 2 inactive -> StateHasChanged() (100ms later)
Student 3 inactive -> StateHasChanged() (100ms later)
... 5 re-renders in 500ms
```

### Solution

**Fix 1: Debounced UI Update Helper**
```csharp
[System.Diagnostics.DebuggerStepThrough]
protected void UpdateUIDebounced(Action updateAction, int delayMs = 500)
{
    _debounceTimer?.Dispose();
    updateAction(); // Update state immediately
    
    // Debounce only the StateHasChanged call
    _debounceTimer = new System.Threading.Timer(_ =>
    {
        InvokeAsync(StateHasChanged);
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }, null, delayMs, Timeout.Infinite);
}
```

**Fix 2: Applied to Participant Status Handler**
```csharp
_hubConnection.On<Dictionary<string, Session.StudentStatus>>("ReceiveParticipantStatuses", statuses =>
{
    UpdateUIDebounced(() =>
    {
        State.ParticipantStatuses = statuses;
        Console.WriteLine($"Received participant statuses update: {statuses.Count} participants");
    }, delayMs: 300); // 300ms debounce
}
```

**Result:**
- Before: 5 students going inactive = 5 re-renders
- After: 5 students going inactive = 1 re-render (batched)

**Files Modified:**
- Components/Pages/SessionViewBase.cs - Added UpdateUIDebounced helper, applied to status handler

### Prevention
- Use debouncing for updates that can arrive in rapid succession
- Keep UpdateUI() for immediate updates (single events)
- Use UpdateUIDebounced() for batch updates (multiple simultaneous events)
- Add [DebuggerStepThrough] to wrapper methods
- Monitor console for excessive "StateHasChanged" logs

---

## BUG-009: Messaging UI Broken - Messages Not Displaying

**Date:** 2025-01-30  
**Severity:** ?? High  
**Component:** MessagingPanel / SignalR Integration

### Problem
The messaging system appeared broken with multiple issues:
1. Messages sent but not displayed in UI
2. Emoji reactions showing as ?? instead of thumbs up
3. Auto-scroll not working
4. Messages not updating in real-time

### Root Cause

**Issue 1: SignalR Handler Threading**
Handlers were calling StateHasChanged() synchronously instead of using InvokeAsync:
```csharp
// WRONG - Can cause threading issues
private async Task HandleReceivePost(Message message)
{
    Messages.Add(message);
    StateHasChanged(); // Synchronous call from SignalR thread
}
```

**Issue 2: Emoji Encoding**
Emoji was using placeholder string "??" instead of actual Unicode emoji character.

**Issue 3: Wrong DOM Selector**
JavaScript was targeting .messages-container but actual element was .messages-wrapper.

### Solution

**Fix 1: Thread-Safe SignalR Handlers**
```csharp
private async Task HandleReceivePost(Message message)
{
    if (message.sessionId != SessionId) return;
    Messages.Add(message);
    await InvokeAsync(StateHasChanged); // Thread-safe
    await ScrollToBottom();
}

private async Task HandleReceiveMessages(List<Message> messages)
{
    Messages = messages.Where(m => m.sessionId == SessionId).ToList();
    await InvokeAsync(StateHasChanged); // Thread-safe
}
```

**Fix 2: Fixed Emoji Rendering**
```csharp
// MessagingPanel.razor
await HubConnection.SendAsync("AddReaction", SessionId, messageId, "??");

// MessageService.cs
public int ThumbsUpCount => Reactions.Count(r => r.Emoji == "??");
```

**Fix 3: Fixed Auto-Scroll**
```javascript
const container = document.querySelector('.messages-wrapper'); // Corrected selector
if (container) {
    container.scrollTop = container.scrollHeight;
}
```

**Files Modified:**
- Components/Shared/MessagingPanel.razor - Fixed handlers, emoji, scroll selector
- Services/MessageService.cs - Fixed emoji in ThumbsUpCount property

### Prevention
- Always use InvokeAsync(StateHasChanged) in SignalR handlers
- Test emoji rendering in different browsers
- Verify DOM selectors match actual element classes
- Test messaging with multiple users simultaneously

---

## BUG-010: Send Button Not Clickable in Messaging UI

**Date:** 2025-01-30  
**Severity:** ?? Critical  
**Component:** MessagingPanel / Input Binding

### Problem
Lecturer could type messages but the send button remained disabled (grayed out). Clicking had no effect.

### Root Cause
Blazor's @bind directive uses onchange event by default, which only fires when the input loses focus (user clicks away). The button's disabled condition checked NewMessageContent, which wasn't updating in real-time as the user typed.

**Sequence:**
1. User types "hello"
2. NewMessageContent still empty (waiting for blur event)
3. Button stays disabled: string.IsNullOrWhiteSpace(NewMessageContent) == true
4. User clicks away -> NewMessageContent updates -> Button enables
5. User confused why button didn't work while typing

### Solution

**Fix 1: Real-Time Input Binding**
```razor
<!-- Before - Updates on blur -->
<input @bind="NewMessageContent" />

<!-- After - Updates on every keystroke -->
<input @bind="NewMessageContent" @bind:event="oninput" />
```

**Fix 2: Added HubConnection Check**
```razor
disabled="@(string.IsNullOrWhiteSpace(NewMessageContent) || IsUploadingFile || HubConnection == null)"
```

**Fix 3: Added Debug Logging**
```csharp
private async Task SendMessage()
{
    Console.WriteLine($"SendMessage called - Content: '{NewMessageContent}', HubConnection: {HubConnection != null}, IsLecturer: {IsLecturer}");
    
    if (string.IsNullOrWhiteSpace(NewMessageContent))
    {
        Console.WriteLine("Message is empty or whitespace");
        return;
    }
    // ... rest of validation
}
```

**Files Modified:**
- Components/Shared/MessagingPanel.razor - Fixed input binding, added debug logging

### Prevention
- Use @bind:event="oninput" for inputs that control button states
- Use @bind:event="onchange" (default) for forms submitted via Enter or button
- Test UI interactions without clicking away from inputs
- Add debug logging for user-facing features

---

## BUG-011: Application Crash from EngagementModal Threading Issue

**Date:** 2025-01-30  
**Severity:** ?? Critical - Application Crash  
**Component:** EngagementModal / Threading

### Problem
Application crashed with unhandled exception when students didn't respond to "Are You There?" modal:
```
System.InvalidOperationException: The current thread is not associated with the Dispatcher. 
Use InvokeAsync() to switch execution to the Dispatcher when triggering rendering or component state.
   at Microsoft.AspNetCore.Components.ComponentBase.StateHasChanged()
   at VIIDII.Components.Shared.EngagementModal.Hide()
   at VIIDII.Components.Shared.EngagementModal.AutoDismiss()
```

### Root Cause
The EngagementModal used System.Threading.Timer for the 30-second countdown. When the timer expired, it called AutoDismiss() -> Hide() -> StateHasChanged() directly from a background thread. Blazor requires all UI updates to happen on the Dispatcher thread, causing an immediate crash.

**Why It Crashed:**
1. Student doesn't respond to modal
2. After 30 seconds, System.Threading.Timer fires on background thread
3. AutoDismiss() calls Hide() on same background thread
4. Hide() calls StateHasChanged() on background thread
5. Blazor detects thread violation -> throws InvalidOperationException -> app crashes

### Solution

**Fix 1: Thread-Safe Hide Method**
```csharp
// Before - CRASHES
private async Task Hide()
{
    _timer?.Dispose();
    IsShown = false;
    await JSRuntime.InvokeVoidAsync("modalInterop.hide", "engagementModal");
    await OnHidden.InvokeAsync();
    StateHasChanged(); // Called on background thread
}

// After - SAFE
private async Task Hide()
{
    _timer?.Dispose();
    IsShown = false;
    try
    {
        await JSRuntime.InvokeVoidAsync("modalInterop.hide", "engagementModal");
        await OnHidden.InvokeAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error hiding engagement modal: {ex.Message}");
    }
    await InvokeAsync(StateHasChanged); // Marshals to UI thread
}
```

**Fix 2: Thread-Safe AutoDismiss**
```csharp
// Before
private async Task AutoDismiss()
{
    Console.WriteLine("Engagement timeout - student did not respond");
    await Hide(); // Still on background thread
}

// After
private async Task AutoDismiss()
{
    Console.WriteLine("Engagement timeout - student did not respond");
    await InvokeAsync(async () => await Hide()); // Switch to UI thread first
}
```

**Fix 3: Thread-Safe Show Method**
```csharp
await InvokeAsync(StateHasChanged); // Changed from StateHasChanged()
```

**Fix 4: Thread-Safe HideEngagementModal Callback**
```csharp
// StudentSessionView.razor
private async Task HideEngagementModal()
{
    _showEngagementModal = false;
    await InvokeAsync(StateHasChanged); // Changed from StateHasChanged()
}
```

**Files Modified:**
- Components/Shared/EngagementModal.razor - Fixed all StateHasChanged calls
- Components/Pages/StudentSessionView.razor - Fixed HideEngagementModal callback

### Prevention
- **CRITICAL RULE**: Never call StateHasChanged() directly from System.Threading.Timer callbacks
- Always use InvokeAsync(StateHasChanged) when:
  - Called from background threads
  - Called from timers (System.Threading.Timer, System.Timers.Timer)
  - Called from Task.Run() or thread pool threads
  - Unsure about thread context
- Use InvokeAsync() for safety - it's a no-op if already on UI thread
- Test timeout/auto-dismiss scenarios thoroughly
- Monitor console for threading exceptions during testing

**Blazor Threading Golden Rules:**
```csharp
// ? SAFE - Always works
await InvokeAsync(StateHasChanged);

// ? UNSAFE - Crashes if called from background thread
StateHasChanged();

// ? SAFE - Timer callbacks need InvokeAsync
_timer = new System.Threading.Timer(async _ =>
{
    await InvokeAsync(StateHasChanged);
}, null, 1000, 1000);
```

---

## BUG-012: DateTime Timezone Inconsistency Causing Negative Duration

**Date:** 2026-02-02  
**Severity:** ?? Critical  
**Component:** Session Timing / Attendance Scoring

### Problem
Session attendance scoring was failing with "invalid duration" errors showing negative minutes (e.g., -58.7 minutes). This completely broke the attendance tracking system, preventing lecturers from seeing any attendance scores for ended sessions.

### Root Cause
The application was using inconsistent datetime references across the codebase:
- Session times stored with `.AddHours(1)` to convert UTC to West Africa Time (WAT)
- Score calculations using pure `DateTime.UtcNow` 
- Comparisons between these mixed timestamps produced negative durations

**Code Evidence:**
```csharp
// Session.StartTime stored as:
DateTime.UtcNow.AddHours(1)  // e.g., 6:00 PM WAT

// But CalculateAttendanceScore used:
var endTime = session.EndTime ?? DateTime.UtcNow;  // e.g., 5:02 PM UTC

// Result: endTime (5:02 PM) < startTime (6:00 PM) = -58 minutes!
```

**Console Logs:**
```
CalculateAttendanceScore: Session 20260202-ABCDEF has invalid duration (-58.7 min), returning empty scores
```

### Solution

**Fix: Pure UTC Storage Throughout**
Removed all `.AddHours(1)` timezone adjustments from server-side code and stored all timestamps as pure UTC.

**UI Display Only:**
Added inline `.AddHours(1)` for display to users in WAT timezone.

**Files Modified:**
- Models/User.cs - Session.StartTime default initialization
- Services/SessionService.cs - 6 methods updated
- Components/Pages/SessionRecap.razor - Added .AddHours(1) for display only
- Components/Shared/TimelineItem.razor - Added .AddHours(1) for display only

### Prevention
- Store all server-side timestamps as pure UTC - never add timezone offsets at storage time
- Convert to local timezone only for display in the UI layer
- Always log session duration in console to catch negative durations during development

---

## BUG-013: Lecturer Counted as Participant in Attendance

**Date:** 2026-02-02  
**Severity:** ?? Medium  
**Component:** Attendance Scoring

### Problem
In session recap, the lecturer appeared in the participant list with an attendance score. Additionally, participants showed as "Unknown User" instead of their MatricNo.

### Root Cause
The `CalculateAttendanceScore()` method collected all IDs without checking if the ID belonged to the lecturer. Also attempted unnecessary name lookups.

### Solution

**Fix 1: Exclude Lecturer from Scoring**
Added `allParticipantIds.Remove(session.LecturerId);` after collecting participant IDs.

**Fix 2: Use MatricNo Directly**
Removed `_userDetailsCache` and used `participantId` directly instead of looking up names.

**Files Modified:**
- Services/SessionService.cs - Removed cache, added lecturer exclusion, simplified handling
- Components/Pages/SessionRecap.razor - Simplified participant display

### Prevention
- Always filter out lecturer/admin IDs when calculating participant-only metrics
- Use `session.LecturerId` to identify and exclude the lecturer
- Prefer simple IDs over name lookups for internal processing

---

## BUG-014: MessagingPanel Handlers Not Registered on Initial Load

**Date:** 2026-02-02  
**Severity:** ?? High  
**Component:** MessagingPanel / SignalR Integration

### Problem
Messages wouldn't appear in the messaging panel on initial page load. Refreshing the page made messages suddenly appear.

### Root Cause
SignalR handlers were registered in `OnInitializedAsync()`, which runs before the `HubConnection` parameter is set by the parent component.

### Solution

**Fix: Register Handlers in OnParametersSetAsync**
Moved handler registration to `OnParametersSetAsync()` where the `HubConnection` parameter is guaranteed to be available.

Added connection tracking to detect when parent swaps connections and proper cleanup in `Dispose()`.

**Files Modified:**
- Components/Shared/MessagingPanel.razor - Moved handler registration to OnParametersSetAsync, added IDisposable

### Prevention
- Never register SignalR handlers in OnInitializedAsync when HubConnection is a parameter
- Always use OnParametersSetAsync for parameter-dependent initialization
- Implement IDisposable to clean up handlers on component disposal
- Test messaging immediately after page load

---

## BUG-015: Issue Buttons Not Visible Until Page Refresh

**Date:** 2026-02-02  
**Severity:** ?? Medium  
**Component:** IssueButtons / Component Lifecycle

### Problem
Students couldn't see the "Having Issues?" buttons until after refreshing the page.

### Root Cause
Same lifecycle issue as BUG-014. The component didn't have `OnParametersSetAsync()` to react when the `HubConnection` parameter was set.

### Solution

**Fix: Add OnParametersSetAsync Handler**
Added `OnParametersSetAsync()` to trigger re-render when HubConnection becomes available.

Added visual feedback showing "Connecting..." when HubConnection is null.

**Files Modified:**
- Components/Shared/IssueButtons.razor - Added OnParametersSetAsync, connection state feedback

### Prevention
- Any component depending on a Parameter for initialization must implement OnParametersSetAsync
- Add visual feedback for loading/connecting states
- Track initialization state with a boolean flag

---

## BUG-016: Session Join Input Not Updating in Real-Time

**Date:** 2026-02-02  
**Severity:** ?? Medium  
**Component:** JoinSession / Input Binding

### Problem
When students typed a session code, the "Join Session" button remained disabled until they clicked outside the input field.

### Root Cause
Blazor's `@bind` directive uses the `onchange` event by default, which only fires when the input loses focus (blur), not on every keystroke.

### Solution

**Fix: Real-Time Input Binding**
Added `@bind:event="oninput"` to update the binding on every keystroke instead of waiting for blur.

**Files Modified:**
- Components/Shared/JoinMethodCard.razor - Added `@bind:event="oninput"`

### Prevention
- Use `@bind:event="oninput"` for inputs that control button states
- Use default `@bind` (onchange) for form submissions where validation should occur after field completion
- Test input fields by typing without clicking away

---
