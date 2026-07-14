using VIIDII.Models;               
using System.Collections.Concurrent;

namespace VIIDII.Services;

public class SessionService
{
    private readonly ConcurrentDictionary<string, Session> _sessions = [];
    private static Dictionary<string, string> _userDetailsCache = new(); // Static cache for user details
    private readonly IServiceProvider _serviceProvider;

    public SessionService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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
    public Session CreateSession(string lecturerId, string title, List<User.Departments> allowedDepartments, List<User.Levels> allowedLevels, bool? replaceExisting = false)
    {
        var lecturers = MockApiService.GetLecturers();
        if (!lecturers.Any(l => l.MatricNo == lecturerId))
            return null;

        var existingSession = _sessions.Values
            .FirstOrDefault(s => s.LecturerMatricNo == lecturerId && s.Status == SessionStatus.Active && s.ExpiresAt > DateTime.UtcNow);

        if (existingSession != null)
        {
            if (replaceExisting == null || replaceExisting == false)
                return existingSession;
            _sessions.TryRemove(existingSession.SessionId, out _);
        }

        var session = new Session
        {
            SessionId = GenerateSessionCode(),
            LecturerMatricNo = lecturerId,
            Title = title,
            AllowedDepartments = allowedDepartments ?? new List<User.Departments>(),
            AllowedLevels = allowedLevels ?? new List<User.Levels>(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        session.AllowedDepartments = session.AllowedDepartments.Contains(User.Departments.Any) ? [.. Enum.GetValues<User.Departments>()] : session.AllowedDepartments;

        if (session.AllowedLevels.Contains(User.Levels.Any))
        {
            session.AllowedLevels = Enum.GetValues(typeof(User.Levels)).Cast<User.Levels>().ToList();
        }

        session.Status = SessionStatus.Active;
        _sessions.TryAdd(session.SessionId, session);

        // Persist to database asynchronously (fire and forget)
        _ = PersistSessionCreationAsync(lecturerId, title, allowedDepartments, allowedLevels);

        return session;
    }

    private static string GenerateSessionCode() => $"{DateTime.UtcNow.AddHours(1):yyyyMMdd}-{string.Concat(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))))}";

    public Session LeaveSession(string sessionId, string participantId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status != SessionStatus.Active)
            return null;
        if (!session.ParticipantIds.Contains(participantId))
            return null;
        session.ParticipantIds.Remove(participantId);
        session.ParticipantConnectionIds.Remove(participantId);

        _ = PersistParticipantLeaveAsync(sessionId, participantId);
        return session;
    }
    public (Session Session, string? Error) JoinSession(string sessionId, string participantId, string? connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status == SessionStatus.Ended || session.ExpiresAt < DateTime.UtcNow)
            return (null, "Session not found, inactive, or expired.");
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
                session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>();
            }
            var joinTime = DateTime.UtcNow.AddHours(1);
            if (joinTime > session.StartTime)
            {
                session.ParticipantEvents[participantId].Add((Session.StudentStatus.Disconnected, session.StartTime));
                _ = PersistAttendanceStatusAsync(sessionId, participantId, Session.StudentStatus.Disconnected, session.StartTime);
                Console.WriteLine($"JoinSession: {participantId} absent before joining, Disconnected at {session.StartTime}, Active at {joinTime}");
            }
            session.ParticipantEvents[participantId].Add((Session.StudentStatus.Active, joinTime));
            _ = PersistAttendanceStatusAsync(sessionId, participantId, Session.StudentStatus.Active, joinTime);
            Console.WriteLine($"Logged Active event for {participantId} joining started session at: {joinTime}");
        }

        // Persist participant to database asynchronously
        _ = PersistParticipantJoinAsync(sessionId, participantId);

        return (session, null);
    }   
    public Session EndSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;
        return EndSession(sessionId, session.LecturerMatricNo);
    }

    public Session EndSession(string sessionId, string lecturerId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;
        if (session.LecturerMatricNo != lecturerId || (session.Status != SessionStatus.Started && session.Status != SessionStatus.Active))
            return null;
        var endedAt = DateTime.UtcNow;
        session.Status = SessionStatus.Ended;
        session.EndTime = endedAt;
        session.IsSessionStarted = false;

        // Persist session end to database asynchronously
        _ = PersistSessionEndAsync(sessionId, lecturerId);

        return session;
    }
    public async Task<Session?> StartSessionAsync(string sessionId)
    {
        var session = await GetSessionByIdAsync(sessionId);
        if (session != null && session.Status != SessionStatus.Started) // Ensure it's not already started
        {
            session.IsSessionStarted = true;
            session.Status = SessionStatus.Started;
            session.StartTime = DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
            Console.WriteLine($"[SessionService C#] StartSession: Session {sessionId} started at {session.StartTime}, Participants: {string.Join(", ", session.ParticipantIds)}");
            // Log initial 'Active' status for all current participants
            foreach (var participantId in session.ParticipantIds.ToList()) // ToList to avoid modification issues if any
            {
                if (!session.ParticipantEvents.ContainsKey(participantId))
                {
                    session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>(); // Changed DateTimeOffset to DateTime
                }
                // Add initial active event at session start time
                session.ParticipantEvents[participantId].Add((Session.StudentStatus.Active, session.StartTime));
                session.ParticipantStatuses[participantId] = Session.StudentStatus.Active; // Ensure current status is active
                _ = PersistAttendanceStatusAsync(sessionId, participantId, Session.StudentStatus.Active, session.StartTime);
                Console.WriteLine($"[SessionService C#] Logged initial Active event for {participantId} at session start: {session.StartTime}");
            }

            // Persist session start to database
            await PersistSessionStartAsync(sessionId);
        }
        return session;
    }

    public void RemoveSessionsByLecturer(string lecturerId)
    {
        var sessionsToRemove = _sessions.Values
            .Where(s => s.LecturerMatricNo == lecturerId)
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
                session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>(); // Changed DateTimeOffset to DateTime
            }
            var eventTimestamp = DateTime.UtcNow.AddHours(1); // Changed DateTimeOffset to DateTime
            session.ParticipantEvents[participantId].Add((status, eventTimestamp));
            _ = PersistAttendanceStatusAsync(sessionId, participantId, status, eventTimestamp);
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

    public async Task<Session?> GetSessionByIdAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            // Check if session has expired
            if (session.ExpiresAt < DateTime.UtcNow)
            {
                Console.WriteLine($"[SessionService C#] Session {sessionId} has expired. Removing from cache.");
                _sessions.TryRemove(sessionId, out _);
                return null;
            }
            Console.WriteLine($"[SessionService C#] Found session {sessionId} in-memory cache.");
            return session;
        }

        Console.WriteLine($"[SessionService C#] Session {sessionId} NOT found in-memory. Querying SQL database...");
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var dbSession = await persistenceService.GetSessionWithParticipantsAsync(sessionId);
            if (dbSession != null)
            {
                // Check if database session has expired
                if (dbSession.ExpiresAt < DateTime.UtcNow)
                {
                    Console.WriteLine($"[SessionService C#] Session {sessionId} from database has expired.");
                    return null;
                }
                // Reconstruct standard collections to avoid null references
                dbSession.ParticipantIds ??= new HashSet<string>();
                dbSession.ParticipantStatuses ??= new Dictionary<string, Session.StudentStatus>();
                dbSession.ParticipantConnectionIds ??= new Dictionary<string, string>();
                dbSession.ParticipantEvents ??= new Dictionary<string, List<(Session.StudentStatus, DateTime)>>();

                // Populate ParticipantIds from database relationship
                if (dbSession.Participants != null)
                {
                    foreach (var sp in dbSession.Participants)
                    {
                        if (sp.User != null && !string.IsNullOrEmpty(sp.User.MatricNo) && sp.LeftAt == null)
                        {
                            dbSession.ParticipantIds.Add(sp.User.MatricNo);
                            if (!dbSession.ParticipantStatuses.ContainsKey(sp.User.MatricNo))
                            {
                                dbSession.ParticipantStatuses[sp.User.MatricNo] = Session.StudentStatus.Active;
                            }
                        }
                    }
                }

                await LoadAttendanceStateAsync(dbSession);

                _sessions.TryAdd(sessionId, dbSession);
                Console.WriteLine($"[SessionService C#] Reconstructed active session {sessionId} from DB into memory cache with {dbSession.ParticipantIds.Count} participants.");
                return dbSession;
            }
            else
            {
                Console.WriteLine($"[SessionService C#] Session {sessionId} not found in SQL database.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService C#] Exception querying database for session {sessionId}: {ex.Message}");
        }

        return null;
    }

    public async Task<Session?> GetSessionByDbIdAsync(int sessionDbId)
    {
        if (sessionDbId <= 0)
        {
            return null;
        }

        var cachedSession = _sessions.Values.FirstOrDefault(s => s.Id == sessionDbId);
        if (cachedSession != null)
        {
            return cachedSession;
        }

        using var scope = _serviceProvider.CreateScope();
        var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
        var dbSession = await persistenceService.GetSessionByDatabaseIdAsync(sessionDbId);
        if (dbSession == null)
        {
            return null;
        }

        return await GetSessionByIdAsync(dbSession.SessionId);
    }

    public List<Session> GetSessionsByLecturer(string lecturerId) =>
        _sessions.Values
            .Where(s => s.LecturerMatricNo == lecturerId 
                && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Started)
                && s.ExpiresAt > DateTime.UtcNow)
            .ToList();

    public async Task<List<Session>> GetSessionsByLecturerAsync(string lecturerId)
    {
        Console.WriteLine($"[SessionService C#] GetSessionsByLecturerAsync for lecturer {lecturerId}");
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var dbSessions = await persistenceService.GetSessionsByLecturerAsync(lecturerId);
            
            var result = new List<Session>();
            foreach (var dbSession in dbSessions)
            {
                var memorySession = await GetSessionByIdAsync(dbSession.SessionId);
                if (memorySession != null)
                {
                    result.Add(memorySession);
                }
            }
            return result;
        }

        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService C#] Exception in GetSessionsByLecturerAsync: {ex.Message}. Falling back to in-memory check.");
            return GetSessionsByLecturer(lecturerId);
        }
    }

    public Session GetSessionByParticipant(string participantId) =>
        _sessions.Values
            .FirstOrDefault(s =>
                (s.Status == SessionStatus.Started || s.Status == SessionStatus.Active)
                && s.ParticipantIds.Contains(participantId)
                && s.ExpiresAt > DateTime.UtcNow);

    public async Task<Session?> GetSessionByParticipantAsync(string participantId)
    {
        Console.WriteLine($"[SessionService C#] GetSessionByParticipantAsync for student {participantId}");
        var session = _sessions.Values
            .FirstOrDefault(s =>
                (s.Status == SessionStatus.Started || s.Status == SessionStatus.Active)
                && s.ParticipantIds.Contains(participantId)
                && s.ExpiresAt > DateTime.UtcNow);

        if (session != null) return session;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var dbSession = await persistenceService.GetSessionByParticipantAsync(participantId);
            if (dbSession != null)
            {
                return await GetSessionByIdAsync(dbSession.SessionId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService C#] Exception in GetSessionByParticipantAsync: {ex.Message}");
        }

        return null;
    }

    public List<Session> GetSessionsBy<TKey>(TKey key, Func<Session, TKey> selector) =>
        _sessions.Values
            .Where(s => Equals(selector(s), key))
            .ToList();

    public List<Session> GetActiveSessions() =>
        _sessions.Values
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Started)
            .ToList();

    public async Task<List<Session>> GetActiveSessionsAsync()
    {
        Console.WriteLine("[SessionService C#] GetActiveSessionsAsync requested");
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var dbSessions = await persistenceService.GetActiveSessionsAsync();
            
            var result = new List<Session>();
            foreach (var dbSession in dbSessions)
            {
                var memorySession = await GetSessionByIdAsync(dbSession.SessionId);
                if (memorySession != null)
                {
                    result.Add(memorySession);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService C#] Exception in GetActiveSessionsAsync: {ex.Message}. Falling back to in-memory check.");
            return GetActiveSessions();
        }
    }

    public async Task<Dictionary<string, ParticipantScoreDetails>> CalculateAttendanceScoreFromPersistenceAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new Dictionary<string, ParticipantScoreDetails>();
        }

        using var scope = _serviceProvider.CreateScope();
        var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
        var session = await persistenceService.GetSessionWithParticipantsAsync(sessionId);
        if (session == null)
        {
            return new Dictionary<string, ParticipantScoreDetails>();
        }

        var endTime = session.EndTime ?? DateTime.UtcNow;
        var totalSessionMinutes = (endTime - session.StartTime).TotalMinutes;
        if (totalSessionMinutes <= 0)
        {
            return new Dictionary<string, ParticipantScoreDetails>();
        }

        var attendanceLogs = await persistenceService.GetAttendanceLogsAsync(sessionId);
        var participantNames = session.Participants?
            .Where(participant => participant.User != null && !string.IsNullOrWhiteSpace(participant.User.MatricNo))
            .ToDictionary(participant => participant.User.MatricNo, participant => participant.User.Name, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var logsByParticipant = attendanceLogs
            .Where(log => log.Student != null && !string.IsNullOrWhiteSpace(log.Student.MatricNo))
            .GroupBy(log => log.Student.MatricNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(log => log.Timestamp).ToList(), StringComparer.OrdinalIgnoreCase);

        var participantIds = new HashSet<string>(participantNames.Keys, StringComparer.OrdinalIgnoreCase);
        participantIds.UnionWith(logsByParticipant.Keys);

        var result = new Dictionary<string, ParticipantScoreDetails>(participantIds.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var participantId in participantIds)
        {
            logsByParticipant.TryGetValue(participantId, out var participantLogs);
            result[participantId] = CalculateScorePerParticipantFromLogs(
                session,
                participantId,
                participantNames.GetValueOrDefault(participantId, "Unknown User"),
                totalSessionMinutes,
                participantLogs ?? []);
        }

        return result;
    }

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

        // Prefer persisted participant identities for reconstructed sessions before falling back to legacy mock data.
        var participantNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (session.Participants != null)
        {
            foreach (var participant in session.Participants)
            {
                if (participant.User != null && !string.IsNullOrWhiteSpace(participant.User.MatricNo))
                {
                    participantNames[participant.User.MatricNo] = participant.User.Name;
                }
            }
        }

        _userDetailsCache ??= MockApiService.GetUsers().ToDictionary(u => u.MatricNo, u => u.Name);

        // Single-pass participant ID collection, including past participants with events/statuses
        var allParticipantIds = new HashSet<string>(session.ParticipantIds);
        allParticipantIds.UnionWith(session.ParticipantEvents.Keys);
        allParticipantIds.UnionWith(session.ParticipantStatuses.Keys);

        var result = new Dictionary<string, ParticipantScoreDetails>(allParticipantIds.Count);
        foreach (var participantId in allParticipantIds)
        {
            result[participantId] = CalculateScorePerParticipant(
                session,
                participantId,
                totalSessionMinutes,
                participantNames.GetValueOrDefault(participantId, _userDetailsCache.GetValueOrDefault(participantId, "Unknown User"))
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
        var sessionEnd = session.EndTime ?? DateTime.UtcNow.AddHours(1);
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

    private ParticipantScoreDetails CalculateScorePerParticipantFromLogs(
        Session session,
        string participantId,
        string participantName,
        double totalSessionMinutes,
        List<AttendanceLog> logs)
    {
        var details = new ParticipantScoreDetails
        {
            ParticipantId = participantId,
            ParticipantName = participantName,
            TotalSessionMinutes = totalSessionMinutes
        };

        if (logs.Count == 0)
        {
            UpdateDurationForStatus(details, Session.StudentStatus.Disconnected, totalSessionMinutes);
            return details;
        }

        var sessionEnd = session.EndTime ?? DateTime.UtcNow;
        double totalCreditMinutes = 0;
        Session.StudentStatus? previousStatus = null;

        var firstLog = logs[0];
        if (firstLog.Timestamp > session.StartTime)
        {
            var preJoinDuration = (firstLog.Timestamp - session.StartTime).TotalMinutes;
            if (preJoinDuration > 0)
            {
                UpdateDurationForStatus(details, Session.StudentStatus.Disconnected, preJoinDuration);
            }
        }

        for (var i = 0; i < logs.Count; i++)
        {
            var currentLog = logs[i];
            var duration = currentLog.Duration > TimeSpan.Zero
                ? currentLog.Duration.TotalMinutes
                : ((i < logs.Count - 1 ? logs[i + 1].Timestamp : sessionEnd) - currentLog.Timestamp).TotalMinutes;
            if (duration <= 0.01)
            {
                previousStatus = currentLog.Status;
                continue;
            }

            totalCreditMinutes += CalculateSegmentCredit(currentLog.Status, previousStatus, duration);
            UpdateDurationForStatus(details, currentLog.Status, duration);
            previousStatus = currentLog.Status;
        }

        details.FinalScorePercentage = Math.Round(Math.Clamp((totalCreditMinutes / totalSessionMinutes) * 100, 0, 100), 2);
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

    // Persistence helper methods
    private async Task PersistSessionCreationAsync(string lecturerId, string title, List<User.Departments> allowedDepartments, List<User.Levels> allowedLevels)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            await persistenceService.CreateAndPersistSessionAsync(lecturerId, title, allowedDepartments, allowedLevels);
            Console.WriteLine($"[SessionService] Session '{title}' persisted for lecturer {lecturerId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error persisting session creation: {ex.Message}");
        }
    }

    private async Task PersistParticipantJoinAsync(string sessionId, string participantMatricNo)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var success = await persistenceService.AddParticipantAsync(sessionId, participantMatricNo);
            if (success)
                Console.WriteLine($"[SessionService] Participant {participantMatricNo} persisted in session {sessionId}");
            else
                Console.WriteLine($"[SessionService] Failed to persist participant {participantMatricNo} in session {sessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error persisting participant join: {ex.Message}");
        }
    }

    private async Task PersistSessionEndAsync(string sessionId, string lecturerId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            await persistenceService.EndAndPersistSessionAsync(sessionId, lecturerId);
            Console.WriteLine($"[SessionService] Session {sessionId} end state persisted");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error persisting session end: {ex.Message}");
        }
    }

    private async Task PersistSessionStartAsync(string sessionId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            await persistenceService.StartAndPersistSessionAsync(sessionId);
            Console.WriteLine($"[SessionService] Session {sessionId} start state persisted");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error persisting session start: {ex.Message}");
        }
    }

    private async Task PersistParticipantLeaveAsync(string sessionId, string participantMatricNo)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var success = await persistenceService.RemoveParticipantAsync(sessionId, participantMatricNo);
            if (success)
            {
                Console.WriteLine($"[SessionService] Participant {participantMatricNo} leave persisted in session {sessionId}");
            }
            else
            {
                Console.WriteLine($"[SessionService] Failed to persist participant leave for {participantMatricNo} in session {sessionId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error persisting participant leave: {ex.Message}");
        }
    }

    private async Task PersistAttendanceStatusAsync(string sessionId, string participantMatricNo, Session.StudentStatus status, DateTime timestamp)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            await persistenceService.LogAttendanceStatusAsync(sessionId, participantMatricNo, status, timestamp);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error persisting attendance status: {ex.Message}");
        }
    }

    private async Task LoadAttendanceStateAsync(Session session)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<Data.SessionPersistenceService>();
            var attendanceLogs = await persistenceService.GetAttendanceLogsAsync(session.SessionId);

            foreach (var log in attendanceLogs.OrderBy(log => log.Timestamp))
            {
                var participantId = log.Student?.MatricNo;
                if (string.IsNullOrEmpty(participantId))
                {
                    continue;
                }

                if (!session.ParticipantEvents.ContainsKey(participantId))
                {
                    session.ParticipantEvents[participantId] = new List<(Session.StudentStatus status, DateTime timeStamp)>();
                }

                session.ParticipantEvents[participantId].Add((log.Status, log.Timestamp));
                session.ParticipantStatuses[participantId] = log.Status;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionService] Error loading attendance state: {ex.Message}");
        }
    }
}