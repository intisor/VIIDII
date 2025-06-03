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
                session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>(); // Changed DateTimeOffset to DateTime
            }
            var joinTime = DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
            if (joinTime > session.StartTime)
            {
                var absentDuration = (joinTime - session.StartTime).TotalMinutes;
                // Log disconnected for the duration of absence
                session.ParticipantEvents[participantId].Add((Session.StudentStatus.Disconnected, session.StartTime));

                Console.WriteLine($"JoinSession: {participantId} absent for {absentDuration:F1} min, Disconnected at {session.StartTime}, Active at {joinTime}");
            }
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
        session.EndTime = DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
        // Don't clear participant IDs so we can still calculate scores
        // session.ParticipantIds.Clear();
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
            session.StartTime = DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
            Console.WriteLine($"StartSession: Session {sessionId} started at {session.StartTime}, Participants: {string.Join(", ", session.ParticipantIds)}");            // Log initial 'Active' status for all current participants
            foreach (var participantId in session.ParticipantIds.ToList()) // ToList to avoid modification issues if any
            {
                if (!session.ParticipantEvents.ContainsKey(participantId))
                {
                    session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>(); // Changed DateTimeOffset to DateTime
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
                session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>(); // Changed DateTimeOffset to DateTime
            }
            var eventTimestamp = DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
            session.ParticipantEvents[participantId].Add((status, eventTimestamp));
            Console.WriteLine($"UpdateParticipantStatus: {participantId} status {status} at {eventTimestamp} in session {sessionId}"); return true; // Indicates a scorable event was logged
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
        var endTime = session.EndTime ?? DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
        var totalSessionMinutes = Math.Max((endTime - session.StartTime).TotalMinutes, 1);
        Console.WriteLine($"CalculateAttendanceScore: Session {sessionId}, Start={session.StartTime}, End={endTime}, Duration={totalSessionMinutes:F1} min");
        var userDetails = MockApiService.GetUsers().ToDictionary(u => u.MatricNo, u => u.Name); // For fetching names

        // Get all participant IDs that have events, even if they're no longer in ParticipantIds
        var allParticipantIds = new HashSet<string>(session.ParticipantIds);
        
        // Add any participants that have events but might not be in the current ParticipantIds
        foreach (var participantId in session.ParticipantEvents.Keys)
        {
            allParticipantIds.Add(participantId);
        }
        
        // Add participants that have status entries
        foreach (var participantId in session.ParticipantStatuses.Keys)
        {
            allParticipantIds.Add(participantId);
        }

        return allParticipantIds
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
        DateTime currentTime = session.StartTime; // Changed DateTimeOffset to DateTime
        Session.StudentStatus currentStatusForCalc = Session.StudentStatus.Disconnected;
        Session.StudentStatus? statusThatPrecededCurrentSegment = null;

        Console.WriteLine($"CalculateScore: {participantId}, TotalSessionMinutes={totalSessionMinutes:F1}, StartTime={session.StartTime}");

        if (session.ParticipantEvents.TryGetValue(participantId, out var events) && events.Any())
        {
            var sortedEvents = events.OrderBy(e => e.timeStamp).ToList();
            Console.WriteLine($"CalculateScore: {participantId} has {sortedEvents.Count} events");

            var firstEventDetails = sortedEvents.First();
            if (firstEventDetails.timeStamp > session.StartTime)
            {
                var durationBeforeFirstEvent = (firstEventDetails.timeStamp - session.StartTime).TotalMinutes;
                if (durationBeforeFirstEvent > 0)
                {
                    rawScore += GetStatusWeight(Session.StudentStatus.Disconnected, null) * durationBeforeFirstEvent;
                    UpdateDurationForStatus(details, Session.StudentStatus.Disconnected, durationBeforeFirstEvent);
                    Console.WriteLine($"CalculateScore: {participantId} Disconnected for {durationBeforeFirstEvent:F1} min, Score+={rawScore:F1}");
                }
                statusThatPrecededCurrentSegment = Session.StudentStatus.Disconnected;
            }
            else
            {
                statusThatPrecededCurrentSegment = null;
            }

            currentTime = firstEventDetails.timeStamp; // Ensure currentTime is DateTime
            currentStatusForCalc = firstEventDetails.status;
            Console.WriteLine($"CalculateScore: {participantId} First event: {currentStatusForCalc} at {currentTime}");

            foreach (var nextEventDetails in sortedEvents.Skip(1))
            {
                var nextStatus = nextEventDetails.status;
                var eventTime = nextEventDetails.timeStamp; // Ensure eventTime is DateTime

                if (eventTime < currentTime) continue;

                var durationInCurrentStatus = (eventTime - currentTime).TotalMinutes;
                if (durationInCurrentStatus > 0)
                {
                    var weight = GetStatusWeight(currentStatusForCalc, statusThatPrecededCurrentSegment);
                    rawScore += weight * durationInCurrentStatus;
                    UpdateDurationForStatus(details, currentStatusForCalc, durationInCurrentStatus);
                    Console.WriteLine($"CalculateScore: {participantId} {currentStatusForCalc} for {durationInCurrentStatus:F1} min, Weight={weight}, Score+={rawScore:F1}");
                }

                statusThatPrecededCurrentSegment = currentStatusForCalc;
                currentStatusForCalc = nextStatus;
                currentTime = eventTime;
            }

            DateTime sessionEffectiveEndTime = session.EndTime ?? DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
            if (sessionEffectiveEndTime > currentTime)
            {
                var durationAfterLastEvent = (sessionEffectiveEndTime - currentTime).TotalMinutes;
                if (durationAfterLastEvent > 0)
                {
                    var weight = GetStatusWeight(currentStatusForCalc, statusThatPrecededCurrentSegment);
                    rawScore += weight * durationAfterLastEvent;
                    UpdateDurationForStatus(details, currentStatusForCalc, durationAfterLastEvent);
                    Console.WriteLine($"CalculateScore: {participantId} {currentStatusForCalc} (end) for {durationAfterLastEvent:F1} min, Weight={weight}, Score+={rawScore:F1}");
                }
            }
        }
        else
        {
            currentStatusForCalc = session.ParticipantStatuses.TryGetValue(participantId, out var lastKnownStatus) ? lastKnownStatus : Session.StudentStatus.Disconnected;
            var duration = totalSessionMinutes;
            if (duration > 0)
            {
                var weight = GetStatusWeight(currentStatusForCalc, null);
                rawScore += weight * duration;
                UpdateDurationForStatus(details, currentStatusForCalc, duration);
                Console.WriteLine($"CalculateScore: {participantId} No events, {currentStatusForCalc} for {duration:F1} min, Weight={weight}, Score={rawScore:F1}");
            }
        }

        double maxPossibleScore = 10 * totalSessionMinutes;
        details.FinalScorePercentage = maxPossibleScore == 0 ? 0 : Math.Clamp((rawScore / maxPossibleScore) * 100, 0, 100);
        Console.WriteLine($"CalculateScore: {participantId} Final: rawScore={rawScore:F1}, Max={maxPossibleScore:F1}, Percentage={details.FinalScorePercentage:F1}%");

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

    private int GetStatusWeight(Session.StudentStatus currentStatus, Session.StudentStatus? statusOfImmediatelyPrecedingEvent)
    {
        if (statusOfImmediatelyPrecedingEvent.HasValue)
        {
            bool wasWarning = statusOfImmediatelyPrecedingEvent == Session.StudentStatus.BatteryLow ||
                              statusOfImmediatelyPrecedingEvent == Session.StudentStatus.DataFinished;

            if (wasWarning)
            {
                if (currentStatus == Session.StudentStatus.InActive) return -1; // Grace penalty for InActive after warning
                if (currentStatus == Session.StudentStatus.Disconnected) return -2; // Grace penalty for Disconnected after warning
            }
        }

        // Default weights if no grace applies or for states not covered by grace
        return currentStatus switch
        {
            Session.StudentStatus.Active => 10,
            Session.StudentStatus.InActive => -2,       // Standard InActive penalty
            Session.StudentStatus.BatteryLow => 0,      // Warning state, not directly penalized; grace is applied to subsequent Inactive/Disconnected states
            Session.StudentStatus.DataFinished => 0,    // Warning state, not directly penalized; grace is applied to subsequent Inactive/Disconnected states
            Session.StudentStatus.Disconnected => -3,  // Standard Disconnected penalty
            _ => 0
        };
    }
}