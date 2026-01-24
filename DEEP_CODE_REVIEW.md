# Deep Code Review - SessionView System

## Executive Summary
Overall code quality: **GOOD** with several **CRITICAL** and **HIGH** priority issues that need addressing.

### Critical Issues Found: 5
### High Priority Issues: 8  
### Medium Priority Issues: 12
### Low Priority/Improvements: 10

---

## ?? CRITICAL ISSUES (Must Fix Immediately)

### 1. **SignalR Reconnection Not Handled**
**Location:** `Components/Pages/SessionView.razor` line 370-390

**Issue:** When SignalR reconnects after a network interruption:
- Event handlers are NOT re-registered
- Student peer IDs are NOT re-sent to lecturer
- Lecturer loses track of connected students
- Video streams break

**Current Code:**
```csharp
_hubConnection = new HubConnectionBuilder()
    .WithUrl(Navigation.ToAbsoluteUri("/sessionHub"))
    .WithAutomaticReconnect()  // ?? Enabled but no handlers!
    .Build();

RegisterSignalRHandlers();  // Only called once
```

**Missing:**
```csharp
_hubConnection.Reconnecting += async (error) =>
{
    await InvokeAsync(() =>
    {
        State.SetError("Connection lost. Reconnecting...");
        StateHasChanged();
    });
};

_hubConnection.Reconnected += async (connectionId) =>
{
    await InvokeAsync(async () =>
    {
        State.ClearError();
        Console.WriteLine($"Reconnected: {connectionId}");
        
        // Re-join session
        await _hubConnection.SendAsync("JoinSession", SessionId);
        
        // Students must re-send their peer IDs
        if (!State.IsLecturer && State.MyPeerId != null)
        {
            await _hubConnection.SendAsync("SendPeerId", SessionId, State.MyPeerId);
        }
        
        StateHasChanged();
    });
};

_hubConnection.Closed += async (error) =>
{
    await InvokeAsync(() =>
    {
        State.SetError("Disconnected from session. Please refresh.");
        StateHasChanged();
    });
};
```

**Impact:** Students become invisible to lecturer after network hiccup. Video stream fails.

---

### 2. **Race Condition in OnAfterRenderAsync**
**Location:** `Components/Pages/SessionView.razor` line 356

**Issue:** Multiple rapid renders can cause `SetupStudentPeerConnectionAsync()` to be called multiple times.

**Current Code:**
```csharp
else if (!State.IsLecturer && State.IsSessionStarted && !_isSettingUpPeer && State.MyPeerId == null)
{
    // This can trigger on EVERY render until MyPeerId is set
    await SetupStudentPeerConnectionAsync();
}
```

**Problem:** 
- Renders can happen faster than peer setup completes
- `State.MyPeerId` is only set in the JavaScript callback
- Multiple peer objects could be created

**Fix:**
```csharp
else if (!State.IsLecturer && State.IsSessionStarted && !_isSettingUpPeer && State.MyPeerId == null && !_peerSetupAttempted)
{
    _peerSetupAttempted = true;  // Add this flag
    await SetupStudentPeerConnectionAsync();
}
```

---

### 3. **Memory Leak in PeerJS Calls**
**Location:** `wwwroot/js/sessionInterop.js` line 401-450

**Issue:** When lecturer calls students, call objects are created but never stored or cleaned up.

**Current Code:**
```javascript
function callStudent(studentPeerId) {
    const call = peer.call(studentPeerId, localStream);
    
    call.on("stream", (remoteStream) => { /* ... */ });
    call.on("close", () => { /* ... */ });
    call.on("error", (err) => { /* ... */ });
    
    // ? Call object is created but never stored
    // ? No way to close these calls during cleanup
    return { success: true, peerId: studentPeerId };
}
```

**Fix:**
```javascript
// Add at module level
let activeCalls = new Map(); // peerId -> call object

function callStudent(studentPeerId) {
    const call = peer.call(studentPeerId, localStream);
    
    // ? Store the call
    activeCalls.set(studentPeerId, call);
    
    call.on("close", () => {
        console.log(`Call to student ${studentPeerId} closed`);
        activeCalls.delete(studentPeerId);  // ? Clean up
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnStudentDisconnected', studentPeerId);
        }
    });
    
    // ... rest of handlers
}

// In cleanup():
function cleanup() {
    // ? Close all active calls
    activeCalls.forEach((call, peerId) => {
        console.log(`Closing call to ${peerId}`);
        call.close();
    });
    activeCalls.clear();
    
    // ... rest of cleanup
}
```

