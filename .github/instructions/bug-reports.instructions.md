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



