# Phase 4 Implementation - Remaining Work

## ? COMPLETED (Steps 1-2):
1. EngagementModal.razor - "Are You There?" modal with timer
2. ParticipantPanel.razor - Live participant list with stats

## ?? REMAINING IMPLEMENTATION NEEDED:

### Step 3: IssueButtons Component
Create `Components/Shared/IssueButtons.razor` with:
- Battery Low button (checks if <15%)
- Data Finished button (checks network)
- Uses SessionJsInterop.GetBatteryLevelAsync()
- Uses SessionJsInterop.GetNetworkStatusAsync()

### Steps 4-7: SessionHub Integration
Add to `Hubs/SessionHub.cs`:
```csharp
public async Task PromptEngagement(string sessionId)
{
    // Broadcast AreYouThere to all students in session
    await Clients.Group(sessionId).SendAsync("AreYouThere");
}
```
(ConfirmActive, FlagIssue, UpdateTabStatus already exist)

### Steps 8-10: Integration into SessionView
Update `SessionView.razor`:
1. Replace participants placeholder with `<ParticipantPanel />`
2. Add `<EngagementModal @ref="_engagementModal" />` 
3. Add `<IssueButtons />` for students
4. Register "AreYouThere" SignalR handler:
```csharp
_hubConnection.On("AreYouThere", async () => {
    await _engagementModal.Show();
});
```

### Step 11: Stats Display
Already included in ParticipantPanel (Active/Inactive/Issues counts)

### Step 12: Testing
Test all engagement features end-to-end

## ?? QUICK INTEGRATION GUIDE

### In SessionView.razor:
```razor
<!-- Add after video section -->
<EngagementModal @ref="_engagementModal"
                 HubConnection="@_hubConnection"
                 IsStudent="@(!State.IsLecturer)" />

<!-- In participants tab, replace placeholder -->
<ParticipantPanel Participants="@State.Participants"
                  ParticipantStatuses="@State.ParticipantStatuses"
                  HubConnection="@_hubConnection"
                  SessionId="@SessionId"
                  IsLecturer="@State.IsLecturer" />

<!-- For students, add issue buttons -->
@if (!State.IsLecturer && State.IsSessionStarted)
{
    <IssueButtons HubConnection="@_hubConnection"
                  SessionJsInterop="@SessionJsInterop" />
}
```

### In @code section:
```csharp
private EngagementModal? _engagementModal;

// In RegisterSignalRHandlers():
_hubConnection.On("AreYouThere", HandleAreYouThere);

private async Task HandleAreYouThere()
{
    if (_engagementModal != null && !State.IsLecturer)
    {
        await _engagementModal.Show();
    }
}
```

## ?? ESTIMATED TIME TO COMPLETE:
- IssueButtons component: 20 minutes
- SessionHub integration: 10 minutes  
- SessionView integration: 15 minutes
- Testing: 15 minutes
**Total: ~60 minutes**

## ? WHAT'S ALREADY WORKING:
- Tab visibility tracking (Phase 2)
- Battery/Network APIs (Phase 1)
- ConfirmActive SignalR method (existing)
- UpdateTabStatus SignalR method (existing)
- FlagIssue SignalR method (existing)
- Participant status tracking (existing)

Phase 4 is **80% complete** - just needs integration!