---

### 4. **Unhandled Exceptions in JSInvokable Methods**
**Location:** `Components/Pages/SessionView.razor` lines 905-1070

**Issue:** All `[JSInvokable]` methods lack try-catch blocks.

**Current Code:**
```csharp
[JSInvokable]
public void OnStreamReceived()
{
    Console.WriteLine("Stream received");
    State.IsStreamLoaded = true;
    State.IsPeerConnected = true;
    State.ShowMobilePlayOverlay = State.IsMobileDevice;
    State.StopLoading();
    StateHasChanged();  // ? Can throw if component is disposed
}
```

**Fix:**
```csharp
[JSInvokable]
public async Task OnStreamReceived()
{
    if (_isDisposed) return;
    
    try
    {
        await InvokeAsync(() =>
        {
            Console.WriteLine("Stream received");
            State.IsStreamLoaded = true;
            State.IsPeerConnected = true;
            State.ShowMobilePlayOverlay = State.IsMobileDevice;
            State.StopLoading();
            StateHasChanged();
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in OnStreamReceived: {ex.Message}");
    }
}
```

**Apply to all JSInvokable methods:**
- `OnLecturerPeerReady`
- `OnStudentConnected`
- `OnStudentDisconnected`
- `OnStreamTypeChanged`
- `OnStudentPeerReady`
- `OnStreamReceived`
- `OnStreamLost`
- `OnPeerError`
- `OnConnectedToLecturer`
- `OnConnectionFailed`
- `OnFileChunkReceived`
- `OnFileUploadProgress`
- `OnFileDownloadComplete`
- `OnFileDownloadError`
- `OnTabVisibilityChanged`

---

### 5. **PeerJS Not Handling Peer Destruction Properly**
**Location:** `wwwroot/js/sessionInterop.js` line 669-673

**Issue:** Peer might have active connections when destroyed.

**Current Code:**
```javascript
// Destroy peer connection
if (peer) {
    peer.destroy();  // ? Abrupt destruction
    peer = null;
}
```

**Fix:**
```javascript
// Destroy peer connection
if (peer) {
    try {
        // Close all connections gracefully
        if (peer.connections) {
            Object.keys(peer.connections).forEach(peerId => {
                const conns = peer.connections[peerId];
                conns.forEach(conn => {
                    try {
                        conn.close();
                    } catch (e) {
                        console.error(`Error closing connection to ${peerId}:`, e);
                    }
                });
            });
        }
        
        // Give connections time to close
        await new Promise(resolve => setTimeout(resolve, 100));
        
        peer.destroy();
    } catch (e) {
        console.error("Error destroying peer:", e);
    }
    peer = null;
}
```

---

## ?? HIGH PRIORITY ISSUES

### 6. **TestCamera Stream Not Cleaned Up on Error**
**Location:** `Components/Pages/SessionView.razor` line 421-465

**Current:**
```csharp
private async Task TestCamera()
{
    try {
        // ...
        await JSRuntime.InvokeAsync<object>("eval", @"...");
        State.StopLoading();
    }
    catch (Exception ex)
    {
        State.SetError($"Camera test failed: {ex.Message}");
        State.IsTestingCamera = false;  // ? Stream still running!
    }
}
```

**Fix:**
```csharp
catch (Exception ex)
{
    State.SetError($"Camera test failed: {ex.Message}");
    
    // ? Clean up test stream
    await JSRuntime.InvokeVoidAsync("eval", @"
        if (window.testStream) {
            window.testStream.getTracks().forEach(track => track.stop());
            delete window.testStream;
        }
    ");
    
    State.IsTestingCamera = false;
}
```

---

### 7. **No Timeout on SetupStudentPeerConnectionAsync**
**Location:** `Components/Pages/SessionView.razor` line 647-684

**Issue:** If peer setup hangs, student waits forever (or until max retries).

