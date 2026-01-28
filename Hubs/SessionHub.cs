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
        private readonly AuthService _authService;
        private static readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
        // Store MatricNo per connection ID (replaces HTTP session which doesn't work with Blazor Server)
        private static readonly ConcurrentDictionary<string, string> _connectionMatricNos = new();

        public SessionHub(MessageService messageService, SessionService sessionService, AuthService authService)
        {
            _messageService = messageService;
            _sessionService = sessionService;
            _authService = authService;
        }

        public override async Task OnConnectedAsync()
        {
            // Note: AuthService might not be initialized yet when SignalR connects
            // This is normal for Blazor Server - authentication happens in the component
            // We'll get the MatricNo later when hub methods are called
            Console.WriteLine($"[SessionHub] New connection: {Context.ConnectionId}");
            
            await base.OnConnectedAsync();
        }

        private async Task<string?> GetMatricNoForConnectionAsync()
        {
            // First check if we already stored it for this connection
            if (_connectionMatricNos.TryGetValue(Context.ConnectionId, out var matricNo))
            {
                return matricNo;
            }
            
            // Try to get from AuthService (must already be initialized by component)
            // Don't call InitializeAsync here - JS interop not available in hub context!
            matricNo = _authService.GetCurrentMatricNo();
            
            if (!string.IsNullOrEmpty(matricNo))
            {
                _connectionMatricNos[Context.ConnectionId] = matricNo;
                Console.WriteLine($"[SessionHub] Mapped connection {Context.ConnectionId} to MatricNo: {matricNo}");
                return matricNo;
            }
            
            // If still null, component hasn't initialized yet - wait a moment and retry once
            Console.WriteLine($"[SessionHub] MatricNo not available yet for {Context.ConnectionId}, waiting for component initialization...");
            await Task.Delay(100); // Brief delay for component to initialize
            
            matricNo = _authService.GetCurrentMatricNo();
            if (!string.IsNullOrEmpty(matricNo))
            {
                _connectionMatricNos[Context.ConnectionId] = matricNo;
                Console.WriteLine($"[SessionHub] Mapped connection {Context.ConnectionId} to MatricNo: {matricNo} (after retry)");
                return matricNo;
            }
            
            Console.WriteLine($"[SessionHub] Warning: Could not get MatricNo for connection {Context.ConnectionId}");
            return null;
        }

        // Synchronous version for places that can't be async
        private string? GetMatricNoForConnection()
        {
            if (_connectionMatricNos.TryGetValue(Context.ConnectionId, out var matricNo))
            {
                return matricNo;
            }
            
            // Fallback: try to get from AuthService (without initialization)
            matricNo = _authService.GetCurrentMatricNo();
            if (!string.IsNullOrEmpty(matricNo))
            {
                _connectionMatricNos[Context.ConnectionId] = matricNo;
                return matricNo;
            }
            
            return null;
        }

        public async Task StartSession(string sessionId, string matricNo)
        {
            // Cache the MatricNo for this connection immediately
            if (!string.IsNullOrEmpty(matricNo))
            {
                _connectionMatricNos[Context.ConnectionId] = matricNo;
                Console.WriteLine($"[SessionHub] Cached MatricNo {matricNo} for connection {Context.ConnectionId}");
            }
            
            // Now get it (will be from cache we just set)
            var userMatricNo = await GetMatricNoForConnectionAsync();
            
            if (string.IsNullOrEmpty(userMatricNo))
            {
                Console.WriteLine($"[SessionHub] StartSession failed: MatricNo not found for connection {Context.ConnectionId}");
                await Clients.Caller.SendAsync("Error", "Session expired. Please log in again.");
                return;
            }
            
            Console.WriteLine($"[SessionHub] StartSession called by {userMatricNo} for session {sessionId}");
            
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            var session = _sessionService.GetSessionById(sessionId);
            
            if(session != null)
            {
                if (IsSessionLecturer(sessionId, userMatricNo))
                {
                    // CRITICAL: Set LecturerConnectionId BEFORE broadcasting
                    session.LecturerConnectionId = Context.ConnectionId;
                    Console.WriteLine($"Lecturer {userMatricNo} set LecturerConnectionId: {session.LecturerConnectionId}");

                    // NOW broadcast StartSession to all (lecturer will handle this in JS)
                    await Clients.Group(sessionId).SendAsync("StartSession", sessionId);

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
                    var (joinedSession, error) = _sessionService.JoinSession(sessionId, userMatricNo, Context.ConnectionId);
                    if (joinedSession is null)
                    {
                        Console.WriteLine($"JoinSession failed: {error}");
                        return;
                    }
                    // Refresh session to get updated participant list
                    session = joinedSession;
                    Console.WriteLine($"Student {userMatricNo} joined session {sessionId}, ParticipantIds: {string.Join(", ", session.ParticipantIds)}");
                }
                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                {
                    // Send participant names instead of just IDs
                    var participants = session.ParticipantIds.ToDictionary(id => id, id => MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == id)?.Name ?? id);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                    Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
                }
            }
        }

        public async Task JoinSession(string sessionId)
        {
            var matricNo = await GetMatricNoForConnectionAsync();
            
            if (string.IsNullOrEmpty(matricNo))
            {
                Console.WriteLine($"[SessionHub] JoinSession failed: MatricNo not found for connection {Context.ConnectionId}");
                await Clients.Caller.SendAsync("Error", "Session expired. Please log in again.");
                return;
            }
            
            Console.WriteLine($"[SessionHub] JoinSession called by {matricNo} for session {sessionId}");
            
            var session = _sessionService.GetSessionById(sessionId);
            if (session == null || session.Status == SessionStatus.Ended)
            {
                Console.WriteLine($"JoinSession failed: Session {sessionId} not found.");
                return;
            }


            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            if (!IsSessionLecturer(sessionId, matricNo))
            {
                var (joinedSession, error) = _sessionService.JoinSession(sessionId, matricNo, Context.ConnectionId);
                if (joinedSession is null)
                {
                    Console.WriteLine($"JoinSession failed: {error}");
                    return;
                }
                // Refresh session to get updated participant list
                session = joinedSession;
                Console.WriteLine($"Student {matricNo} joined session {sessionId}, ParticipantIds: {string.Join(", ", session.ParticipantIds)}");
            }

            if (session.IsSessionStarted)
            {
                await Clients.Caller.SendAsync("StartSession", sessionId);
                await Clients.Caller.SendAsync("SessionStarted", sessionId);
            }

            if (!string.IsNullOrEmpty(session.LecturerConnectionId))
            {
                // Send participant names instead of just IDs (consistent with StartSession)
                var participants = session.ParticipantIds.ToDictionary(id => id, id => MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == id)?.Name ?? id);
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
            }
        }

        public async Task EndSession(string sessionId)
        {
            var matricNo = await GetMatricNoForConnectionAsync();
            var session = _sessionService.GetSessionById(sessionId);

            if (session != null && IsSessionLecturer(sessionId, matricNo))
            {
                _sessionService.EndSession(sessionId, matricNo);
                // Notify all clients in the session that it has ended
                await Clients.Group(sessionId).SendAsync("SessionEnded", sessionId);
                Console.WriteLine($"Session {sessionId} ended by lecturer {matricNo}");
            }
        }

        public async Task SessionStarted(string sessionId)
        {
            await Clients.Group(sessionId).SendAsync("SessionStarted", sessionId); // Inform everyone

            var session = _sessionService.GetSessionById(sessionId);
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
            var matricNo = await GetMatricNoForConnectionAsync();
            if (IsSessionLecturer(sessionId, matricNo))
            {
                await Clients.Group(sessionId).SendAsync("ReceiveStreamChange", streamType);
                Console.WriteLine($"Notified stream change ({streamType}) to session {sessionId}");
            }
            else
            {
                Console.WriteLine($"Unauthorized stream change attempt by {matricNo} in session {sessionId}");
            }
        }
        public async Task SendPeerId(string sessionId, string peerId)
        {
            var userId = await GetMatricNoForConnectionAsync();

            // Only send to others in the group (not back to sender)
            // This prevents the lecturer from receiving their own peer ID
            await Clients.OthersInGroup(sessionId).SendAsync("ReceivePeerId", userId, peerId);
            Console.WriteLine($"Sent peer ID {peerId} for user {userId} to others in session {sessionId}");
        }

        public async Task CreatePost(string sessionId, string content, bool isFile)
        {
            var matricNo = await GetMatricNoForConnectionAsync();
            
            if (string.IsNullOrEmpty(matricNo))
            {
                await Clients.Caller.SendAsync("Error", "Session expired. Please log in again.");
                return;
            }
            
            var user = MockApiService.GetUsers().FirstOrDefault(s => s.MatricNo == matricNo);
            if (user == null)
            {
                await Clients.Caller.SendAsync("Error", "User not found.");
                return;
            }
            
            var userName = user.Name;
            var post = _messageService.CreatePost(sessionId, matricNo, userName, content, true, isFile);
            await Clients.Group(sessionId).SendAsync("ReceivePost", post); // Changed from Clients.Others to Clients.Group(sessionId)
            // Optionally, PostCreated can still be sent if the caller needs specific confirmation beyond receiving the post itself.
            // For now, let's assume ReceivePost is sufficient for the caller to see their own post.
            // If specific UI updates are needed only for the caller upon their post creation (e.g. clearing input), PostCreated can be kept.
            // Let's keep PostCreated for now, as it might be used for UI cues like clearing the input field or showing a 'sent' status.
            await Clients.Caller.SendAsync("PostCreated", post.id);
        }

        public async Task CreateComment(string sessionId, string postId, string content)
        {
            var matricNo = await GetMatricNoForConnectionAsync();
            
            if (string.IsNullOrEmpty(matricNo))
            {
                await Clients.Caller.SendAsync("Error", "Session expired. Please log in again.");
                return;
            }
            
            var user = MockApiService.GetUsers().FirstOrDefault(s => s.MatricNo == matricNo);
            if (user == null)
            {
                await Clients.Caller.SendAsync("Error", "User not found.");
                return;
            }
            
            var userName = user.Name;
            var isLecturer = IsSessionLecturer(sessionId, matricNo);
            var comment = _messageService.CreateComment(sessionId, matricNo, userName, content, postId, isLecturer);
            await Clients.Group(sessionId).SendAsync("ReceiveComment", comment);
        }

        public async Task GetMessages(string sessionId)
        {
            var messages = _messageService.GetAllMessages(sessionId);
            await Clients.Caller.SendAsync("ReceiveMessages", messages);
        }

        private bool IsSessionLecturer(string sessionId, string matricNo)
        {
            if (string.IsNullOrEmpty(matricNo))
            {
                return false;
            }
            var session = _sessionService.GetSessionById(sessionId);
            return session != null && session.LecturerId == matricNo;
        }

        public async Task UpdateTabStatus(bool isActive)
        {
            var matricNo = await GetMatricNoForConnectionAsync();
            var session = _sessionService.GetSessionByParticipant(matricNo);
            if (session is not null && !IsSessionLecturer(session.SessionId, matricNo) && session.IsSessionStarted)
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
            var matricNo = await GetMatricNoForConnectionAsync();
            var session = _sessionService.GetSessionByParticipant(matricNo);
            if (session is not null && !IsSessionLecturer(session.SessionId, matricNo) && session.IsSessionStarted)
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
            var matricNo = await GetMatricNoForConnectionAsync();
            var session = _sessionService.GetSessionByParticipant(matricNo);
            if (session is not null && !IsSessionLecturer(session.SessionId, matricNo) && session.IsSessionStarted)
            {
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

        /// <summary>
        /// Keep-alive method to prevent circuit timeout
        /// Called periodically by client to maintain connection
        /// </summary>
        public Task KeepAlive()
        {
            // Just receiving this message keeps the circuit alive
            // No need to do anything else
            return Task.CompletedTask;
        }


        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Get MatricNo for this connection
            var matricNo = GetMatricNoForConnection();
            
            if (!string.IsNullOrEmpty(matricNo))
            {
                // Handle participant disconnection for session tracking
                var session = _sessionService.GetSessionByParticipant(matricNo);
                if (session is not null && !IsSessionLecturer(session.SessionId, matricNo))
                {
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
                }
                
                Console.WriteLine($"[SessionHub] Connection {Context.ConnectionId} (MatricNo: {matricNo}) disconnected");
            }
            
            // Clean up connection mapping
            _connectionMatricNos.TryRemove(Context.ConnectionId, out _);
            
            await base.OnDisconnectedAsync(exception);
        }
        public static bool TryGetLastSeen(string participantId, out DateTime lastSeen) =>
            _lastSeen.TryGetValue(participantId, out lastSeen);

        // Messaging - Reaction Methods
        public async Task AddReaction(string sessionId, string messageId, string emoji)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            if (string.IsNullOrEmpty(matricNo)) return;

            var success = _messageService.AddReaction(sessionId, messageId, matricNo, emoji);
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

            var success = _messageService.RemoveReaction(sessionId, messageId, matricNo, emoji);
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
            var session = _sessionService.GetSessionById(sessionId);

            // Only lecturer can prompt engagement
            if (session != null && IsSessionLecturer(sessionId, matricNo))
            {
                // Broadcast "Are You There?" to all students in session (except lecturer)
                await Clients.GroupExcept(sessionId, Context.ConnectionId).SendAsync("AreYouThere");
                Console.WriteLine($"Lecturer {matricNo} prompted engagement for session {sessionId}");
            }
        }
    }
}