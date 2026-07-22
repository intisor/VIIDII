using VIIDII.Models;
using VIIDII.Services;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace VIIDII.Hubs
{
    public class SessionHub : Hub
    {
        private readonly MessageService _messageService;
        private readonly SessionService _sessionService;
        private readonly UserService _userService;
        private static readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
        private static readonly ConcurrentDictionary<string, DateTime> _pendingEngagementPrompts = new();

        public SessionHub(MessageService messageService, SessionService sessionService, UserService userService)
        {
            _messageService = messageService;
            _sessionService = sessionService;
            _userService = userService;
        }

        public async Task StartSession(string sessionId)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            await Clients.Group(sessionId).SendAsync("StartSession", sessionId);
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if(session != null)
            {
                if (await IsSessionLecturerAsync(sessionId,matricNo))
                {
                    session.LecturerConnectionId = Context.ConnectionId;
                    Console.WriteLine($"Lecturer {matricNo} set LecturerConnectionId: {session.LecturerConnectionId}");

                    // If session is already started, send current scores and statuses to lecturer
                    if (session.Status == SessionStatus.Started)
                    {
                        var currentScores = _sessionService.CalculateAttendanceScore(sessionId);
                        await Clients.Caller.SendAsync("ReceiveParticipantScoreDetails", currentScores);
                        var currentStatuses = _sessionService.GetParticipantStatus(sessionId);
                        await Clients.Caller.SendAsync("ReceiveParticipantStatuses", currentStatuses);
                        Console.WriteLine($"Sent current scores/statuses to reconnected lecturer {matricNo} for started session {sessionId}");
                    }
                }
                else
                {
                    var (joinedSession, error) = _sessionService.JoinSession(sessionId, matricNo,Context.ConnectionId);
                    if (joinedSession is null)
                    {
                        Console.WriteLine($"JoinSession failed: {error}");
                        return;
                    }
                    Console.WriteLine($"Student {matricNo} joined session {sessionId}, ParticipantIds: {string.Join(", ", session.ParticipantIds)}");
                }
                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                {
                    // Send participant names instead of just IDs
                    var participants = new Dictionary<string, string>();
                    foreach (var id in session.ParticipantIds)
                    {
                        var user = await _userService.GetUserByMatricNoAsync(id);
                        participants[id] = user?.Name ?? id;
                    }
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                    Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
                }
            }
        }

        public async Task JoinSession(string sessionId)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            Console.WriteLine($"[SessionHub] JoinSession called. SessionId={sessionId}, MatricNo={matricNo}, ConnectionId={Context.ConnectionId}");
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session == null || session.Status == SessionStatus.Ended)
            {
                Console.WriteLine($"JoinSession failed: Session {sessionId} not found.");
                return;
            }


            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            if (!await IsSessionLecturerAsync(sessionId, matricNo))
            {
                var (joinedSession, error) = _sessionService.JoinSession(sessionId, matricNo, Context.ConnectionId);
                if (joinedSession is null)
                {
                    Console.WriteLine($"JoinSession failed: {error}");
                    return;
                }
                Console.WriteLine($"Student {matricNo} joined session {sessionId}, ParticipantIds: {string.Join(", ", session.ParticipantIds)}");
            }

            if (session.IsSessionStarted)
            {
                await Clients.Caller.SendAsync("StartSession", sessionId);
                await Clients.Caller.SendAsync("SessionStarted", sessionId);
            }

            if (!string.IsNullOrEmpty(session.LecturerConnectionId))
            {
                // Send participant names instead of just IDs
                var participants = new Dictionary<string, string>();
                foreach (var id in session.ParticipantIds)
                {
                    var user = await _userService.GetUserByMatricNoAsync(id);
                    participants[id] = user?.Name ?? id;
                }

                Console.WriteLine($"[SessionHub] Sending ReceiveParticipants to lecturer connection {session.LecturerConnectionId} for session {sessionId}. Payload: {string.Join(", ", participants.Select(p => $"{p.Key}={p.Value}"))}");
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
            }
            else
            {
                Console.WriteLine($"[SessionHub] No lecturer connection registered for session {sessionId}; ReceiveParticipants not sent.");
            }
        }

        public async Task EndSession(string sessionId)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = await _sessionService.GetSessionByIdAsync(sessionId);

            if (session != null && await IsSessionLecturerAsync(sessionId, matricNo))
            {
                _sessionService.EndSession(sessionId, matricNo);

                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                {
                    var participants = new Dictionary<string, string>();
                    foreach (var id in session.ParticipantIds)
                    {
                        var user = await _userService.GetUserByMatricNoAsync(id);
                        participants[id] = user?.Name ?? id;
                    }

                    var statuses = _sessionService.GetParticipantStatus(sessionId);
                    var scores = await _sessionService.CalculateAttendanceScoreFromPersistenceAsync(sessionId);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                }

                // Notify all clients in the session that it has ended
                await Clients.Group(sessionId).SendAsync("SessionEnded", sessionId);
                Console.WriteLine($"Session {sessionId} ended by lecturer {matricNo}");
            }
        }

        public async Task SessionStarted(string sessionId)
        {
            await Clients.Group(sessionId).SendAsync("SessionStarted", sessionId); // Inform everyone

            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session != null && !string.IsNullOrEmpty(session.LecturerConnectionId) && session.Status == SessionStatus.Started)
            {
                // Send initial scores to the lecturer
                var scores = _sessionService.CalculateAttendanceScore(sessionId);
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                Console.WriteLine($"Sent initial scores to lecturer for session {sessionId}");

                // Also send initial statuses
                var statuses = _sessionService.GetParticipantStatus(sessionId);
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                Console.WriteLine($"Sent initial statuses to lecturer for session {sessionId}");
            }
        }
        public async Task NotifyStreamChange(string sessionId, string streamType)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            if (await IsSessionLecturerAsync(sessionId, matricNo))
            {
                await Clients.Group(sessionId).SendAsync("ReceiveStreamChange", streamType);
                Console.WriteLine($"Notified stream change ({streamType}) to session {sessionId}");
            }
            else
            {
                Console.WriteLine($"Unauthorized stream change attempt by {matricNo} in session {sessionId}");
            }
        }
        public Task SendMessage(string user, string message) => Clients.Others.SendAsync("ReceiveMessage", user, message);
        public async Task SendPeerId(string sessionId, string userId, string peerId)
        {
            await Clients.Group(sessionId).SendAsync("ReceivePeerId", userId, peerId);
        }

        public async Task CreatePost(string sessionId, string content, bool isFile)
        {
            try
            {
                var httpContext = Context.GetHttpContext();
                var matricNo = httpContext?.Session.GetString("MatricNo");
                
                if (string.IsNullOrEmpty(matricNo))
                {
                    await Clients.Caller.SendAsync("Error", "Session not found. Please log in again.");
                    return;
                }

                var user = await _userService.GetUserByMatricNoAsync(matricNo);
                var userName = user?.Name ?? matricNo;
                var post = await _messageService.CreatePostAsync(sessionId, matricNo, userName, content, true, isFile);
                
                await Clients.Group(sessionId).SendAsync("ReceivePost", post);
                await Clients.Caller.SendAsync("PostCreated", post.id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CreatePost] Error: {ex.Message}");
                await Clients.Caller.SendAsync("Error", "Failed to create post. Please try again.");
            }
        }

        public async Task CreateComment(string sessionId, string postId, string content)
        {
            try
            {
                var httpContext = Context.GetHttpContext();
                var matricNo = httpContext?.Session.GetString("MatricNo");
                
                if (string.IsNullOrEmpty(matricNo))
                {
                    await Clients.Caller.SendAsync("Error", "Session not found. Please log in again.");
                    return;
                }

                var user = await _userService.GetUserByMatricNoAsync(matricNo);
                var userName = user?.Name ?? matricNo;
                var isLecturer = await IsSessionLecturerAsync(sessionId, matricNo);
                var comment = await _messageService.CreateCommentAsync(sessionId, matricNo, userName, content, postId, isLecturer);
                
                await Clients.Group(sessionId).SendAsync("ReceiveComment", comment);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CreateComment] Error: {ex.Message}");
                await Clients.Caller.SendAsync("Error", "Failed to create comment. Please try again.");
            }
        }

        public async Task GetMessages(string sessionId)
        {
            var messages = await _messageService.GetAllMessagesAsync(sessionId);
            await Clients.Caller.SendAsync("ReceiveMessages", messages);
        }

        private async Task<bool> IsSessionLecturerAsync(string sessionId, string matricNo)
        {
            if (string.IsNullOrEmpty(matricNo))
            {
                return false;
            }
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            return session != null && session.LecturerMatricNo == matricNo;
        }

        public async Task UpdateTabStatus(bool isActive)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            if (session is not null && !await IsSessionLecturerAsync(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                var status = isActive ? Session.StudentStatus.Active : Session.StudentStatus.InActive;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, status))
                {
                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    {
                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);

                        // Also send updated scores
                        var scores = _sessionService.CalculateAttendanceScore(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                        Console.WriteLine($"Sent updated scores to lecturer after {matricNo} status changed to {status}");
                    }
                }
            }
        }

        public async Task FlagIssue(string issue)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            if (session is not null && !await IsSessionLecturerAsync(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                var status = issue == "BatteryLow" ? Session.StudentStatus.BatteryLow : Session.StudentStatus.DataFinished;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, status))
                {
                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    {
                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);

                        // Also send updated scores
                        var scores = _sessionService.CalculateAttendanceScore(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                        Console.WriteLine($"Sent updated scores to lecturer after {matricNo} flagged {issue}");
                    }
                }
            }
        }

        public async Task ConfirmActive()
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            if (session is not null && !await IsSessionLecturerAsync(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                _pendingEngagementPrompts.TryRemove(matricNo, out _);
                _lastSeen[matricNo] = DateTime.UtcNow;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.Active))
                {
                    _lastSeen[matricNo] = DateTime.UtcNow; // Keep this logic for ConfirmActive
                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    {
                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);

                        // Also send updated scores
                        var scores = _sessionService.CalculateAttendanceScore(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                        Console.WriteLine($"Sent updated scores to lecturer after {matricNo} confirmed active");
                    }
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            if (!string.IsNullOrEmpty(matricNo))
            {
                var session = await _sessionService.GetSessionByParticipantAsync(matricNo);
                if (session is not null && !await IsSessionLecturerAsync(session.SessionId, matricNo))
                {
                    session.ParticipantConnectionIds.Remove(matricNo);

                    if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.Disconnected))
                    {
                        if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                        {
                            var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                            await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);

                            // Also send updated scores
                            var scores = _sessionService.CalculateAttendanceScore(session.SessionId);
                            await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                            Console.WriteLine($"Sent updated scores to lecturer after {matricNo} disconnected");
                        }
                    }

                    _sessionService.LeaveSession(session.SessionId, matricNo);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
        public static bool TryGetLastSeen(string participantId, out DateTime lastSeen) =>
            _lastSeen.TryGetValue(participantId, out lastSeen);

        public static void RegisterEngagementPrompt(string participantId, DateTime promptedAt) =>
            _pendingEngagementPrompts[participantId] = promptedAt;

        public static bool TryGetPendingEngagementPrompt(string participantId, out DateTime promptedAt) =>
            _pendingEngagementPrompts.TryGetValue(participantId, out promptedAt);

        public static void ClearPendingEngagementPrompt(string participantId) =>
            _pendingEngagementPrompts.TryRemove(participantId, out _);

        // Messaging - Reaction Methods
        public async Task AddReaction(string sessionId, string messageId, string emoji)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            if (string.IsNullOrEmpty(matricNo)) return;

            var success = await _messageService.AddReactionAsync(sessionId, messageId, matricNo, emoji);
            if (success)
            {
                // Broadcast reaction to all in session
                await Clients.Group(sessionId).SendAsync("ReceiveReaction", messageId, matricNo, emoji, true);
                Console.WriteLine($"Reaction added: {matricNo} reacted {emoji} to message {messageId}");
            }
        }

        public async Task RemoveReaction(string sessionId, string messageId, string emoji)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            if (string.IsNullOrEmpty(matricNo)) return;

            var success = await _messageService.RemoveReactionAsync(sessionId, messageId, matricNo, emoji);
            if (success)
            {
                // Broadcast reaction removal to all in session
                await Clients.Group(sessionId).SendAsync("ReceiveReaction", messageId, matricNo, emoji, false);
                Console.WriteLine($"Reaction removed: {matricNo} unreacted {emoji} from message {messageId}");
            }
        }

        // Engagement - Prompt Methods
        public async Task PromptEngagement(string sessionId)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = await _sessionService.GetSessionByIdAsync(sessionId);

            // Only lecturer can prompt engagement
            if (session != null && await IsSessionLecturerAsync(sessionId, matricNo))
            {
                var promptedConnectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Console.WriteLine($"[SessionHub] Lecturer {matricNo} manually prompted engagement for session {sessionId}");

                foreach (var participantId in session.ParticipantIds)
                {
                    if (_sessionService.IsLecturer(participantId))
                    {
                        continue;
                    }

                    if (session.ParticipantConnectionIds.TryGetValue(participantId, out var connectionId) &&
                        !string.IsNullOrWhiteSpace(connectionId))
                    {
                        RegisterEngagementPrompt(participantId, DateTime.UtcNow);
                        promptedConnectionIds.Add(connectionId);
                        await Clients.Client(connectionId).SendAsync("AreYouThere");
                        Console.WriteLine($"[SessionHub] Sent direct AreYouThere to participant {participantId} via connection {connectionId}");
                    }
                    else
                    {
                        Console.WriteLine($"[SessionHub] No direct connection for participant {participantId}; fallback broadcast will be used");
                    }
                }

                await Clients.GroupExcept(sessionId, promptedConnectionIds.ToArray()).SendAsync("AreYouThere");
                Console.WriteLine($"[SessionHub] Fallback broadcast sent to session {sessionId} for {promptedConnectionIds.Count} direct recipient(s)");
            }
        }
    }
}