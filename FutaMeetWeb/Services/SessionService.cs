using FutaMeetWeb.Models;
using System.Collections.Concurrent;

namespace FutaMeetWeb.Services;

public class SessionService
{
    private readonly ConcurrentDictionary<string, Session> _sessions = [];


    public bool IsLecturer(string matricNo)
    {
        var lecturers = MockApiService.GetLecturers();
        return lecturers.Any(l => l.MatricNo == matricNo);
    }
    public Session CreateSession(string lecturerId, string title, bool? replaceExisting = false)
    {
        var lecturers = MockApiService.GetLecturers();
        if (!lecturers.Any(l => l.MatricNo == lecturerId))
            return null;

        var existingSession = _sessions.Values
            .FirstOrDefault(s => s.LecturerId == lecturerId && s.Status == SessionStatus.Active);

        if (existingSession != null)
        {
            if (replaceExisting == null || replaceExisting == false)
                return existingSession;
            _sessions.TryRemove(existingSession.SessionId, out _);
        }

        var session = new Session(lecturerId, title);
        session.Status = SessionStatus.Active;
        _sessions.TryAdd(session.SessionId, session);
        return session;
    }

    public Session LeaveSession(string sessionId, string participantId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status != SessionStatus.Active)
            return null;
        if (!session.ParticipantIds.Contains(participantId))
            return null;
        session.ParticipantIds.Remove(participantId);
        return session;
    }

    public (Session Session, string Error) JoinSession(string sessionId, string participantId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status == SessionStatus.Ended)
            return (null, "Session not found or inactive.");
        if (!MockApiService.GetUsers().Any(u => u.MatricNo == participantId))
            return (null, "Invalid user.");
        if (_sessions.Values.Any(s => s.Status == SessionStatus.Active && s.ParticipantIds.Contains(participantId) && s.SessionId != sessionId))
            return (null, "You are already in a different session.");
        session.ParticipantIds.Add(participantId);
        return (session, null);
    }

    public Session EndSession(string sessionId, string lecturerId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;
        if (session.LecturerId != lecturerId || session.Status != SessionStatus.Started)
            return null;
        session.Status = SessionStatus.Ended;
        session.EndTime = DateTime.UtcNow.AddHours(1);
        session.ParticipantIds.Clear();
        session.IsSessionStarted = false;
        return session;
    }
    public Session StartSession(string sessionId)
    {
        var session = GetSessionById(sessionId);
        if (session != null)
        {
            session.IsSessionStarted = true;
            session.Status = SessionStatus.Started;
            session.StartTime = DateTime.UtcNow.AddHours(1);
        }
        return session;
    }

    public Session GetSessionById(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public List<Session> GetSessionsByLecturer(string lecturerId) =>
        _sessions.Values
            .Where(s => s.LecturerId == lecturerId && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Started))
            .ToList();

    public List<Session> GetSessionsByParticipant(string participantId) =>
        _sessions.Values
            .Where(s => s.Status == SessionStatus.Active && s.ParticipantIds.Contains(participantId))
            .ToList();

    public List<Session> GetSessionsBy<TKey>(TKey key, Func<Session, TKey> selector) =>
        _sessions.Values
            .Where(s => Equals(selector(s), key))
            .ToList();

    public List<Session> GetActiveSessions() =>
        _sessions.Values
            .Where(s => s.Status == SessionStatus.Active)
            .ToList();
}