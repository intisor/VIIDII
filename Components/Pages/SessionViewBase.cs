using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.SignalR.Client;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Components.Pages;

/// <summary>
/// Base class for session view pages (Lecturer and Student)
/// Contains shared state, SignalR connection logic, and lifecycle management
/// </summary>
public abstract class SessionViewBase : ComponentBase, IAsyncDisposable
{
    // Injected Services
    [Inject] protected AuthService AuthService { get; set; } = default!;
    [Inject] protected SessionService SessionService { get; set; } = default!;
    [Inject] protected ISessionJsInterop SessionJsInterop { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;

    // Parameters
    [Parameter] public string SessionId { get; set; } = string.Empty;

    // Shared State
    protected SessionState State = new();
    protected Session? CurrentSession;
    protected HubConnection? _hubConnection;
    protected bool _isDisposed = false;
    protected bool _isNavigating = false;
    protected Action? _backClickHandler;
    private System.Threading.Timer? _keepAliveTimer;
    private System.Threading.Timer? _debounceTimer;

    // Abstract Properties
    protected abstract bool IsLecturer { get; }
    protected abstract string BackNavigationUrl { get; }

    // Lifecycle Methods
    protected override bool ShouldRender()
    {
        if (_isNavigating)
        {
            return false;
        }
        return true;
    }

    protected override async Task OnInitializedAsync()
    {
        Console.WriteLine($"[SessionViewBase] OnInitializedAsync started for SessionId: {SessionId}");
        
        try
        {
            // Load user FIRST
            Console.WriteLine($"[SessionViewBase] Loading current user...");
            State.CurrentUser = await AuthService.GetCurrentUserAsync();
            if (State.CurrentUser == null)
            {
                Console.WriteLine($"[SessionViewBase] No user found, redirecting to login");
                _isNavigating = true;
                Navigation.NavigateTo("/login", forceLoad: true);
                return;
            }
            Console.WriteLine($"[SessionViewBase] User loaded: {State.CurrentUser.Name} ({State.CurrentUser.MatricNo})");

            // Load session
            Console.WriteLine($"[SessionViewBase] Loading session: {SessionId}");
            CurrentSession = SessionService.GetSessionById(SessionId);
            if (CurrentSession == null)
            {
                Console.WriteLine($"[SessionViewBase] Session not found: {SessionId}");
                _isNavigating = true;
                Navigation.NavigateTo("/dashboard", forceLoad: true);
                return;
            }
            Console.WriteLine($"[SessionViewBase] Session loaded: {CurrentSession.Title}");

            // Verify user role matches page type
            bool actualIsLecturer = CurrentSession.LecturerId == State.CurrentUser.MatricNo;
            Console.WriteLine($"[SessionViewBase] Role check - IsLecturer property: {IsLecturer}, Actual: {actualIsLecturer}");
            
            if (actualIsLecturer != IsLecturer)
            {
                // User is on wrong page for their role - redirect
                _isNavigating = true;
                var correctUrl = actualIsLecturer 
                    ? $"/session/lecturer/{SessionId}" 
                    : $"/session/student/{SessionId}";
                Console.WriteLine($"[SessionViewBase] Role mismatch! Redirecting to: {correctUrl}");
                Navigation.NavigateTo(correctUrl, forceLoad: true);
                return;
            }

            // Check for fresh start query parameter
            var uri = new Uri(Navigation.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            bool forceFreshStart = query["fresh"] == "true";

            if (forceFreshStart)
            {
                Console.WriteLine("Fresh start requested - clearing session storage");
                await ClearSessionStorageAsync();
            }
            else
            {
                await TryRestoreSessionStateAsync();
            }

            // Initialize state
            State.SessionId = SessionId;
            State.SessionTitle = CurrentSession.Title;
            State.Status = CurrentSession.Status;
            State.IsSessionStarted = CurrentSession.IsSessionStarted;
            State.IsLecturer = IsLecturer;
            Console.WriteLine($"[SessionViewBase] State initialized - IsSessionStarted: {State.IsSessionStarted}");

            // Initialize back click handler
            _backClickHandler = () => Navigation.NavigateTo(BackNavigationUrl);

            // Role-specific initialization
            Console.WriteLine($"[SessionViewBase] Calling OnRoleSpecificInitializeAsync...");
            await OnRoleSpecificInitializeAsync();

            // Note: SignalR connection setup moved to OnAfterRenderAsync 
            // to ensure AuthService is initialized first (requires JS interop)
            
            Console.WriteLine($"[SessionViewBase] OnInitializedAsync completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionViewBase] Critical error in OnInitializedAsync: {ex.Message}");
            Console.WriteLine($"[SessionViewBase] Stack trace: {ex.StackTrace}");
            State.SetError($"Failed to load session: {ex.Message}");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                Console.WriteLine($"[SessionViewBase] OnAfterRenderAsync started");
                
                // Ensure AuthService is initialized (requires JS interop)
                await AuthService.InitializeAsync();
                Console.WriteLine($"[SessionViewBase] AuthService initialized");
                
                State.IsMobileDevice = await SessionJsInterop.IsMobileAsync();
                
                // Setup SignalR Hub Connection (now that AuthService is ready)
                Console.WriteLine($"[SessionViewBase] Setting up SignalR hub connection...");
                await SetupHubConnectionAsync();
                
                // Save initial state
                await SaveSessionStateAsync();
                
                // Role-specific after render logic (includes JS interop initialization)
                await OnRoleSpecificAfterRenderAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnAfterRenderAsync: {ex.Message}");
            }
        }
    }

    // SignalR Setup
    protected async Task SetupHubConnectionAsync()
    {
        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/sessionHub"))
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();

            // Setup reconnection handlers
            _hubConnection.Reconnecting += OnReconnecting;
            _hubConnection.Reconnected += OnReconnected;
            _hubConnection.Closed += OnConnectionClosed;

            // Shared event handlers
            RegisterSharedHubHandlers();
            
            // Role-specific event handlers
            RegisterRoleSpecificHubHandlers();

            await _hubConnection.StartAsync();
            Console.WriteLine($"SignalR connected for {(IsLecturer ? "Lecturer" : "Student")}");

            // Get MatricNo from current user and pass it to hub
            var matricNo = State.CurrentUser?.MatricNo;
            if (string.IsNullOrEmpty(matricNo))
            {
                Console.WriteLine($"[SessionViewBase] Warning: No MatricNo available for SignalR");
                State.SetError("Authentication error. Please refresh the page.");
                return;
            }

            // Join session with explicit MatricNo
            await _hubConnection.SendAsync("StartSession", SessionId, matricNo);
            Console.WriteLine($"[SessionViewBase] Sent StartSession with MatricNo: {matricNo}");

            // CRITICAL: Trigger re-render to notify child components (MessagingPanel) that HubConnection is now available
            await InvokeAsync(StateHasChanged);

            // Start keep-alive timer to prevent circuit timeout
            StartKeepAliveTimer();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SignalR connection error: {ex.Message}");
            State.SetError("Failed to connect to session. Please refresh the page.");
        }
    }