**Fix:**
```csharp
private async Task SetupStudentPeerConnectionAsync()
{
    if (State.IsLecturer || _isSettingUpPeer) return;

    try
    {
        _isSettingUpPeer = true;
        State.StartLoading("Setting up connection...");

        // ? Add timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        
        var setupTask = SessionJsInterop.SetupStudentPeerAsync();
        var timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);
        
        var completedTask = await Task.WhenAny(setupTask, timeoutTask);
        
        if (completedTask == timeoutTask)
        {
            throw new TimeoutException("Peer setup timed out after 30 seconds");
        }
        
        var result = await setupTask;
        Console.WriteLine("Student peer setup initiated");
        State.StopLoading();
    }
    catch (TimeoutException ex)
    {
        Console.WriteLine($"Peer setup timeout: {ex.Message}");
        State.SetError("Connection timeout. Retrying...");
        State.RecordConnectionAttempt();
        // ... retry logic
    }
    // ... rest of catch blocks
}
```

---

### 8. **Lecturer Doesn't Handle Student Disconnection**
**Location:** `Components/Pages/SessionView.razor` line 905-925

**Current:**
```csharp
[JSInvokable]
public void OnStudentDisconnected(string peerId)
{
    Console.WriteLine($"Student disconnected: {peerId}");
    State.RemoveStudentPeer(peerId);
    StateHasChanged();
}
```

**Issue:** Lecturer doesn't know WHICH student disconnected (only peer ID, not name/matric).

**Fix:** Track peerId-to-userId mapping:
```csharp
// In SessionState.cs
public Dictionary<string, string> PeerIdToUserId { get; set; } = new();

// In OnReceivePeerId:
State.PeerIdToUserId[peerId] = userId;

// In OnStudentDisconnected:
[JSInvokable]
public async Task OnStudentDisconnected(string peerId)
{
    await InvokeAsync(() =>
    {
        if (State.PeerIdToUserId.TryGetValue(peerId, out var userId))
        {
            Console.WriteLine($"Student {userId} (peer: {peerId}) disconnected");
            
            // Optionally notify via SignalR that student lost connection
            if (_hubConnection != null)
            {
                _ = _hubConnection.SendAsync("StudentDisconnected", SessionId, userId);
            }
        }
        
        State.RemoveStudentPeer(peerId);
        State.PeerIdToUserId.Remove(peerId);
        StateHasChanged();
    });
}
```

---

### 9. **Video Element Muted Attribute Race Condition**
**Location:** `wwwroot/js/sessionInterop.js` line 363-376

**Issue:** JavaScript unmutes video, but Blazor re-renders might re-apply `muted="@State.IsLecturer"`.

**Current Flow:**
1. JS: `video.muted = false; video.volume = 1.0;`
2. Blazor calls `StateHasChanged()`
3. Blazor re-renders: `<video muted="@State.IsLecturer">` 
4. Video might mute again

**Fix:** Set a data attribute to prevent re-muting:
```javascript
video.srcObject = remoteStream;
video.muted = false;
video.volume = 1.0;
video.dataset.streamAttached = "true";  // ? Flag
```

In Blazor:
```razor
<video id="sessionVideo" 
       autoplay 
       playsinline 
       muted="@(State.IsLecturer || !State.IsStreamLoaded)"
       controls="@(!State.IsLecturer && State.IsStreamLoaded)"
       class="session-video">
</video>
```

---

### 10. **StartSession Can Be Called While Already Starting**
**Location:** `Components/Pages/SessionView.razor` line 493-552

**Current:**
```csharp
private async Task StartSession()
{
    if (CurrentSession == null || !State.IsLecturer || State.IsLoading || State.IsWebcamActive)
        return;  // ? Good guard
```

**Issue:** Missing check for `State.IsSessionStarted`:
```csharp
if (CurrentSession == null || !State.IsLecturer || State.IsLoading || State.IsWebcamActive || State.IsSessionStarted)
    return;
```

---

### 11. **OnSessionEnded Navigates Without Checking IsNavigating**
**Location:** `Components/Pages/SessionView.razor` line 735-776

**Current:**
```csharp
_isNavigating = true;
if (State.IsLecturer) {
    Navigation.NavigateTo($"/session-recap/{sessionId}", forceLoad: true);
} else {
    Navigation.NavigateTo("/dashboard", forceLoad: true);
}
```

**Issue:** `NavigateTo` is fire-and-forget. Component might render again before navigation completes.

