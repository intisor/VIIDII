namespace VIIDII.Models
{
    public class User
    {
        public int Id { get; set; }
        public required string MatricNo { get; set; }
        public required string Name { get; set; }
        public required string PasswordHash { get; set; }
        public Role Role { get; set; }
        public Departments? Department { get; set; }
        public Levels? Level { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Session> LecturerSessions { get; set; } = new List<Session>();
        public ICollection<SessionParticipant> SessionParticipants { get; set; } = new List<SessionParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<AttendanceLog> AttendanceLogs { get; set; } = new List<AttendanceLog>();
        public ICollection<FileMetadata> UploadedFiles { get; set; } = new List<FileMetadata>();

        public enum Departments
        {
            Any = 0,
            ComputerScience = 1,
            ElectricalEngineering = 2,
            MechanicalEngineering = 3,
            SoftwareEngineering = 4,
            MiningEngineering = 5,
            Architecture = 6,
        }

        public enum Levels
        {
            Any = 0,
            Level100 = 100,
            Level200 = 200,
            Level300 = 300,
            Level400 = 400,
            Level500 = 500
        }
    }

    public enum Role { Student, Lecturer, Admin }

    public class Session
    {
        public int Id { get; set; }
        public required string SessionId { get; set; }
        public int LecturerId { get; set; }
        public string? LecturerMatricNo { get; set; } // Store for reference without FK constraint
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public required string Title { get; set; }
        public List<User.Departments> AllowedDepartments { get; set; } = new();
        public List<User.Levels> AllowedLevels { get; set; } = new();
        public string LecturerConnectionId { get; set; } = string.Empty;
        public SessionStatus Status { get; set; } = SessionStatus.Active;
        public bool IsSessionStarted { get; set; } = false;

        // In-memory collections (not persisted, for runtime use)
        [System.Text.Json.Serialization.JsonIgnore]
        public HashSet<string> ParticipantIds { get; set; } = new HashSet<string>();
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, StudentStatus> ParticipantStatuses { get; set; } = new Dictionary<string, StudentStatus>();
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, List<(StudentStatus status, DateTime timeStamp)>> ParticipantEvents { get; set; } = new Dictionary<string, List<(StudentStatus status, DateTime timeStamp)>>();
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, string> ParticipantConnectionIds { get; set; } = new Dictionary<string, string>();

        // Navigation properties (EF Core)
        [System.Text.Json.Serialization.JsonIgnore]
        public User Lecturer { get; set; } = null!;
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<SessionParticipant> Participants { get; set; } = new List<SessionParticipant>();
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<AttendanceLog> AttendanceLogs { get; set; } = new List<AttendanceLog>();
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<FileMetadata> Files { get; set; } = new List<FileMetadata>();

        public enum StudentStatus { Active, InActive, BatteryLow, DataFinished, Disconnected }
    }

    public enum SessionStatus { Active, Started, Ended, Cancelled }

    public class SessionParticipant
    {
        public int SessionId { get; set; }
        public int UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LeftAt { get; set; }

        // Navigation properties
        public Session Session { get; set; } = null!;
        public User User { get; set; } = null!;
    }

    public class Message
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int AuthorId { get; set; }
        public required string Content { get; set; }
        public int? ParentId { get; set; }
        public string? Reaction { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Session Session { get; set; } = null!;
        public User Author { get; set; } = null!;
        public Message? Parent { get; set; }
        public ICollection<Message> Replies { get; set; } = new List<Message>();
    }

    public class AttendanceLog
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int StudentId { get; set; }
        public Session.StudentStatus Status { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Session Session { get; set; } = null!;
        public User Student { get; set; } = null!;
    }

    public class FileMetadata
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public required string FileName { get; set; }
        public long FileSizeBytes { get; set; }
        public required string MimeType { get; set; }
        public int UploadedByUserId { get; set; }
        public string? DataChannelPeerId { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Session Session { get; set; } = null!;
        public User UploadedBy { get; set; } = null!;
    }
}