    private void StartKeepAliveTimer()
    {
        // Send a keep-alive ping every 30 seconds to prevent circuit timeout
        // This runs on a background thread, but we check tab visibility to avoid
        // issues when browser throttles background tabs
        _keepAliveTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected && !_isDisposed)
                {
                    // Check if tab is visible before sending keep-alive
                    bool isVisible = await IsTabVisibleAsync();
                    
                    // Always send keep-alive, but log if tab is hidden
                    await _hubConnection.SendAsync("KeepAlive");
                    
                    if (isVisible)
                    {
                        Console.WriteLine($"[KeepAlive-{(IsLecturer ? "Lecturer" : "Student")}] Ping sent at {DateTime.Now:HH:mm:ss}");
                    }
                    else
                    {
                        Console.WriteLine($"[KeepAlive-{(IsLecturer ? "Lecturer" : "Student")}] Ping sent while tab hidden at {DateTime.Now:HH:mm:ss}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KeepAlive-{(IsLecturer ? "Lecturer" : "Student")}] Error sending ping: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        Console.WriteLine($"[KeepAlive-{(IsLecturer ? "Lecturer" : "Student")}] Timer started - will ping every 30 seconds");
    }

    private async Task<bool> IsTabVisibleAsync()
    {
        try
        {
            return await SessionJsInterop.IsTabVisibleAsync();
        }
        catch
        {
            return true; // Assume visible on error
        }
    }

    // Reconnection handlers
    private Task OnReconnecting(Exception? exception)
    {
        Console.WriteLine($"[{(IsLecturer ? "Lecturer" : "Student")}] SignalR reconnecting... Reason: {exception?.Message}");
        UpdateUI(() =>
        {
            State.IsHubConnected = false;
            State.ConnectionStatus = SessionState.ConnectionStatusType.Reconnecting;
            State.ConnectionMessage = "Reconnecting...";
        });
        return Task.CompletedTask;
    }

    private async Task OnReconnected(string? connectionId)
    {
        Console.WriteLine($"[{(IsLecturer ? "Lecturer" : "Student")}] SignalR reconnected! New connection ID: {connectionId}");
        
        // Rejoin session after reconnection
        var matricNo = State.CurrentUser?.MatricNo;
        if (!string.IsNullOrEmpty(matricNo))
        {
            try
            {
                await _hubConnection!.SendAsync("StartSession", SessionId, matricNo);
                Console.WriteLine($"[{(IsLecturer ? "Lecturer" : "Student")}] Rejoined session after reconnection");
                
                UpdateUI(() =>
                {
                    State.IsHubConnected = true;
                    State.ConnectionStatus = SessionState.ConnectionStatusType.Reconnected;
                    State.ConnectionMessage = "Reconnected successfully";
                    State.ClearError();
                });

                // Auto-dismiss banner after 3 seconds
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    UpdateUI(() =>
                    {
                        State.ConnectionStatus = SessionState.ConnectionStatusType.Connected;
                        State.ConnectionMessage = string.Empty;
                    });
                });

                // Lecturer-specific reconnection handling
                if (IsLecturer)
                {
                    // The hub will send current scores/statuses automatically
                    // Just need to ensure peer connections are reestablished if needed
                    await OnRoleSpecificReconnected();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rejoining session after reconnection: {ex.Message}");
                
                UpdateUI(() =>
                {
                    State.ConnectionStatus = SessionState.ConnectionStatusType.Disconnected;
                    
                    // Different error messages for lecturer vs student
                    if (IsLecturer)
                    {
                        State.ConnectionMessage = "Reconnection issue - Students still in session";
                        State.SetError("Reconnected but failed to rejoin. Students can still see your stream. Click 'End Session' if needed.");
                    }
                    else
                    {
                        State.ConnectionMessage = "Reconnection failed";
                        State.SetError("Reconnected but failed to rejoin session. Please refresh.");
                    }
                });
            }
        }
    }

    private Task OnConnectionClosed(Exception? exception)
    {
        Console.WriteLine($"[{(IsLecturer ? "Lecturer" : "Student")}] SignalR connection closed. Reason: {exception?.Message}");
        UpdateUI(() =>
        {
            State.IsHubConnected = false;
            State.ConnectionStatus = SessionState.ConnectionStatusType.Disconnected;
            State.ConnectionMessage = "Connection lost";
            State.SetError("Connection lost. Please refresh the page.");
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Helper method to update UI without stepping into framework code during debugging
    /// </summary>
    [System.Diagnostics.DebuggerStepThrough]
    protected void UpdateUI(Action updateAction)
    {
        updateAction();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Debounced UI update - prevents excessive re-renders when multiple updates occur in quick succession
    /// </summary>
    [System.Diagnostics.DebuggerStepThrough]
    protected void UpdateUIDebounced(Action updateAction, int delayMs = 500)
    {
        // Cancel any pending update
        _debounceTimer?.Dispose();
        
        // Apply the update to state immediately (so data is fresh)
        updateAction();
        
        // Debounce the StateHasChanged call
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            InvokeAsync(StateHasChanged);
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }, null, delayMs, Timeout.Infinite);
    }

    protected virtual void RegisterSharedHubHandlers()
    {
        if (_hubConnection == null) return;

        _hubConnection.On<Dictionary<string, string>>("ReceiveParticipants", participants =>
        {
            UpdateUI(() => State.Participants = participants);
        });

        _hubConnection.On<Dictionary<string, Session.StudentStatus>>("ReceiveParticipantStatuses", statuses =>
        {
            // Use debounced update to prevent excessive re-renders when multiple students
            // become inactive simultaneously (e.g., after not responding to "Are You There?")
            UpdateUIDebounced(() =>
            {
                State.ParticipantStatuses = statuses;
                Console.WriteLine($"Received participant statuses update: {statuses.Count} participants");
            }, delayMs: 300); // 300ms debounce - batches rapid updates together
        });

        _hubConnection.On<string>("SessionEnded", async sessionId =>
        {
            Console.WriteLine($"Session ended: {sessionId}");
            await ClearSessionStorageAsync();
            _isNavigating = true;
            Navigation.NavigateTo("/dashboard", forceLoad: true);
        });

        _hubConnection.On<string, string>("Error", (message, details) =>
        {
            UpdateUI(() =>
            {
                Console.WriteLine($"Hub error: {message} - {details}");
                State.SetError(message);
            });
        });
    }

    // State Management
    protected async Task SaveSessionStateAsync()
    {
        try
        {
            var stateJson = System.Text.Json.JsonSerializer.Serialize(State);
            await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", $"sessionState_{SessionId}", stateJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving session state: {ex.Message}");
        }
    }

    protected async Task TryRestoreSessionStateAsync()
    {
        try
        {
            var stateJson = await JSRuntime.InvokeAsync<string>("sessionStorage.getItem", $"sessionState_{SessionId}");
            if (!string.IsNullOrEmpty(stateJson))
            {
                var restoredState = System.Text.Json.JsonSerializer.Deserialize<SessionState>(stateJson);
                if (restoredState != null)
                {
                    State = restoredState;
                    Console.WriteLine("Session state restored from storage");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error restoring session state: {ex.Message}");
        }
    }

    protected async Task ClearSessionStorageAsync()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", $"sessionState_{SessionId}");
            await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", $"peerId_{SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing session storage: {ex.Message}");
        }
    }

    protected void RefreshPage()
    {
        Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
    }

    // Abstract Methods - Must be implemented by derived classes
    protected abstract Task OnRoleSpecificInitializeAsync();
    protected abstract Task OnRoleSpecificAfterRenderAsync();
    protected abstract void RegisterRoleSpecificHubHandlers();
    protected virtual Task OnRoleSpecificReconnected() => Task.CompletedTask;

    // Cleanup
    public virtual async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            // Stop timers
            _keepAliveTimer?.Dispose();
            _debounceTimer?.Dispose();
            Console.WriteLine("[KeepAlive] Timers stopped");

            await SaveSessionStateAsync();

            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }

            // Derived classes handle their own DotNetObjectReference disposal
            await OnRoleSpecificDisposeAsync();

            await SessionJsInterop.CleanupAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during dispose: {ex.Message}");
        }

        GC.SuppressFinalize(this);
    }

    // Abstract method for derived class cleanup
    protected virtual Task OnRoleSpecificDisposeAsync() => Task.CompletedTask;
}