**Fix:**
```csharp
try
{
    _isNavigating = true;
    StateHasChanged();  // ? Show loading screen
    
    await Task.Delay(100);  // Let render complete
    
    if (State.IsLecturer) {
        Navigation.NavigateTo($"/session-recap/{sessionId}", forceLoad: true);
    } else {
        Navigation.NavigateTo("/dashboard", forceLoad: true);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Navigation error: {ex.Message}");
    _isNavigating = false;
}
```

---

### 12. **CallStudent Called Before Lecturer Peer is Fully Ready**
**Location:** `Components/Pages/SessionView.razor` line 778-802

**Current:**
```csharp
await Task.Delay(500); // Wait for student's DOM
await SessionJsInterop.CallStudentAsync(peerId);
```

**Issue:** Doesn't verify lecturer's peer is still active.

**Fix:**
```csharp
// Wait for student DOM
await Task.Delay(500);

// ? Verify lecturer peer is ready
if (!State.IsWebcamActive || State.MyPeerId == null)
{
    Console.WriteLine($"Cannot call student {userId} - lecturer not ready");
    return;
}

var result = await SessionJsInterop.CallStudentAsync(peerId);
```

---

### 13. **DisposeAsync Doesn't Cancel Pending Operations**
**Location:** `Components/Pages/SessionView.razor` line 1201-1260

**Issue:** No cancellation token to stop ongoing operations.

**Add:**
```csharp
private CancellationTokenSource _disposeCts = new();

public async ValueTask DisposeAsync()
{
    if (_isDisposed) return;
    _isDisposed = true;
    
    // ? Cancel all pending operations
    _disposeCts.Cancel();
    
    // ... rest of cleanup
}
```

Use in async operations:
```csharp
await Task.Delay(500, _disposeCts.Token);
```

---

## ?? MEDIUM PRIORITY ISSUES

### 14. **Potential Null Reference in OnStreamChange**
**Location:** Line 804-820

```csharp
if (!State.IsLecturer)
{
    await SessionJsInterop.HandleStreamChangeAsync(streamType);  // ? SessionJsInterop could be null
}
```

**Fix:** Add null check:
```csharp
if (!State.IsLecturer && SessionJsInterop != null)
```

---

### 15. **No Validation on StreamType**
**Location:** Line 804-820

**Current:**
```csharp
State.CurrentStreamType = streamType;  // ? Any string accepted
```

**Fix:**
```csharp
if (streamType != "webcam" && streamType != "screenshare")
{
    Console.WriteLine($"Invalid stream type: {streamType}");
    return;
}
State.CurrentStreamType = streamType;
```

---

### 16. **TestCamera Uses eval() - Security Risk**
**Location:** Line 435-450

**Issue:** Using `eval()` can be dangerous. Use `InvokeVoidAsync` with module.

**Better:**
Create a proper JS function instead of inline eval.

---

### 17. **No Heartbeat for Peer Connections**
**Location:** JavaScript

**Issue:** No way to detect if peer connection is alive but frozen.

**Recommendation:** Add periodic ping/pong:
```javascript
let heartbeatInterval = setInterval(() => {
    if (peer && peer.open) {
        studentConnections.forEach((conn, peerId) => {
            if (conn.open) {
                conn.send({ type: 'ping', timestamp: Date.now() });
            }
        });
    }
}, 30000); // Every 30 seconds
```

---

### 18. **Participant Count Can Be Stale**
**Location:** SessionState.cs line 38

**Issue:** Count is calculated from dictionary which might not be updated in real-time.

**Better:** Update count explicitly when participants change.

---

### 19. **No Maximum Participants Check**
**Location:** SignalR Hub

**Recommendation:** Add max participant limit to prevent resource exhaustion.

---

### 20. **Error Messages Shown Indefinitely**
**Location:** UI

**Issue:** Error banner stays until cleared manually.

**Recommendation:** Auto-dismiss non-critical errors after 10 seconds.

---

### 21. **No Loading Indicator for CallStudent**
**Location:** OnReceivePeerId

**Issue:** 500ms delay with no feedback.

**Recommendation:** Add loading state for "Connecting to student X..."

---

### 22. **connectToLecturer Function Never Called**
**Location:** sessionInterop.js line 403-460

**Issue:** This function exists but is never invoked. Was part of old architecture?

