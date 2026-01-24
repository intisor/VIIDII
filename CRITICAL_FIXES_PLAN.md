# Critical Fixes Implementation Plan

## Priority 1: Fix Today (Before Next Test)

### Fix #1: SignalR Reconnection Handlers ?? 15 min
**File:** `Components/Pages/SessionView.razor`
**Location:** After line 377 (in `SetupHubConnectionAsync`)

```csharp
await _hubConnection.StartAsync();
State.IsHubConnected = true;

// ? ADD RECONNECTION HANDLERS
_hubConnection.Reconnecting += async (error) =>
{
    await InvokeAsync(() =>
    {
        Console.WriteLine($"SignalR reconnecting: {error?.Message}");
        State.SetError("Connection lost. Reconnecting...");
        StateHasChanged();
    });
};

_hubConnection.Reconnected += async (connectionId) =>
{
    await InvokeAsync(async () =>
    {
        Console.WriteLine($"SignalR reconnected: {connectionId}");
        State.ClearError();
        
        // Re-join session
        await _hubConnection.SendAsync("JoinSession", SessionId);
        
        // Students must re-send their peer IDs
        if (!State.IsLecturer && State.MyPeerId != null)
        {
            Console.WriteLine($"Re-sending peer ID after reconnection: {State.MyPeerId}");
            await _hubConnection.SendAsync("SendPeerId", SessionId, State.MyPeerId);
        }
        
        StateHasChanged();
    });
};

_hubConnection.Closed += async (error) =>
{
    await InvokeAsync(() =>
    {
        Console.WriteLine($"SignalR connection closed: {error?.Message}");
        State.SetError("Disconnected from session. Please refresh the page.");
        State.IsHubConnected = false;
        StateHasChanged();
    });
};

// Join session
await _hubConnection.SendAsync("JoinSession", SessionId);
```

---

### Fix #2: Wrap All JSInvokable Methods ?? 20 min
**File:** `Components/Pages/SessionView.razor`

Replace all JSInvokable methods following this pattern:

**Before:**
```csharp
[JSInvokable]
public void OnStreamReceived()
{
    Console.WriteLine("Stream received");
    State.IsStreamLoaded = true;
    StateHasChanged();
}
```

**After:**
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

**Apply to these 15 methods:**
1. `OnLecturerPeerReady` (line 905)
2. `OnStudentConnected` (line 918)
3. `OnStudentDisconnected` (line 925)
4. `OnStreamTypeChanged` (line 933)
5. `OnStudentPeerReady` (line 940)
6. `OnStreamReceived` (line 954)
7. `OnStreamLost` (line 965)
8. `OnPeerError` (line 974)
9. `OnConnectedToLecturer` (line 993)
10. `OnConnectionFailed` (line 1001)
11. `OnFileChunkReceived` (line 1014)
12. `OnFileUploadProgress` (line 1020)
13. `OnFileDownloadComplete` (line 1027)
14. `OnFileDownloadError` (line 1033)
15. `OnTabVisibilityChanged` (line 1050)

---

### Fix #3: Memory Leak - Store PeerJS Calls ?? 10 min
**File:** `wwwroot/js/sessionInterop.js`

**Add at module level (line 16):**
```javascript
let activeCalls = new Map(); // peerId -> call object
```

**Update callStudent function (line 401-450):**
```javascript
function callStudent(studentPeerId) {
    if (!isLecturer) {
        console.error("Only lecturer can call students");
        return { success: false, error: "Not lecturer" };
    }

    if (!peer || peer.disconnected) {
        console.error("Lecturer peer not initialized");
        return { success: false, error: "Peer not initialized" };
    }

    if (!localStream) {
        console.error("No local stream available");
        return { success: false, error: "No local stream" };
    }

    console.log(`Calling student: ${studentPeerId}`);

    try {
        const call = peer.call(studentPeerId, localStream);

        if (!call) {
            console.error("Failed to create call");
            return { success: false, error: "Failed to create call" };
        }

        // ? Store the call
        activeCalls.set(studentPeerId, call);

        call.on("stream", (remoteStream) => {
            console.log(`Call established with student ${studentPeerId}`);
        });

        call.on("close", () => {
            console.log(`Call to student ${studentPeerId} closed`);
            // ? Remove from active calls
            activeCalls.delete(studentPeerId);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnStudentDisconnected', studentPeerId);
            }
        });

        call.on("error", (err) => {
            console.error(`Call error with student ${studentPeerId}:`, err);
            // ? Remove from active calls on error
            activeCalls.delete(studentPeerId);
        });

        console.log(`Call initiated to student ${studentPeerId}`);
        return { success: true, peerId: studentPeerId };

    } catch (err) {
        console.error(`Exception calling student ${studentPeerId}:`, err);
        return { success: false, error: err.message };
    }
}
```

