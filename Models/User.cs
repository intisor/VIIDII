namespace VIIDII.Models
{
    public class User
    {
        public required string Name { get; set; }
        public required string MatricNo { get; set; }
        public required string Password { get; set; }
        public Role Role { get; set; }
        public Departments? Department { get; set; } 
        public Levels? Level { get; set; } 

        public enum Departments
        {
            Any,
            ComputerScience,
            ElectricalEngineering,
            MechanicalEngineering,
            SoftwareEngineering,
            MiningEngineering,
            Architecture,
        }

        public enum Levels
        {
            Any,
            Level100, 
            Level200,
            Level300,
            Level400,
            Level500
        }
    }
    public enum Role { Student, Lecturer, Admin }
    public class Session
    {
        public string SessionId { get; } = GenerateSessionCode();
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public Session(string lecturerId)
        {
            LecturerId = lecturerId;
        }
        public string LecturerId { get; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow; // Pure UTC - no timezone offset
        public DateTime? EndTime { get; set; } = null;
        public required string Title { get; set; }
        public required List<User.Departments> AllowedDepartments { get; set; }
        public required List<User.Levels> AllowedLevels { get; set; }
        public string LecturerConnectionId { get; set; } = string.Empty;
        public SessionStatus Status { get; set; } = SessionStatus.Active;
        public bool IsSessionStarted { get; set; } = false;
        public HashSet<string> ParticipantIds { get; } = new HashSet<string>(); // Fixed initialization
        public Dictionary<string, StudentStatus> ParticipantStatuses { get; } = new Dictionary<string, StudentStatus>(); // Fixed initialization
        public Dictionary<string, List<(StudentStatus status, DateTime timeStamp)>> ParticipantEvents { get; } = new Dictionary<string, List<(StudentStatus status, DateTime timeStamp)>>(); // Fixed initialization
        public Dictionary<string, string> ParticipantConnectionIds { get; } = new Dictionary<string, string>(); // Fixed initialization

        public enum StudentStatus { Active, InActive, BatteryLow, DataFinished, Disconnected }

        private static string GenerateSessionCode() => $"{DateTime.UtcNow:yyyyMMdd}-{string.Concat(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))))}";
    }
    public enum SessionStatus { Active, Started, Ended, Cancelled }
}