using VIIDII.Data;
using VIIDII.Models;
using Microsoft.EntityFrameworkCore;

namespace VIIDII.Services
{
    /// <summary>
    /// Repository for session persistence operations via DbContext
    /// Note: SessionParticipant.SessionId is an int FK to Session.Id, not the string SessionId
    /// </summary>
    public class SessionRepository
    {
        private readonly ViidiiDbContext _context;

        public SessionRepository(ViidiiDbContext context)
        {
            _context = context;
        }

        public async Task<Session> CreateSessionAsync(Session session)
        {
            await _context.Sessions.AddAsync(session);
            await _context.SaveChangesAsync();
            return session;
        }

        public async Task<Session?> GetSessionByIdAsync(string sessionId)
        {
            return await _context.Sessions
                .Include(s => s.Lecturer)
                .Include(s => s.Participants)
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);
        }

        public async Task<Session?> GetSessionByPkAsync(int sessionPk)
        {
            return await _context.Sessions
                .Include(s => s.Lecturer)
                .Include(s => s.Participants)
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == sessionPk);
        }

        public async Task<List<Session>> GetSessionsByLecturerAsync(string lecturerMatricNo)
        {
            return await _context.Sessions
                .Where(s => s.LecturerMatricNo == lecturerMatricNo && 
                           (s.Status == SessionStatus.Active || s.Status == SessionStatus.Started))
                .Include(s => s.Participants)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Session>> GetActiveSessionsAsync()
        {
            return await _context.Sessions
                .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Started)
                .Include(s => s.Lecturer)
                .Include(s => s.Participants)
                .ToListAsync();
        }

        public async Task<Session?> GetSessionByParticipantAsync(string participantMatricNo)
        {
            return await _context.Sessions
                .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Started)
                .Include(s => s.Participants)
                .ThenInclude(sp => sp.User)
                .FirstOrDefaultAsync(s => s.Participants.Any(p => p.User.MatricNo == participantMatricNo));
        }

        public async Task<Session?> UpdateSessionAsync(Session session)
        {
            _context.Sessions.Update(session);
            await _context.SaveChangesAsync();
            return session;
        }

        public async Task<bool> DeleteSessionAsync(string sessionId)
        {
            var session = await _context.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
            if (session == null)
                return false;

            _context.Sessions.Remove(session);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Add participant to session by session primary key
        /// SessionParticipant.SessionId is the int FK to Session.Id
        /// </summary>
        public async Task<SessionParticipant> AddParticipantAsync(int sessionPk, int userId)
        {
            var participant = new SessionParticipant
            {
                SessionId = sessionPk,
                UserId = userId
            };

            await _context.SessionParticipants.AddAsync(participant);
            await _context.SaveChangesAsync();
            return participant;
        }

        /// <summary>
        /// Remove participant by session primary key
        /// </summary>
        public async Task<bool> RemoveParticipantAsync(int sessionPk, int userId)
        {
            var participant = await _context.SessionParticipants
                .FirstOrDefaultAsync(sp => sp.SessionId == sessionPk && sp.UserId == userId);

            if (participant == null)
                return false;

            _context.SessionParticipants.Remove(participant);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Get participants by session primary key
        /// </summary>
        public async Task<List<SessionParticipant>> GetSessionParticipantsAsync(int sessionPk)
        {
            return await _context.SessionParticipants
                .Where(sp => sp.SessionId == sessionPk)
                .Include(sp => sp.User)
                .ToListAsync();
        }

        /// <summary>
        /// Get participant count by session primary key
        /// </summary>
        public async Task<int> GetSessionParticipantCountAsync(int sessionPk)
        {
            return await _context.SessionParticipants
                .CountAsync(sp => sp.SessionId == sessionPk);
        }
    }
}
