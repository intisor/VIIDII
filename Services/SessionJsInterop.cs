using Microsoft.JSInterop;

namespace VIIDII.Services;

/// <summary>
/// Network status information
/// </summary>
public class NetworkStatus
{
    public string EffectiveType { get; set; } = "unknown";
    public double Downlink { get; set; }
    public double Rtt { get; set; }
    public bool SaveData { get; set; }
    public bool Measured { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Battery status information
/// </summary>
public class BatteryStatus
{
    public int Level { get; set; }
    public bool Charging { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Service for handling JavaScript interop operations related to webcam, screen sharing, and peer connections
/// </summary>
public interface ISessionJsInterop
{
    /// <summary>
    /// Initialize the session interop with context
    /// </summary>
    Task<bool> InitializeAsync<T>(string sessionId, bool isLecturer, DotNetObjectReference<T> dotNetRef) where T : class;

    /// <summary>
    /// Start webcam for lecturer
    /// </summary>
    Task<object> StartWebcamAsync(string sessionId);

    /// <summary>
    /// Start screen sharing for lecturer
    /// </summary>
    Task<object> StartScreenShareAsync();

    /// <summary>
    /// Setup peer connection for student
    /// </summary>
    Task<object> SetupStudentPeerAsync();

    /// <summary>
    /// Connect student to lecturer peer
    /// </summary>
    Task<object> ConnectToLecturerAsync(string lecturerPeerId);

    /// <summary>
    /// Call a student peer (lecturer only)
    /// </summary>
    Task<object> CallStudentAsync(string studentPeerId);

    /// <summary>
    /// Handle stream change notification (when lecturer switches stream type)
    /// </summary>
    Task HandleStreamChangeAsync(string streamType);

    /// <summary>
    /// Send data to all connected peers (for file sharing)
    /// </summary>
    Task<object> SendDataToPeersAsync(object data);

    /// <summary>
    /// Send file to all students in chunks via P2P
    /// </summary>
    Task<object> SendFileToStudentsAsync(object fileStream, string messageId);

    /// <summary>
    /// Cleanup session resources (webcam, peer connections, etc.)
    /// </summary>
    Task CleanupAsync();

    /// <summary>
    /// Check if webcam is initialized
    /// </summary>
    Task<bool> IsWebcamInitializedAsync();

    /// <summary>
    /// Check if device is mobile
    /// </summary>
    Task<bool> IsMobileAsync();

    /// <summary>
    /// Get battery level and charging status
    /// </summary>
    Task<BatteryStatus> GetBatteryLevelAsync();

    /// <summary>
    /// Get network status information
    /// </summary>
    Task<NetworkStatus> GetNetworkStatusAsync();

    /// <summary>
    /// Check if tab is currently visible
    /// </summary>
    Task<bool> IsTabVisibleAsync();

    /// <summary>
    /// Setup tab visibility change listener
    /// </summary>
    Task SetupTabVisibilityListenerAsync();
}

public class SessionJsInterop : ISessionJsInterop
{
    private readonly IJSRuntime _jsRuntime;

    public SessionJsInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<bool> InitializeAsync<T>(string sessionId, bool isLecturer, DotNetObjectReference<T> dotNetRef) where T : class
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("sessionInterop.initialize", sessionId, isLecturer, dotNetRef);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in InitializeAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in InitializeAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> StartWebcamAsync(string sessionId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.startWebcam", sessionId);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in StartWebcamAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in StartWebcamAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> StartScreenShareAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.startScreenShare");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in StartScreenShareAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in StartScreenShareAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> SetupStudentPeerAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.setupStudentPeer");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in SetupStudentPeerAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SetupStudentPeerAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> ConnectToLecturerAsync(string lecturerPeerId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.connectToLecturer", lecturerPeerId);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in ConnectToLecturerAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ConnectToLecturerAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> CallStudentAsync(string studentPeerId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.callStudent", studentPeerId);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in CallStudentAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in CallStudentAsync: {ex.Message}");
            throw;
        }
    }

    public async Task HandleStreamChangeAsync(string streamType)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("sessionInterop.handleStreamChange", streamType);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in HandleStreamChangeAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in HandleStreamChangeAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> SendDataToPeersAsync(object data)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.sendDataToPeers", data);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in SendDataToPeersAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SendDataToPeersAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<object> SendFileToStudentsAsync(object fileStream, string messageId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<object>("sessionInterop.sendFileToStudents", fileStream, messageId);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in SendFileToStudentsAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SendFileToStudentsAsync: {ex.Message}");
            throw;
        }
    }

    public async Task CleanupAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("sessionInterop.cleanup");
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected, cleanup already happened
            Console.WriteLine("JS runtime disconnected during cleanup");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in CleanupAsync: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in CleanupAsync: {ex.Message}");
        }
    }

    public async Task<bool> IsWebcamInitializedAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("sessionInterop.isWebcamInitialized");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in IsWebcamInitializedAsync: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in IsWebcamInitializedAsync: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsMobileAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("sessionInterop.isMobile");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in IsMobileAsync: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in IsMobileAsync: {ex.Message}");
            return false;
        }
    }

    public async Task<BatteryStatus> GetBatteryLevelAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<BatteryStatus>("sessionInterop.getBatteryLevel");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in GetBatteryLevelAsync: {ex.Message}");
            return new BatteryStatus { Level = -1, Error = ex.Message };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetBatteryLevelAsync: {ex.Message}");
            return new BatteryStatus { Level = -1, Error = ex.Message };
        }
    }

    public async Task<NetworkStatus> GetNetworkStatusAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<NetworkStatus>("sessionInterop.getNetworkStatus");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in GetNetworkStatusAsync: {ex.Message}");
            return new NetworkStatus { EffectiveType = "unknown", Error = ex.Message };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetNetworkStatusAsync: {ex.Message}");
            return new NetworkStatus { EffectiveType = "unknown", Error = ex.Message };
        }
    }

    public async Task<bool> IsTabVisibleAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("sessionInterop.isTabVisible");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in IsTabVisibleAsync: {ex.Message}");
            return true; // Default to visible on error
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in IsTabVisibleAsync: {ex.Message}");
            return true; // Default to visible on error
        }
    }

    public async Task SetupTabVisibilityListenerAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("sessionInterop.setupTabVisibilityListener");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JS Error in SetupTabVisibilityListenerAsync: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SetupTabVisibilityListenerAsync: {ex.Message}");
            throw;
        }
    }
}

