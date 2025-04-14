namespace FutaMeetWeb.Models
{
    public class User
    {
        public string Name { get; set; }
        public string MatricNo { get; set; }
        public Role Role { get; set; }
        public string Department { get; set; }
    }
    public enum Role { Student, Lecturer, Admin }
    public class Session(string lecturerId, string title)
    {
        public string SessionId { get; } = GenerateSessionCode();
        public string LecturerId { get; } = lecturerId;
        public DateTime StartTime { get; } = DateTime.UtcNow.AddHours(1);
        public DateTime? EndTime { get; set; } = null;
        public string Title { get; set; } = title;
        public SessionStatus Status { get; set; } = SessionStatus.Active;
        public HashSet<string> ParticipantIds { get; } = [];

        private static string GenerateSessionCode() => $"{DateTime.UtcNow.AddHours(1):yyyyMMdd}-{string.Concat(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))))}";
    }
    public enum SessionStatus { Active, Ended, Cancelled}
}