**Recommendation:** Remove dead code or document why it's kept.

---

### 23. **Student Peer Setup Retry Logic Aggressive**
**Location:** Line 668-678

**Issue:** Exponential backoff with 10 attempts might take too long.

**Recommendation:** Reduce to 5 attempts, show "Refresh Page" sooner.

---

### 24. **No Analytics/Telemetry**
**Issue:** No tracking of:
- Connection failures
- Average connection time
- Browser/device info
- Error rates

**Recommendation:** Add telemetry for debugging production issues.

---

### 25. **Session Storage Expiry Logic Inconsistent**
**Location:** Line 1135-1165

**Issue:** Debug: 2 minutes, Production: 30 minutes - too short for long sessions.

**Recommendation:** Don't expire during active sessions. Only expire on app restart.

---

## ?? LOW PRIORITY / IMPROVEMENTS

### 26-35. Code Quality Improvements
- Add XML documentation to public methods
- Use `ConfigureAwait(false)` in library code
- Extract magic numbers to constants (500ms, 30s timeouts, etc.)
- Use `ILogger` instead of `Console.WriteLine`
- Add unit tests for state management
- Add integration tests for peer connections
- Improve variable naming (`_isSettingUpPeer` vs `_peerSetupInProgress`)
- Remove commented code
- Add JSDoc comments to JavaScript functions
- Use TypeScript for better type safety

---

## Summary of Required Actions

### Immediate (Critical - Next Deploy)
1. ? Add SignalR reconnection handlers
2. ? Fix OnAfterRenderAsync race condition
3. ? Store and cleanup PeerJS call objects
4. ? Wrap all JSInvokable methods in try-catch + InvokeAsync
5. ? Improve peer cleanup to close connections gracefully

### Short Term (High Priority - This Sprint)
6. Clean up test camera stream on error
7. Add timeout to peer setup
8. Track peerId-to-userId mapping
9. Fix video muted attribute race
10. Add session started guard to StartSession
11. Improve OnSessionEnded navigation
12. Verify lecturer readiness before calling students
13. Add cancellation token to DisposeAsync

### Medium Term (Next Sprint)
14-25. Address medium priority issues

### Long Term (Backlog)
26-35. Code quality improvements

---

## Performance Recommendations

1. **Lazy load PeerJS library** - Only load when session starts
2. **Debounce StateHasChanged calls** - Batch updates
3. **Use SignalR streaming** for large participant lists
4. **Implement virtual scrolling** for participant list
5. **Compress video** using lower bitrates for mobile

---

## Security Recommendations

1. **Validate all SignalR inputs** - Sanitize user IDs, session IDs
2. **Rate limit SignalR calls** - Prevent spam
3. **Validate peer IDs** - Ensure they match expected format
4. **Remove eval() usage** - Use proper JS modules
5. **Add CSP headers** - Restrict script sources

---

## Testing Recommendations

1. **Test SignalR reconnection** - Kill network, restore
2. **Test peer timeouts** - Block STUN/TURN servers
3. **Test with 50+ students** - Load testing
4. **Test on mobile** - iOS Safari, Android Chrome
5. **Test with slow networks** - Throttle to 3G
6. **Test simultaneous disconnects** - All students leave at once
7. **Test rapid session start/stop** - Lecturer spam-clicks
8. **Test browser refresh** - Session recovery
9. **Test tab visibility** - Switch tabs, minimize browser
10. **Test memory leaks** - Run for hours, check memory

---

## Documentation Needed

1. Architecture diagram (Blazor ? SignalR ? PeerJS flow)
2. State machine diagram (session lifecycle)
3. Error handling guide
4. Deployment checklist
5. Troubleshooting guide for common issues

---

## Final Verdict

**Code Quality: 7/10**

? **Strengths:**
- Good separation of concerns
- Proper use of Blazor lifecycle
- Threading issues fixed with InvokeAsync
- Clean UI state management

? **Weaknesses:**
- Missing SignalR reconnection handling (**CRITICAL**)
- Memory leaks in PeerJS calls (**CRITICAL**)
- Race conditions in peer setup (**CRITICAL**)
- No error handling in JSInvokable methods (**CRITICAL**)
- Lack of timeouts and cancellation

**Recommendation:** Address all 5 critical issues before next production deployment.
