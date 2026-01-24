using VIIDII.Models;

namespace VIIDII.Models;

/// <summary>
/// Manages all state for a session view (SOLID - Single Responsibility)
/// </summary>
public class SessionState
{
    // Session Information
    public string SessionId { get; set; } = string.Empty;
    public string SessionTitle { get; set; } = string.Empty;
    public SessionStatus Status { get; set; }
    public bool IsSessionStarted { get; set; }
    public DateTime? StartTime { get; set; }

    // User State
    public User? CurrentUser { get; set; }
    public bool IsLecturer { get; set; }
    public string? MyPeerId { get; set; }

    // Video State
    public bool IsWebcamActive { get; set; }
    public bool IsScreenSharing { get; set; }
    public bool IsStreamLoaded { get; set; }
    public bool IsMobileDevice { get; set; }
    public bool IsTestingCamera { get; set; }
    public string CurrentStreamType { get; set; } = "webcam"; // "webcam" or "screenshare"

    // Peer Management
    public string? LecturerPeerId { get; set; } // For students
    public List<string> StudentPeerIds { get; set; } = new();
    public Dictionary<string, string> ConnectedStudents { get; set; } = new(); // PeerId -> Name

    // Participant Management (for lecturer)
    public Dictionary<string, string> Participants { get; set; } = new(); // MatricNo -> Name
    public Dictionary<string, Session.StudentStatus> ParticipantStatuses { get; set; } = new();
    public int ParticipantCount => Participants.Count;

    // UI State
    public bool IsLoading { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ShowMobilePlayOverlay { get; set; }
    public bool ShowTestCameraSection { get; set; }
    public string ActiveTab { get; set; } = "messages"; // "messages" or "participants"
    public bool IsTabExpanded { get; set; }

    // Connection State
    public bool IsHubConnected { get; set; }
    public bool IsPeerConnected { get; set; }
    public int ConnectionAttempts { get; set; }
    public DateTime? LastConnectionAttempt { get; set; }

    // Browser Persistence
    public bool IsRestoredFromStorage { get; set; }

    // Methods
    public void Reset()
    {
        IsWebcamActive = false;
        IsScreenSharing = false;
        IsStreamLoaded = false;
        MyPeerId = null;
        LecturerPeerId = null;
        StudentPeerIds.Clear();
        ConnectedStudents.Clear();
        Participants.Clear();
        ParticipantStatuses.Clear();
        ErrorMessage = null;
        IsLoading = false;
        IsHubConnected = false;
        IsPeerConnected = false;
        ConnectionAttempts = 0;
    }

    public void SetError(string message)
    {
        ErrorMessage = message;
        IsLoading = false;
    }

    public void ClearError()
    {
        ErrorMessage = null;
    }

    public void StartLoading(string? message = null)
    {
        IsLoading = true;
        if (message != null)
        {
            ErrorMessage = message;
        }
    }

    public void StopLoading()
    {
        IsLoading = false;
    }

    public void AddParticipant(string matricNo, string name)
    {
        if (!Participants.ContainsKey(matricNo))
        {
            Participants[matricNo] = name;
        }
    }

    public void RemoveParticipant(string matricNo)
    {
        Participants.Remove(matricNo);
        ParticipantStatuses.Remove(matricNo);
    }

    public void UpdateParticipantStatus(string matricNo, Session.StudentStatus status)
    {
        ParticipantStatuses[matricNo] = status;
    }

    public void AddStudentPeer(string peerId, string? name = null)
    {
        if (!StudentPeerIds.Contains(peerId))
        {
            StudentPeerIds.Add(peerId);
            if (name != null)
            {
                ConnectedStudents[peerId] = name;
            }
        }
    }

    public void RemoveStudentPeer(string peerId)
    {
        StudentPeerIds.Remove(peerId);
        ConnectedStudents.Remove(peerId);
    }

    public void ToggleTab()
    {
        ActiveTab = ActiveTab == "messages" ? "participants" : "messages";
    }

    public void SetTab(string tab)
    {
        if (tab == "messages" || tab == "participants")
        {
            ActiveTab = tab;
        }
    }

    public void RecordConnectionAttempt()
    {
        ConnectionAttempts++;
        LastConnectionAttempt = DateTime.UtcNow;
    }

    public bool ShouldRetryConnection()
    {
        // Exponential backoff: 1s, 2s, 4s, 8s, 16s, max 30s
        if (LastConnectionAttempt == null) return true;

        var timeSinceLastAttempt = DateTime.UtcNow - LastConnectionAttempt.Value;
        var backoffDelay = Math.Min(Math.Pow(2, ConnectionAttempts - 1), 30);

        return timeSinceLastAttempt.TotalSeconds >= backoffDelay;
    }

    public int MaxConnectionAttempts => 10; // After 10 attempts, prompt user to refresh

    public bool HasExceededMaxAttempts => ConnectionAttempts >= MaxConnectionAttempts;
}
