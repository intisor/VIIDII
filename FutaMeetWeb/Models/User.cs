namespace FutaMeetWeb.Models
{
    public class User
    {
        public string Name { get; set; }
        public string MatricNo { get; set; }
        public string Password { get; set; }
        public Role Role { get; set; }
        public Departments? Department { get; set; } 
        public Levels? Level { get; set; } 

        public enum Departments
        {
            ComputerScience,
            ElectricalEngineering,
            MechanicalEngineering,
            SoftwareEngineering,
            MiningEngineering,
            Architecture,
        }

        public enum Levels
        {
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
        public Session(string lecturerId)
        {
            LecturerId = lecturerId;
        }
        public string LecturerId { get; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow.AddHours(1);
        public DateTime? EndTime { get; set; } = null;
        public string Title { get; set; }
        public List<User.Departments> AllowedDepartments { get; set; }
        public List<User.Levels> AllowedLevels { get; set; }
        public string LecturerConnectionId { get; set; }
        public SessionStatus Status { get; set; } = SessionStatus.Active;
        public bool IsSessionStarted { get; set; } = false;
        public HashSet<string> ParticipantIds { get; } = new HashSet<string>(); // Fixed initialization
        public Dictionary<string, StudentStatus> ParticipantStatuses { get; } = new Dictionary<string, StudentStatus>(); // Fixed initialization
        public Dictionary<string, List<(StudentStatus status, DateTime TimeStamp)>> ParticipantEvents { get; } = new Dictionary<string, List<(StudentStatus status, DateTime TimeStamp)>>(); // Fixed initialization
        public Dictionary<string, string> ParticipantConnectionIds { get; } = new Dictionary<string, string>(); // Fixed initialization

        public enum StudentStatus { Active, InActive, BatteryLow, DataFinished, Disconnected }

        private static string GenerateSessionCode() => $"{DateTime.UtcNow.AddHours(1):yyyyMMdd}-{string.Concat(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))))}";
    }
    public enum SessionStatus { Active, Started, Ended, Cancelled }
}