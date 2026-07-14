using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Data
{
    /// <summary>
    /// Extension service that adds persistence capabilities to SessionService
    /// Bridges in-memory session state with database storage
    /// </summary>
    public class SessionPersistenceService
    {
        private readonly SessionRepository _sessionRepository;
        private readonly UserService _userService;

        public SessionPersistenceService(SessionRepository sessionRepository, UserService userService)
        {
            _sessionRepository = sessionRepository;
            _userService = userService;
        }

        /// <summary>
        /// Create session and persist to database
        /// </summary>
        public async Task<Models.Session?> CreateAndPersistSessionAsync(
            string lecturerMatricNo, 
            string title, 
            List<User.Departments> allowedDepartments, 
            List<User.Levels> allowedLevels)
        {
            var lecturer = await _userService.GetUserByMatricNoAsync(lecturerMatricNo);
            if (lecturer == null || lecturer.Role != Role.Lecturer)
                return null;

            var sessionCode = GenerateSessionCode();
            var session = new Models.Session
            {
                SessionId = sessionCode,
                LecturerId = lecturer.Id,
                LecturerMatricNo = lecturerMatricNo,
                Title = title,
                AllowedDepartments = allowedDepartments ?? new List<User.Departments>(),
                AllowedLevels = allowedLevels ?? new List<User.Levels>(),
                Status = SessionStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            // Expand "Any" to include all enums
            if (session.AllowedDepartments.Contains(User.Departments.Any))
                session.AllowedDepartments = Enum.GetValues<User.Departments>().ToList();

            if (session.AllowedLevels.Contains(User.Levels.Any))
                session.AllowedLevels = Enum.GetValues<User.Levels>().ToList();

            return await _sessionRepository.CreateSessionAsync(session);
        }

        /// <summary>
        /// End session and persist state change
        /// </summary>
        public async Task<Models.Session?> EndAndPersistSessionAsync(string sessionId, string lecturerMatricNo)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null || session.LecturerMatricNo != lecturerMatricNo)
                return null;

            session.Status = SessionStatus.Ended;
            session.EndTime = DateTime.UtcNow;
            await _sessionRepository.FinalizeAttendanceLogsAsync(session.Id, session.EndTime.Value);

            return await _sessionRepository.UpdateSessionAsync(session);
        }

        /// <summary>
        /// Start session and persist state change
        /// </summary>
        public async Task<Models.Session?> StartAndPersistSessionAsync(string sessionId)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null || session.Status == SessionStatus.Started)
                return null;

            session.Status = SessionStatus.Started;
            session.StartTime = DateTime.UtcNow;

            return await _sessionRepository.UpdateSessionAsync(session);
        }

        /// <summary>
        /// Add participant to session
        /// </summary>
        public async Task<bool> AddParticipantAsync(string sessionId, string participantMatricNo)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null)
                return false;

            var participant = await _userService.GetUserByMatricNoAsync(participantMatricNo);
            if (participant == null)
                return false;

            // Validate eligibility
            if (!session.AllowedDepartments.Contains(User.Departments.Any) &&
                (participant.Department == null || !session.AllowedDepartments.Contains(participant.Department.Value)))
                return false;

            if (!session.AllowedLevels.Contains(User.Levels.Any) &&
                (participant.Level == null || !session.AllowedLevels.Contains(participant.Level.Value)))
                return false;

            await _sessionRepository.AddParticipantAsync(session.Id, participant.Id);
            return true;
        }

        /// <summary>
        /// Remove participant from session
        /// </summary>
        public async Task<bool> RemoveParticipantAsync(string sessionId, int userId)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null)
                return false;

            return await _sessionRepository.RemoveParticipantAsync(session.Id, userId);
        }

        public async Task<bool> RemoveParticipantAsync(string sessionId, string participantMatricNo)
        {
            var participant = await _userService.GetUserByMatricNoAsync(participantMatricNo);
            if (participant == null)
            {
                return false;
            }

            return await RemoveParticipantAsync(sessionId, participant.Id);
        }

        public async Task<bool> LogAttendanceStatusAsync(string sessionId, string participantMatricNo, Session.StudentStatus status, DateTime? timestamp = null)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null)
                return false;

            var participant = await _userService.GetUserByMatricNoAsync(participantMatricNo);
            if (participant == null)
                return false;

            await _sessionRepository.AddAttendanceLogAsync(session.Id, participant.Id, status, timestamp ?? DateTime.UtcNow);
            return true;
        }

        public async Task<List<AttendanceLog>> GetAttendanceLogsAsync(string sessionId)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null)
            {
                return new List<AttendanceLog>();
            }

            return await _sessionRepository.GetAttendanceLogsAsync(session.Id);
        }

        /// <summary>
        /// Get session with all participant details
        /// </summary>
        public async Task<Models.Session?> GetSessionWithParticipantsAsync(string sessionId)
        {
            return await _sessionRepository.GetSessionByIdAsync(sessionId);
        }

        public async Task<Models.Session?> GetSessionByDatabaseIdAsync(int sessionId)
        {
            return await _sessionRepository.GetSessionByPkAsync(sessionId);
        }

        /// <summary>
        /// Get all active sessions
        /// </summary>
        public async Task<List<Models.Session>> GetActiveSessionsAsync()
        {
            return await _sessionRepository.GetActiveSessionsAsync();
        }

        /// <summary>
        /// Get sessions by lecturer
        /// </summary>
        public async Task<List<Models.Session>> GetSessionsByLecturerAsync(string lecturerMatricNo)
        {
            return await _sessionRepository.GetSessionsByLecturerAsync(lecturerMatricNo);
        }

        /// <summary>
        /// Get session for a participant
        /// </summary>
        public async Task<Models.Session?> GetSessionByParticipantAsync(string participantMatricNo)
        {
            return await _sessionRepository.GetSessionByParticipantAsync(participantMatricNo);
        }

        /// <summary>
        /// Get participant count for session
        /// </summary>
        public async Task<int> GetParticipantCountAsync(string sessionId)
        {
            var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
            if (session == null)
                return 0;

            return await _sessionRepository.GetSessionParticipantCountAsync(session.Id);
        }

        private static string GenerateSessionCode() => 
            $"{DateTime.UtcNow.AddHours(1):yyyyMMdd}-{string.Concat(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))))}";
    }
}
