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
    public Session CreateSession(string lecturerId, string title, List<User.Departments> allowedDepartments,List<User.Levels> allowedLevels, bool? replaceExisting = false)
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

        var session = new Session(lecturerId)
        {
            Title = title,
            AllowedDepartments = allowedDepartments ?? new List<User.Departments>(),
            AllowedLevels = allowedLevels ?? new List<User.Levels>()
        };

        session.AllowedDepartments = session.AllowedDepartments.Contains(User.Departments.Any) ? [.. Enum.GetValues<User.Departments>()] : session.AllowedDepartments;

        if (session.AllowedLevels.Contains(User.Levels.Any))
        {
            session.AllowedLevels = Enum.GetValues(typeof(User.Levels)).Cast<User.Levels>().ToList();
        }


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
    public (Session Session, string Error) JoinSession(string sessionId, string participantId, string connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status == SessionStatus.Ended)
            return (null, "Session not found or inactive.");
        var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == participantId);
        if (user is null)
            return (null, "Invalid user.");
        if (!session.AllowedDepartments.Contains(User.Departments.Any) && !session.AllowedDepartments.Contains(user.Department.Value))
            return (null, "Your department is not allowed for this session.");

        if (!session.AllowedLevels.Contains(User.Levels.Any) &&!session.AllowedLevels.Contains(user.Level.Value))
            return (null, "Your level is not allowed for this session.");
        if (_sessions.Values.Any(s => s.Status == SessionStatus.Active && s.ParticipantIds.Contains(participantId) && s.SessionId != sessionId))
            return (null, "You are already in a different session.");
        if (string.IsNullOrEmpty(connectionId))
            return (null, "Invalid connection ID.");
        session.ParticipantIds.Add(participantId);
        session.ParticipantStatuses[participantId] = Session.StudentStatus.Active;
        session.ParticipantConnectionIds[participantId] = connectionId;
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
    public bool UpdateParticipantStatus(string sessionId,string participantId,Session.StudentStatus status )
    {
        if (_sessions.TryGetValue(sessionId, out var session) && session.ParticipantIds.Contains(participantId))
        {
            if (session.ParticipantStatuses.TryGetValue(participantId, out var currentStatus) && currentStatus == status)
                return false;
            session.ParticipantStatuses[participantId] = status;
            Console.WriteLine($"Updated {participantId} status to {status} in session {sessionId}");
            return true;
        }
        return false;
    }
    public Dictionary<string,Session.StudentStatus> GetParticipantStatus(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session.ParticipantStatuses : new Dictionary<string, Session.StudentStatus>();
    }
    public Session GetSessionById(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;
    public List<Session> GetSessionsByLecturer(string lecturerId) =>
        _sessions.Values
            .Where(s => s.LecturerId == lecturerId && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Started))
            .ToList();
    public Session GetSessionByParticipant(string participantId) =>
        _sessions.Values
            .FirstOrDefault(s =>
                (s.Status == SessionStatus.Started || s.Status == SessionStatus.Active)
                && s.ParticipantIds.Contains(participantId));
    public List<Session> GetSessionsBy<TKey>(TKey key, Func<Session, TKey> selector) =>
        _sessions.Values
            .Where(s => Equals(selector(s), key))
            .ToList();
    public List<Session> GetActiveSessions() =>
        _sessions.Values
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Started)
            .ToList();

    public Dictionary<string, double> CalculateAttendanceScore(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new Dictionary<string, double>();
        var totalMinutes = Math.Max(
            ((session.EndTime ?? DateTime.UtcNow) - session.StartTime).TotalMinutes,
            1
        );
        return session.ParticipantIds
            .ToDictionary(participantId =>  participantId,
            participantId => CalculateScoreperParticicpant(session,participantId,totalMinutes));
    }
    public double CalculateScoreperParticicpant(Session session, string participantId, double totalMinutes)
    {
        if (!session.ParticipantEvents.TryGetValue(participantId, out var events) || events.Count == 0)
            return 0;
        double score = 0;
        DateTime lastTime = session.StartTime;
        foreach(var (status,time) in events)
        {
            score += GetStatusWeight(status) * (time - lastTime).TotalMinutes;
            lastTime = time;
        }
        var finalMinutes = ((session.EndTime ?? DateTime.UtcNow) - lastTime).TotalMinutes;
        if(session.ParticipantStatuses.TryGetValue(participantId, out var lastStatus))
        {
            score += GetStatusWeight(lastStatus) * finalMinutes;
        }
        return Math.Clamp((score / totalMinutes) * 100, 0, 100);
    }
    private int GetStatusWeight(Session.StudentStatus status)
    {
        return status switch
        {
            Session.StudentStatus.Active => 10,
            Session.StudentStatus.InActive => -5,
            Session.StudentStatus.BatteryLow => -10,
            Session.StudentStatus.DataFinished => -10,
            Session.StudentStatus.Disconnected => -20,
            _ => 0
        };
    }
}