**Update cleanup function (line 654):**
```javascript
function cleanup() {
    console.log("Cleaning up session resources");

    // ? Close all active calls
    activeCalls.forEach((call, peerId) => {
        try {
            console.log(`Closing call to ${peerId}`);
            call.close();
        } catch (e) {
            console.error(`Error closing call to ${peerId}:`, e);
        }
    });
    activeCalls.clear();

    // Stop local stream
    if (localStream) {
        localStream.getTracks().forEach(track => track.stop());
        localStream = null;
    }

    // ... rest of cleanup
}
```

---

### Fix #4: Race Condition in OnAfterRenderAsync ?? 5 min
**File:** `Components/Pages/SessionView.razor`

**Add flag (line 234):**
```csharp
private bool _peerSetupAttempted = false;
```

**Update OnAfterRenderAsync (line 356):**
```csharp
else if (!State.IsLecturer && State.IsSessionStarted && !_isSettingUpPeer && State.MyPeerId == null && !_peerSetupAttempted)
{
    _peerSetupAttempted = true;
    Console.WriteLine("Session started after initial render - setting up peer now");
    await SetupStudentPeerConnectionAsync();
}
```

---

### Fix #5: Graceful Peer Destruction ?? 10 min
**File:** `wwwroot/js/sessionInterop.js`

**Update cleanup function (line 669-673):**
```javascript
// Destroy peer connection
if (peer) {
    try {
        console.log("Destroying peer connection gracefully");
        
        // ? Close all peer connections first
        if (peer.connections) {
            Object.keys(peer.connections).forEach(peerId => {
                const conns = peer.connections[peerId];
                if (Array.isArray(conns)) {
                    conns.forEach(conn => {
                        try {
                            console.log(`Closing connection to ${peerId}`);
                            conn.close();
                        } catch (e) {
                            console.error(`Error closing connection to ${peerId}:`, e);
                        }
                    });
                }
            });
        }
        
        // ? Wait briefly for graceful closure
        await new Promise(resolve => setTimeout(resolve, 100));
        
        // ? Now destroy the peer
        peer.destroy();
        console.log("Peer destroyed successfully");
    } catch (e) {
        console.error("Error destroying peer:", e);
    }
    peer = null;
}
```

---

## Testing Checklist After Fixes

### Test #1: SignalR Reconnection
1. Start session as lecturer
2. Join as student
3. **Disable network** on student side for 5 seconds
4. **Re-enable network**
5. ? Verify: Student sees "Reconnecting..." then reconnects
6. ? Verify: Lecturer still sees student in participant list
7. ? Verify: Video stream resumes

### Test #2: JSInvokable Error Handling
1. Start session
2. Join as student
3. While video is playing, **refresh lecturer page**
4. ? Verify: Student doesn't crash
5. ? Verify: Console shows graceful error handling

### Test #3: Memory Leaks
1. Start session as lecturer
2. Join with 5 students
3. Have all students leave
4. ? Verify: Console shows "Closing call to [peerId]" for each
5. ? Verify: No errors in JavaScript console
6. **Open DevTools > Memory > Take Snapshot**
7. Start new session, repeat 10 times
8. ? Verify: Memory doesn't continuously increase

### Test #4: Race Condition
1. Start session as lecturer
2. **Rapidly** refresh student page 5 times in a row
3. ? Verify: Only ONE peer is created
4. ? Verify: Console shows "Session started after initial render" only once

### Test #5: Graceful Cleanup
1. Start session with students
2. Lecturer clicks "End Session"
3. ? Verify: Console shows peer connections closing
4. ? Verify: Console shows "Peer destroyed successfully"
5. ? Verify: No error messages

---

## Estimated Total Time: 60 minutes

## Commit Message
```
fix: critical stability improvements

- Add SignalR reconnection handlers to restore connections
- Wrap all JSInvokable methods in InvokeAsync + try-catch
- Fix memory leak by tracking and cleaning up PeerJS calls
- Prevent race condition in peer setup with flag
- Improve peer cleanup to gracefully close connections

Fixes #[issue-number]
```

---

## Next Steps (After These Fixes)

Once critical fixes are tested and deployed:

1. **Add timeout to peer setup** (High Priority #7)
2. **Track peerId-to-userId mapping** (High Priority #8)
3. **Clean up test camera on error** (High Priority #6)
4. **Add cancellation token to DisposeAsync** (High Priority #13)
5. **Implement proper error recovery UI**

---

## Deployment Notes

- ? Test locally with Chrome DevTools network throttling
- ? Test on actual mobile devices (iOS + Android)
- ? Monitor console for errors during first hour after deployment
- ? Have rollback plan ready
- ? Document any issues discovered in production

---

## Success Criteria

After implementing these 5 critical fixes:

- ? Sessions survive network interruptions
- ? No crashes from JavaScript callbacks
- ? No memory leaks during long sessions
- ? No duplicate peers created
- ? Clean shutdown with no errors

If all 5 tests pass ?, proceed to high-priority fixes.
If any test fails ?, debug before deploying.
