using VIIDII.Models;               
using System.Collections.Concurrent;

namespace VIIDII.Services;

public class SessionService
{
    private readonly ConcurrentDictionary<string, Session> _sessions = [];

    /// <summary>
    /// Tracks the DFA state of every peer connection, keyed by peerId.
    /// Shared across all sessions — a peerId is globally unique.
    /// </summary>
    private readonly ConcurrentDictionary<string, PeerConnectionContext> _peerStates = [];

    /// <summary>
    /// Attempts a DFA state transition for the given peer.
    /// Creates the context on first use (Idle state).
    /// </summary>
    /// <returns>True if the transition was valid and applied.</returns>
    public bool TryTransitionPeer(string peerId, PeerTrigger trigger, out PeerState newState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);

        var ctx = _peerStates.GetOrAdd(peerId, static id => new PeerConnectionContext(id));
        var result = ctx.TryTransition(trigger, out newState);

        if (!result)
        {
            Console.WriteLine($"[DFA] Invalid transition: peer={peerId}, current={ctx.CurrentState}, trigger={trigger}");
        }
        else
        {
            Console.WriteLine($"[DFA] Transition: peer={peerId}, trigger={trigger} → {newState}");
        }

        return result;
    }

    /// <summary>
    /// Gets the current DFA state for a peer, or null if no context exists.
    /// </summary>
    public PeerState? GetPeerState(string peerId)
    {
        return _peerStates.TryGetValue(peerId, out var ctx) ? ctx.CurrentState : null;
    }

    /// <summary>
    /// Returns peer states for all peers in a session.
    /// </summary>
    public Dictionary<string, PeerState> GetPeerStatesForSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return [];

        var result = new Dictionary<string, PeerState>();
        foreach (var peerId in session.ParticipantIds)
        {
            if (_peerStates.TryGetValue(peerId, out var ctx))
            {
                result[peerId] = ctx.CurrentState;
            }
        }
        return result;
    }

    /// <summary>
    /// Removes peer state tracking for a peer (e.g., on session end/leave).
    /// </summary>
    public void RemovePeerState(string peerId)
    {
        _peerStates.TryRemove(peerId, out _);
    }

    // Define ParticipantScoreDetails here or ensure it's in Models/User.cs or a new Models/ParticipantScoreDetails.cs
    public class ParticipantScoreDetails
    {
        public required string ParticipantId { get; set; }
        public double FinalScorePercentage { get; set; }
        public double TotalSessionMinutes { get; set; }
        public double TimeActiveMinutes { get; set; }
        public double TimeInactiveMinutes { get; set; }
        public double TimeBatteryLowMinutes { get; set; }
        public double TimeDataFinishedMinutes { get; set; }
        public double TimeDisconnectedMinutes { get; set; }
        public required string ParticipantName { get; set; } // Added to fetch participant name
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
            AllowedDepartments = allowedDepartments ?? [],
            AllowedLevels = allowedLevels ?? []
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
    public (Session Session, string? Error) JoinSession(string sessionId, string participantId, string? connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status == SessionStatus.Ended)
            return (null, "Session not found or inactive.");
        var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == participantId);
        if (user is null)
            return (null, "Invalid user.");

        if (!session.AllowedDepartments.Contains(User.Departments.Any))
        {
            if (user.Department is null || !session.AllowedDepartments.Contains(user.Department.Value))
                return (null, "Your department is not allowed for this session.");
        }

        if (!session.AllowedLevels.Contains(User.Levels.Any))
        {
            if (user.Level is null || !session.AllowedLevels.Contains(user.Level.Value))
                return (null, "Your level is not allowed for this session.");
        }

        if (_sessions.Values.Any(s => s.Status == SessionStatus.Active && s.ParticipantIds.Contains(participantId) && s.SessionId != sessionId))
            return (null, "You are already in a different session.");

        session.ParticipantIds.Add(participantId);
        session.ParticipantStatuses[participantId] = Session.StudentStatus.Active;
        if (!string.IsNullOrEmpty(connectionId))
        {
            session.ParticipantConnectionIds[participantId] = connectionId;
        }

        // If session has already started, log an initial event for this joining participant
        if (session.Status == SessionStatus.Started)
        {
            if (!session.ParticipantEvents.ContainsKey(participantId))
            {
                session.ParticipantEvents[participantId] = [];
            }
            var joinTime = DateTime.UtcNow; // Pure UTC
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
    public Session EndSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;
        return EndSession(sessionId, session.LecturerId);
    }

    public Session EndSession(string sessionId, string lecturerId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;
        if (session.LecturerId != lecturerId || (session.Status != SessionStatus.Started && session.Status != SessionStatus.Active))
            return null;
        session.Status = SessionStatus.Ended;
        session.EndTime = DateTime.UtcNow; // Pure UTC
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
            session.StartTime = DateTime.UtcNow; // Pure UTC
            Console.WriteLine($"StartSession: Session {sessionId} started at {session.StartTime}, Participants: {string.Join(", ", session.ParticipantIds)}");            // Log initial 'Active' status for all current participants
            foreach (var participantId in session.ParticipantIds.ToList()) // ToList to avoid modification issues if any
            {
                if (!session.ParticipantEvents.ContainsKey(participantId))
                {
                    session.ParticipantEvents[participantId] = [];
                }
                // Add initial active event at session start time
                session.ParticipantEvents[participantId].Add((Session.StudentStatus.Active, session.StartTime));
                session.ParticipantStatuses[participantId] = Session.StudentStatus.Active; // Ensure current status is active
                Console.WriteLine($"Logged initial Active event for {participantId} at session start: {session.StartTime}");
            }
        }
        return session;
    }

    public void RemoveSessionsByLecturer(string lecturerId)
    {
        var sessionsToRemove = _sessions.Values
            .Where(s => s.LecturerId == lecturerId)
            .Select(s => s.SessionId)
            .ToList();

        foreach (var sessionId in sessionsToRemove)
        {
            _sessions.TryRemove(sessionId, out _);
        }
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
                session.ParticipantEvents[participantId] = [];
            }
            var eventTimestamp = DateTime.UtcNow; // Pure UTC
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

        // Use UTC now for endTime to avoid over-rewarding ongoing sessions
        var endTime = session.EndTime ?? DateTime.UtcNow;
        var totalSessionMinutes = (endTime - session.StartTime).TotalMinutes;

        // Return empty scores for invalid (zero or negative) session duration
        if (totalSessionMinutes <= 0)
        {
            Console.WriteLine($"CalculateAttendanceScore: Session {sessionId} has invalid duration ({totalSessionMinutes:F1} min), returning empty scores");
            return new Dictionary<string, ParticipantScoreDetails>();
        }


        Console.WriteLine($"CalculateAttendanceScore: Session {sessionId}, Start={session.StartTime}, End={endTime}, Duration={totalSessionMinutes:F1} min");

        // Single-pass participant ID collection, including past participants with events/statuses
        HashSet<string> allParticipantIds = [..session.ParticipantIds];
        allParticipantIds.UnionWith(session.ParticipantEvents.Keys);
        allParticipantIds.UnionWith(session.ParticipantStatuses.Keys);
        
        // IMPORTANT: Exclude the lecturer from participant scoring
        allParticipantIds.Remove(session.LecturerId);

        var result = new Dictionary<string, ParticipantScoreDetails>(allParticipantIds.Count);
        foreach (var participantId in allParticipantIds)
        {
            result[participantId] = CalculateScorePerParticipant(
                session,
                participantId,
                totalSessionMinutes,
                participantId  // Just use MatricNo, no need for name lookup
            );
        }

        return result;
    }

    public ParticipantScoreDetails CalculateScorePerParticipant(Session session, string participantId, double totalSessionMinutes, string participantName)
    {
        var details = new ParticipantScoreDetails
        {
            ParticipantId = participantId,
            TotalSessionMinutes = totalSessionMinutes,
            ParticipantName = participantName
        };

        Console.WriteLine($"CalculateScore: {participantId}, TotalSessionMinutes={totalSessionMinutes:F1}, StartTime={session.StartTime}");

        double totalCreditMinutes = 0;
        var sessionEnd = session.EndTime ?? DateTime.UtcNow; // Pure UTC
        Session.StudentStatus? previousStatus = null;

        if (!session.ParticipantEvents.TryGetValue(participantId, out var events) || !events.Any())
        {
            // No events: use last known status or default to Disconnected
            var status = session.ParticipantStatuses.TryGetValue(participantId, out var lastStatus)
                ? lastStatus
                : Session.StudentStatus.Disconnected;

            totalCreditMinutes = CalculateSegmentCredit(status, null, totalSessionMinutes);
            UpdateDurationForStatus(details, status, totalSessionMinutes);
            Console.WriteLine($"CalculateScore: {participantId} {status} for {totalSessionMinutes:F1} min, Credit={totalCreditMinutes:F1} min");
        }
        else
        {
            // Process events chronologically with correct timeline logic
            var sortedEvents = events.OrderBy(e => e.timeStamp).ToList();
            var currentTime = session.StartTime;

            // Handle time before first event (late join scenario)
            var firstEvent = sortedEvents[0];
            if (firstEvent.timeStamp > session.StartTime)
            {
                var duration = (firstEvent.timeStamp - session.StartTime).TotalMinutes;
                if (duration > 0)
                {
                    var credit = CalculateSegmentCredit(Session.StudentStatus.Disconnected, null, duration);
                    totalCreditMinutes += credit;
                    UpdateDurationForStatus(details, Session.StudentStatus.Disconnected, duration);
                    Console.WriteLine($"CalculateScore: {participantId} Disconnected (before join) for {duration:F1} min, Credit={credit:F1} min");
                }
                currentTime = firstEvent.timeStamp;
            }

            // Process each status change event
            for (int i = 0; i < sortedEvents.Count; i++)
            {
                var currentEvent = sortedEvents[i];
                var nextEventTime = i < sortedEvents.Count - 1 ? sortedEvents[i + 1].timeStamp : sessionEnd;

                // Calculate duration from this event until the next event (or session end)
                var duration = (nextEventTime - currentEvent.timeStamp).TotalMinutes;

                if (duration > 0.01)
                {
                    var credit = CalculateSegmentCredit(currentEvent.status, previousStatus, duration);
                    totalCreditMinutes += credit;
                    UpdateDurationForStatus(details, currentEvent.status, duration);
                    Console.WriteLine($"CalculateScore: {participantId} {currentEvent.status} for {duration:F1} min, Credit={credit:F1} min");
                }

                previousStatus = currentEvent.status;
            }
        }

        // Calculate final percentage
        details.FinalScorePercentage = totalSessionMinutes == 0 ? 0 : 
            Math.Round(Math.Clamp((totalCreditMinutes / totalSessionMinutes) * 100, 0, 100), 2);

        Console.WriteLine($"CalculateScore: {participantId} Final: TotalCredit={totalCreditMinutes:F1}, Possible={totalSessionMinutes:F1}, Percentage={details.FinalScorePercentage:F1}%");

        return details;
    }

    private double CalculateSegmentCredit(Session.StudentStatus currentStatus, Session.StudentStatus? previousStatus, double duration)
    {
        // Base credit calculation
        double baseCredit = currentStatus switch
        {
            Session.StudentStatus.Active => duration,           // 100% credit
            Session.StudentStatus.BatteryLow => 0,              // Warning only - no credit
            Session.StudentStatus.DataFinished => 0,            // Warning only - no credit
            Session.StudentStatus.InActive => 0,                // 0% credit normally
            Session.StudentStatus.Disconnected => 0,            // 0% credit normally
            _ => 0
        };

        // Apply 50% grace if current status follows a warning
        bool previousWasWarning = previousStatus is Session.StudentStatus.BatteryLow or Session.StudentStatus.DataFinished;

        if (previousWasWarning && (currentStatus == Session.StudentStatus.InActive || currentStatus == Session.StudentStatus.Disconnected))
        {
            var graceCredit = duration * 0.5; // 50% credit instead of 0%
            Console.WriteLine($"Grace applied: {currentStatus} after warning gets {graceCredit:F1} min credit (50% of {duration:F1} min)");
            return graceCredit;
        }

        return baseCredit;
    }

    private void UpdateDurationForStatus(ParticipantScoreDetails details, Session.StudentStatus status, double duration)
    {
        // Consider using ILogger for production instead of Console.WriteLine
        switch (status)
        {
            case Session.StudentStatus.Active:
                details.TimeActiveMinutes += duration;
                break;
            case Session.StudentStatus.InActive:
                details.TimeInactiveMinutes += duration;
                break;
            case Session.StudentStatus.BatteryLow:
                details.TimeBatteryLowMinutes += duration;
                break;
            case Session.StudentStatus.DataFinished:
                details.TimeDataFinishedMinutes += duration;
                break;
            case Session.StudentStatus.Disconnected:
                details.TimeDisconnectedMinutes += duration;
                break;
            default:
                break;
        }
    }

    // Add missing methods for page functionality
    public List<Session> GetAllSessions() =>
        _sessions.Values.ToList();

    public Session? GetSessionByCode(string code) =>
        _sessions.Values.FirstOrDefault(s => s.SessionId == code);

    public Session? GetSession(string sessionId) =>
        GetSessionById(sessionId);

    public void AddParticipant(string sessionId, string participantId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            if (!session.ParticipantIds.Contains(participantId))
            {
                session.ParticipantIds.Add(participantId);
                session.ParticipantStatuses[participantId] = Session.StudentStatus.Active;
            }
        }
    }
}