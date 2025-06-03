using FutaMeetWeb.Models;               
using System.Collections.Concurrent;

namespace FutaMeetWeb.Services;

public class SessionService
{
    private readonly ConcurrentDictionary<string, Session> _sessions = [];

    // Define ParticipantScoreDetails here or ensure it's in Models/User.cs or a new Models/ParticipantScoreDetails.cs
    public class ParticipantScoreDetails
    {
        public string ParticipantId { get; set; }
        public double FinalScorePercentage { get; set; }
        public double TotalSessionMinutes { get; set; }
        public double TimeActiveMinutes { get; set; }
        public double TimeInactiveMinutes { get; set; }
        public double TimeBatteryLowMinutes { get; set; }
        public double TimeDataFinishedMinutes { get; set; }
        public double TimeDisconnectedMinutes { get; set; }
        public string ParticipantName { get; set; } // Added to fetch participant name
    }
  
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

        // If session has already started, log an initial event for this joining participant
        if (session.Status == SessionStatus.Started)
        {
            if (!session.ParticipantEvents.ContainsKey(participantId))
            {
                session.ParticipantEvents[participantId] = new List<(Session.StudentStatus, DateTime)>();
            }
            var joinTime = DateTime.UtcNow.AddHours(1);
            session.ParticipantEvents[participantId].Add((Session.StudentStatus.Active, joinTime));
            Console.WriteLine($"Logged Active event for {participantId} joining started session at: {joinTime}");
        }

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
        if (session != null && session.Status != SessionStatus.Started) // Ensure it's not already started
        {
            session.IsSessionStarted = true;
            session.Status = SessionStatus.Started;
            session.StartTime = DateTime.UtcNow.AddHours(1);

            // Log initial 'Active' status for all current participants
            foreach (var participantId in session.ParticipantIds.ToList()) // ToList to avoid modification issues if any
            {
                if (!session.ParticipantEvents.ContainsKey(participantId))
                {
                    session.ParticipantEvents[participantId] = new List<(Session.StudentStatus, DateTime)>();
                }
                // Add initial active event at session start time
                session.ParticipantEvents[participantId].Add((Session.StudentStatus.Active, session.StartTime));
                session.ParticipantStatuses[participantId] = Session.StudentStatus.Active; // Ensure current status is active
                Console.WriteLine($"Logged initial Active event for {participantId} at session start: {session.StartTime}");
            }
        }
        return session;
    }
    public bool UpdateParticipantStatus(string sessionId, string participantId, Session.StudentStatus status)
    {
        if (_sessions.TryGetValue(sessionId, out var session) &&
            session.ParticipantIds.Contains(participantId) &&
            session.Status == SessionStatus.Started) // Only log events if session has officially started
        {
            // Optimization: if status hasn't changed, don't log an event or return true
            if (session.ParticipantStatuses.TryGetValue(participantId, out var currentStatus) && currentStatus == status)
            {
                return false;
            }

            session.ParticipantStatuses[participantId] = status;

            // Log the event
            if (!session.ParticipantEvents.ContainsKey(participantId))
            {
                session.ParticipantEvents[participantId] = new List<(Session.StudentStatus, DateTime)>();
            }
            
            var eventTimestamp = DateTime.UtcNow.AddHours(1); // Consistent with StartTime/EndTime
            session.ParticipantEvents[participantId].Add((status, eventTimestamp));

            Console.WriteLine($"Logged event for {participantId}: status {status} at {eventTimestamp} in session {sessionId}");
            return true; // Indicates a scorable event was logged
        }
        // If session not started, or participant/session not found, but we might still want to update current status if possible
        else if (_sessions.TryGetValue(sessionId, out session) && session.ParticipantIds.Contains(participantId))
        {
             if (session.ParticipantStatuses.TryGetValue(participantId, out var currentStatus) && currentStatus == status)
                return false;
            session.ParticipantStatuses[participantId] = status; // Update current status
            Console.WriteLine($"Updated {participantId} status to {status} in session {sessionId} (session not started, event not logged for scoring).");
            return false; // Indicates no scorable event was logged
        }
        return false; // Participant or session not found
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

    public Dictionary<string, ParticipantScoreDetails> CalculateAttendanceScore(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new Dictionary<string, ParticipantScoreDetails>();

        var totalSessionMinutes = Math.Max(
            ((session.EndTime ?? DateTime.UtcNow) - session.StartTime).TotalMinutes,
            1
        );

        var userDetails = MockApiService.GetUsers().ToDictionary(u => u.MatricNo, u => u.Name); // For fetching names

        return session.ParticipantIds
            .ToDictionary(participantId => participantId,
            participantId => CalculateScoreperParticipant(session, participantId, totalSessionMinutes, userDetails.GetValueOrDefault(participantId, "Unknown User")));
    }

    public ParticipantScoreDetails CalculateScoreperParticipant(Session session, string participantId, double totalSessionMinutes, string participantName)
    {
        var details = new ParticipantScoreDetails
        {
            ParticipantId = participantId,
            TotalSessionMinutes = totalSessionMinutes,
            ParticipantName = participantName
        };

        double rawScore = 0;
        DateTime currentTime = session.StartTime; // Start tracking from session start
        Session.StudentStatus currentStatusForCalc = Session.StudentStatus.Active; // Default assumption

        // Attempt to get the initial status at session start if available, otherwise assume Active.
        // This is important if a participant joined *before* the session officially started.
        if (session.ParticipantEvents.TryGetValue(participantId, out var events) && events.Any())
        {
            // If first event is after session start, assume Active from session.StartTime till first event.
            var firstEvent = events.OrderBy(e => e.TimeStamp).First();
            if (firstEvent.TimeStamp > session.StartTime)
            {
                var durationBeforeFirstEvent = (firstEvent.TimeStamp - session.StartTime).TotalMinutes;
                if (durationBeforeFirstEvent > 0)
                {
                    rawScore += GetStatusWeight(Session.StudentStatus.Active) * durationBeforeFirstEvent;
                    UpdateDurationForStatus(details, Session.StudentStatus.Active, durationBeforeFirstEvent);
                }
            }
            // The status of the first event becomes the current status for calculation from its timestamp.
            currentStatusForCalc = firstEvent.status;
            currentTime = firstEvent.TimeStamp;
        }
        else // No events at all for this participant
        {
            // If no events, assume they were in their last known status for the whole session, or Active if no status recorded.
            // This covers participants who joined but had no status changes.
            currentStatusForCalc = session.ParticipantStatuses.TryGetValue(participantId, out var lastKnownStatus) ? lastKnownStatus : Session.StudentStatus.Active;
            var duration = totalSessionMinutes;
            if (duration > 0) {
                rawScore += GetStatusWeight(currentStatusForCalc) * duration;
                UpdateDurationForStatus(details, currentStatusForCalc, duration);
            }
            double maxPossibleScoreForNoEventCase = 10 * totalSessionMinutes;
            if (maxPossibleScoreForNoEventCase == 0) details.FinalScorePercentage = 0;
            else details.FinalScorePercentage = Math.Clamp((rawScore / maxPossibleScoreForNoEventCase) * 100, 0, 100);
            return details;
        }

        // Process subsequent events
        foreach (var (nextStatus, eventTime) in events.OrderBy(e => e.TimeStamp).Skip(1)) // Skip the first event as it set the initial state
        {
            if (eventTime < currentTime) continue; // Should not happen if events are ordered, but good for safety

            var durationInCurrentStatus = (eventTime - currentTime).TotalMinutes;
            if (durationInCurrentStatus > 0)
            {
                rawScore += GetStatusWeight(currentStatusForCalc) * durationInCurrentStatus;
                UpdateDurationForStatus(details, currentStatusForCalc, durationInCurrentStatus);
            }
            currentStatusForCalc = nextStatus;
            currentTime = eventTime;
        }

        // Account for time from the last event to the session end (or current time if session not ended)
        DateTime sessionEffectiveEndTime = session.EndTime ?? DateTime.UtcNow.AddHours(1);
        if (sessionEffectiveEndTime > currentTime) // Ensure we only add duration if session end is after last event time
        {
            var durationAfterLastEvent = (sessionEffectiveEndTime - currentTime).TotalMinutes;
            if (durationAfterLastEvent > 0)
            {
                // The status for this final period is the `currentStatusForCalc` (status of the last processed event)
                rawScore += GetStatusWeight(currentStatusForCalc) * durationAfterLastEvent;
                UpdateDurationForStatus(details, currentStatusForCalc, durationAfterLastEvent);
            }
        }
        
        double maxPossibleScore = 10 * totalSessionMinutes;
        if (maxPossibleScore == 0) details.FinalScorePercentage = 0;
        else details.FinalScorePercentage = Math.Clamp((rawScore / maxPossibleScore) * 100, 0, 100);

        return details;
    }

    private void UpdateDurationForStatus(ParticipantScoreDetails details, Session.StudentStatus status, double duration)
    {
        switch (status)
        {
            case Session.StudentStatus.Active: details.TimeActiveMinutes += duration; break;
            case Session.StudentStatus.InActive: details.TimeInactiveMinutes += duration; break;
            case Session.StudentStatus.BatteryLow: details.TimeBatteryLowMinutes += duration; break;
            case Session.StudentStatus.DataFinished: details.TimeDataFinishedMinutes += duration; break;
            case Session.StudentStatus.Disconnected: details.TimeDisconnectedMinutes += duration; break;
        }
    }

    private int GetStatusWeight(Session.StudentStatus status)
    {
        return status switch
        {
            Session.StudentStatus.Active => 10,
            Session.StudentStatus.InActive => -2,      // Was -5
            Session.StudentStatus.BatteryLow => -3,    // Was -10
            Session.StudentStatus.DataFinished => -5,  // Was -10
            Session.StudentStatus.Disconnected => -10, // Was -20
            _ => 0
        };
    }
}