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
        private readonly StudentSeatService _seatService;
        private readonly RosterService _rosterService;
        private static readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
        private static readonly ConcurrentDictionary<string, DateTime> _pendingEngagementPrompts = new();

        public SessionHub(
            MessageService messageService,
            SessionService sessionService,
            UserService userService,
            StudentSeatService seatService,
            RosterService rosterService)
        {
            _messageService = messageService;
            _sessionService = sessionService;
            _userService = userService;
            _seatService = seatService;
            _rosterService = rosterService;
        }

        /// <summary>
        /// Resolves the authenticated matric number from the auth cookie claims.
        /// Falls back to the legacy session value during migration.
        /// IMPORTANT: identity must never be accepted from a client-supplied parameter.
        /// </summary>
        private string? CurrentMatricNo()
        {
            var fromClaims = Context.User?.FindFirst(ViidiiClaims.MatricNo)?.Value;
            if (!string.IsNullOrWhiteSpace(fromClaims))
            {
                return fromClaims;
            }

            // Students hold a seat rather than an account, so there are no claims for
            // them. The hub has to recognise the seat on every call, not just when the
            // classroom page first loaded, or the connection drops them as anonymous.
            var seat = _seatService.Read(Context.GetHttpContext());
            if (seat is not null)
            {
                return seat.MatricNo;
            }

            return Context.GetHttpContext()?.Session.GetString("MatricNo");
        }

        /// <summary>
        /// Guards a hub method: rejects the call when the caller is anonymous.
        /// Returns false (after notifying the caller) when authentication is missing.
        /// </summary>
        private async Task<bool> RequireAuthAsync()
        {
            if (!string.IsNullOrWhiteSpace(CurrentMatricNo()))
            {
                return true;
            }

            await Clients.Caller.SendAsync("Error", "You are not signed in. Please sign in and try again.");
            Context.Abort();
            return false;
        }

        /// <summary>
        /// Resolves who is calling, and proves it rather than believing it.
        /// <para>
        /// The room builds its hub connection on the server: the component constructs a
        /// HubConnection that calls this hub over HTTP from the web server itself. That
        /// client has no cookie jar, so Context.User is always anonymous on it and every
        /// call arrived here unidentified. That is why the lecturer's connection ID was
        /// never recorded, and why students sat waiting for a lecturer who had already
        /// started.
        /// </para>
        /// <para>
        /// The component's own circuit is different: it runs where the request cookies
        /// are, and it already resolves an identity there before rendering. So it passes
        /// what it resolved, and this method checks that against the database rather
        /// than accepting it. A caller may claim to be whoever they like; the claim only
        /// counts if they own the session, or are on that session's class list.
        /// </para>
        /// </summary>
        private async Task<string?> ResolveVerifiedIdentityAsync(string sessionId, string? claimed)
        {
            var fromConnection = CurrentMatricNo();

            if (!string.IsNullOrWhiteSpace(fromConnection))
            {
                return fromConnection;
            }

            if (string.IsNullOrWhiteSpace(claimed))
            {
                return null;
            }

            var session = await _sessionService.GetSessionByIdAsync(sessionId);

            if (session is null)
            {
                return null;
            }

            // A lecturer is whoever the session was created for.
            if (!string.IsNullOrWhiteSpace(session.LecturerMatricNo)
                && string.Equals(session.LecturerMatricNo, claimed, StringComparison.OrdinalIgnoreCase))
            {
                return claimed;
            }

            // If the participant was already admitted to this session (e.g. via direct invite, RSVP, or SessionService.JoinSession):
            if (session.ParticipantIds.Any(id => string.Equals(id.Trim(), claimed.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return claimed;
            }

            // A student has to actually be on the list for this class. Without a class
            // there is nothing to check against, so the claim is refused rather than
            // allowed through on the strength of being a well-formed identifier.
            if (session.ClassId is int classId && classId > 0)
            {
                // Identifier only, deliberately. The name has already been checked once,
                // where the student claimed their seat, and that check compares against
                // the roster's spelling of the name. Re-deriving it here would require
                // the display name to travel with the identifier, and it buys nothing:
                // the seat cookie is what the student holds, and this is only confirming
                // the claim refers to a real attendee on this class.
                var onList = await _rosterService.GetForClassAsync(classId);

                if (onList.Any(r => string.Equals(r.Identifier.Trim(), claimed.Trim(),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return claimed;
                }
            }

            // Also allow registered users known to the system:
            var registeredUser = await _userService.GetUserByMatricNoAsync(claimed);
            if (registeredUser != null)
            {
                return claimed;
            }

            Console.WriteLine(
                $"[SessionHub] Refused claimed identity '{claimed}' for session {sessionId}: "
                + "not the lecturer and not on the class list.");

            return null;
        }

        public async Task StartSession(string sessionId, string? claimedMatricNo = null)
        {
            var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);

            // Same reason as JoinSession: with no identity the call reaches the service
            // as a null participant key and throws.
            if (string.IsNullOrWhiteSpace(matricNo))
            {
                Console.WriteLine($"[SessionHub] StartSession refused: no identity for connection {Context.ConnectionId}.");
                await Clients.Caller.SendAsync("Error", "Your seat expired. Reload the page and try again.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if(session != null)
            {
                if (await IsSessionLecturerAsync(sessionId,matricNo))
                {
                    // CRITICAL: set LecturerConnectionId BEFORE broadcasting.
                    // Broadcasting first races students who need the lecturer's
                    // connection ID to negotiate their peer connection.
                    session.LecturerConnectionId = Context.ConnectionId;
                    Console.WriteLine($"Lecturer {matricNo} set LecturerConnectionId: {session.LecturerConnectionId}");

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
                    var (joinedSession, error) = await _sessionService.JoinSession(sessionId, matricNo, Context.ConnectionId);
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

        public async Task JoinSession(string sessionId, string? claimedMatricNo = null)
        {
            var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);

            // Students hold a seat rather than an account. If no identity can be proven
            // there is nothing to join under, and the service used to throw on a null
            // dictionary key, killing the circuit. Tell the caller instead.
            if (string.IsNullOrWhiteSpace(matricNo))
            {
                Console.WriteLine($"[SessionHub] JoinSession refused: no identity for connection {Context.ConnectionId}.");
                await Clients.Caller.SendAsync("Error", "Your seat expired. Reload the page and join again.");
                return;
            }

            Console.WriteLine($"[SessionHub] JoinSession called. SessionId={sessionId}, MatricNo={matricNo}, ConnectionId={Context.ConnectionId}");
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session == null || session.Status == SessionStatus.Ended)
            {
                Console.WriteLine($"JoinSession failed: Session {sessionId} not found.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

            bool isLecturer = await IsSessionLecturerAsync(sessionId, matricNo);
            if (isLecturer)
            {
                session.LecturerConnectionId = Context.ConnectionId;
                await Groups.AddToGroupAsync(Context.ConnectionId, $"{sessionId}-lecturers");
                Console.WriteLine($"[SessionHub] Lecturer {matricNo} joined and set LecturerConnectionId: {session.LecturerConnectionId}");
            }
            else
            {
                var (joinedSession, error) = await _sessionService.JoinSession(sessionId, matricNo, Context.ConnectionId);
                if (joinedSession is null)
                {
                    Console.WriteLine($"JoinSession failed: {error}");
                    return;
                }
                session = joinedSession;
                Console.WriteLine($"Student {matricNo} joined session {sessionId}, ParticipantIds: {string.Join(", ", session.ParticipantIds)}");
            }

            if (session.IsSessionStarted)
            {
                await Clients.Caller.SendAsync("StartSession", sessionId);
                await Clients.Caller.SendAsync("SessionStarted", sessionId);
            }

            // Broadcast current participants and statuses to the lecturer UI
            await BroadcastParticipantsToLecturerAsync(session);
        }

        private async Task BroadcastParticipantsToLecturerAsync(Session session)
        {
            if (session == null) return;

            var participants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in session.ParticipantIds)
            {
                var user = await _userService.GetUserByMatricNoAsync(id);
                participants[id] = user?.Name ?? id;
            }

            var statuses = _sessionService.GetParticipantStatus(session.SessionId);
            var scores = _sessionService.CalculateAttendanceScore(session.SessionId);

            Console.WriteLine($"[SessionHub] BroadcastParticipantsToLecturerAsync for session {session.SessionId}: {participants.Count} participants ({string.Join(", ", participants.Select(p => $"{p.Key}={p.Value}"))})");

            await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipants", participants);
            await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantStatuses", statuses);
            await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantScoreDetails", scores);

            if (!string.IsNullOrEmpty(session.LecturerConnectionId))
            {
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
            }
        }


        public async Task EndSession(string sessionId, string? claimedMatricNo = null)
        {
            var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);
            var session = await _sessionService.GetSessionByIdAsync(sessionId);

            bool isLecturer = false;
            if (session != null)
            {
                if (Context.ConnectionId == session.LecturerConnectionId)
                {
                    isLecturer = true;
                }
                else if (!string.IsNullOrEmpty(matricNo))
                {
                    isLecturer = await IsSessionLecturerAsync(sessionId, matricNo);
                }
                else if (!string.IsNullOrWhiteSpace(session.LecturerMatricNo) &&
                         string.Equals(session.LecturerMatricNo, claimedMatricNo?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    isLecturer = true;
                }
            }

            if (session != null && isLecturer)
            {
                var lecturerId = matricNo ?? claimedMatricNo ?? session.LecturerMatricNo;
                _sessionService.EndSession(sessionId, lecturerId);

                await BroadcastParticipantsToLecturerAsync(session);

                // Notify all clients in the session that it has ended
                await Clients.Group(sessionId).SendAsync("SessionEnded", sessionId);
                MessageService.ClearSessionMessages(sessionId);
                Console.WriteLine($"[SessionHub] Session {sessionId} ended by lecturer {lecturerId}. Ephemeral messages purged.");
            }
            else
            {
                Console.WriteLine($"[SessionHub] EndSession rejected for session {sessionId}. claimed: '{claimedMatricNo}', matric: '{matricNo}', caller: '{Context.ConnectionId}'");
            }
        }

        public async Task SessionStarted(string sessionId)
        {
            await Clients.Group(sessionId).SendAsync("SessionStarted", sessionId); // Inform everyone

            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session != null)
            {
                var matricNo = CurrentMatricNo();
                if (!string.IsNullOrEmpty(matricNo) && await IsSessionLecturerAsync(sessionId, matricNo))
                {
                    session.LecturerConnectionId = Context.ConnectionId;
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"{sessionId}-lecturers");
                }

                await BroadcastParticipantsToLecturerAsync(session);
            }
        }

        public async Task SendLiveCaption(string sessionId, string speakerName, string text, bool isFinal)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(text)) return;
            await Clients.Group(sessionId).SendAsync("ReceiveLiveCaption", new
            {
                type = "caption",
                speaker = speakerName,
                text = text,
                isFinal = isFinal,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        public async Task NotifyStreamChange(string sessionId, string streamType)
        {
            var matricNo = CurrentMatricNo();
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
            // MUST be OthersInGroup, not Group: a client that receives its own peer ID
            // attempts to negotiate a WebRTC connection with itself, which fails and
            // can stall the whole mesh handshake for that student.
            await Clients.OthersInGroup(sessionId).SendAsync("ReceivePeerId", userId, peerId);
        }

        public async Task CreatePost(string sessionId, string content, bool isFile)
        {
            await SendRoomMessage(sessionId, content, isFile, null, null, null, false);
        }

        public async Task SendRoomMessage(
            string sessionId,
            string content,
            bool isFile,
            string? fileUrl,
            string? fileName,
            long? fileSizeBytes,
            bool isQuestion,
            string? claimedMatricNo = null)
        {
            try
            {
                var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);
                if (string.IsNullOrEmpty(matricNo))
                {
                    var session = await _sessionService.GetSessionByIdAsync(sessionId);
                    if (session != null)
                    {
                        if (session.LecturerConnectionId == Context.ConnectionId)
                        {
                            matricNo = session.LecturerMatricNo;
                        }
                        else
                        {
                            matricNo = session.ParticipantConnectionIds.FirstOrDefault(kv => kv.Value == Context.ConnectionId).Key;
                        }
                    }
                }
                if (string.IsNullOrEmpty(matricNo))
                {
                    matricNo = CurrentMatricNo();
                }
                if (string.IsNullOrEmpty(matricNo) && !string.IsNullOrWhiteSpace(claimedMatricNo))
                {
                    matricNo = claimedMatricNo;
                }

                if (string.IsNullOrEmpty(matricNo))
                {
                    matricNo = "Attendee";
                }

                var user = await _userService.GetUserByMatricNoAsync(matricNo);
                var userName = user?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    var seat = _seatService.Read(Context.GetHttpContext());
                    userName = seat?.DisplayName ?? matricNo;
                }

                var isLecturer = await IsSessionLecturerAsync(sessionId, matricNo);
                var post = await _messageService.CreatePostAsync(
                    sessionId,
                    matricNo,
                    userName,
                    content,
                    isLecturer,
                    isFile,
                    fileUrl,
                    fileName,
                    fileSizeBytes,
                    isQuestion);

                await Clients.Group(sessionId).SendAsync("ReceivePost", post);
                await Clients.Caller.SendAsync("PostCreated", post.id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SendMessage] Error: {ex.Message}");
                await Clients.Caller.SendAsync("Error", "Failed to send message. Please try again.");
            }
        }

        public async Task MarkQuestionAnswered(string sessionId, string messageId, bool isAnswered)
        {
            try
            {
                var matricNo = CurrentMatricNo();

                var success = await _messageService.MarkQuestionAnsweredAsync(sessionId, messageId, isAnswered);
                if (success)
                {
                    await Clients.Group(sessionId).SendAsync("ReceiveQuestionAnswered", messageId, isAnswered);
                    Console.WriteLine($"[MarkQuestionAnswered] Question {messageId} in {sessionId} set answered={isAnswered} by {matricNo}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarkQuestionAnswered] Error: {ex.Message}");
            }
        }

        public async Task CreateComment(string sessionId, string postId, string content, string? claimedMatricNo = null)
        {
            try
            {
                var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);
                if (string.IsNullOrEmpty(matricNo))
                {
                    var session = await _sessionService.GetSessionByIdAsync(sessionId);
                    if (session != null)
                    {
                        if (session.LecturerConnectionId == Context.ConnectionId)
                        {
                            matricNo = session.LecturerMatricNo;
                        }
                        else
                        {
                            matricNo = session.ParticipantConnectionIds.FirstOrDefault(kv => kv.Value == Context.ConnectionId).Key;
                        }
                    }
                }
                if (string.IsNullOrEmpty(matricNo))
                {
                    matricNo = CurrentMatricNo();
                }
                if (string.IsNullOrEmpty(matricNo) && !string.IsNullOrWhiteSpace(claimedMatricNo))
                {
                    matricNo = claimedMatricNo;
                }

                if (string.IsNullOrEmpty(matricNo))
                {
                    matricNo = "Attendee";
                }

                var user = await _userService.GetUserByMatricNoAsync(matricNo);
                var userName = user?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    var seat = _seatService.Read(Context.GetHttpContext());
                    userName = seat?.DisplayName ?? matricNo;
                }

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
            var matricNo = CurrentMatricNo();
            Session? session = null;
            if (!string.IsNullOrEmpty(matricNo))
            {
                session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            }
            if (session == null)
            {
                session = _sessionService.GetAllSessions().FirstOrDefault(s => s.ParticipantConnectionIds.Values.Contains(Context.ConnectionId));
                if (session != null && string.IsNullOrEmpty(matricNo))
                {
                    matricNo = session.ParticipantConnectionIds.FirstOrDefault(kv => kv.Value == Context.ConnectionId).Key;
                }
            }

            if (session is not null && !string.IsNullOrEmpty(matricNo) && !await IsSessionLecturerAsync(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                var status = isActive ? Session.StudentStatus.Active : Session.StudentStatus.InActive;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, status))
                {
                    var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                    var scores = _sessionService.CalculateAttendanceScore(session.SessionId);

                    await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantStatuses", statuses);
                    await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantScoreDetails", scores);

                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    {
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                        Console.WriteLine($"Sent updated scores to lecturer after {matricNo} status changed to {status}");
                    }
                }
            }
        }

        public async Task FlagIssue(string issue)
        {
            var matricNo = CurrentMatricNo();
            Session? session = null;
            if (!string.IsNullOrEmpty(matricNo))
            {
                session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            }
            if (session == null)
            {
                session = _sessionService.GetAllSessions().FirstOrDefault(s => s.ParticipantConnectionIds.Values.Contains(Context.ConnectionId));
                if (session != null && string.IsNullOrEmpty(matricNo))
                {
                    matricNo = session.ParticipantConnectionIds.FirstOrDefault(kv => kv.Value == Context.ConnectionId).Key;
                }
            }

            if (session is not null && !string.IsNullOrEmpty(matricNo) && !await IsSessionLecturerAsync(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                var status = issue == "BatteryLow" ? Session.StudentStatus.BatteryLow : Session.StudentStatus.DataFinished;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, status))
                {
                    var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                    var scores = _sessionService.CalculateAttendanceScore(session.SessionId);

                    await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantStatuses", statuses);
                    await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantScoreDetails", scores);

                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    {
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                        Console.WriteLine($"Sent updated scores to lecturer after {matricNo} flagged {issue}");
                    }
                }
            }
        }

        public async Task ConfirmActive(string? claimedMatricNo = null, string? sessionId = null)
        {
            var matricNo = CurrentMatricNo();
            if (string.IsNullOrEmpty(matricNo) && !string.IsNullOrWhiteSpace(claimedMatricNo))
            {
                matricNo = claimedMatricNo.Trim();
            }

            Session? session = null;
            if (!string.IsNullOrEmpty(sessionId))
            {
                session = await _sessionService.GetSessionByIdAsync(sessionId);
            }
            if (session == null && !string.IsNullOrEmpty(matricNo))
            {
                session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            }
            if (session == null)
            {
                session = _sessionService.GetAllSessions().FirstOrDefault(s => s.ParticipantConnectionIds.Values.Contains(Context.ConnectionId));
            }

            if (string.IsNullOrEmpty(matricNo) && session != null)
            {
                matricNo = session.ParticipantConnectionIds.FirstOrDefault(kv => kv.Value == Context.ConnectionId).Key;
            }

            if (string.IsNullOrWhiteSpace(matricNo) || session == null)
            {
                Console.WriteLine($"[SessionHub] ConfirmActive rejected: unable to resolve matricNo or session for connection {Context.ConnectionId}");
                return;
            }

            if (!await IsSessionLecturerAsync(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                _pendingEngagementPrompts.TryRemove(matricNo, out _);
                _lastSeen[matricNo] = DateTime.UtcNow;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.Active))
                {
                    _lastSeen[matricNo] = DateTime.UtcNow;

                    var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                    var scores = _sessionService.CalculateAttendanceScore(session.SessionId);

                    await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantStatuses", statuses);
                    await Clients.Group($"{session.SessionId}-lecturers").SendAsync("ReceiveParticipantScoreDetails", scores);

                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    {
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                    }
                    Console.WriteLine($"[SessionHub] Sent updated scores/statuses to lecturer after {matricNo} confirmed active in session {session.SessionId}");
                }
            }
        }

        public async Task RecordDeviceTelemetry(int? batteryLevel, bool? isCharging, int? rtt, string? effectiveType)
        {
            var matricNo = CurrentMatricNo();
            if (string.IsNullOrWhiteSpace(matricNo)) return;

            var session = await _sessionService.GetSessionByParticipantAsync(matricNo);
            if (session is not null && !await IsSessionLecturerAsync(session.SessionId, matricNo))
            {
                _sessionService.RecordParticipantTelemetry(session.SessionId, matricNo, batteryLevel, isCharging, rtt, effectiveType);
                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                {
                    var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var matricNo = CurrentMatricNo();
            if (!string.IsNullOrEmpty(matricNo))
            {
                var session = await _sessionService.GetSessionByParticipantAsync(matricNo);
                if (session is not null && !await IsSessionLecturerAsync(session.SessionId, matricNo))
                {
                    session.ParticipantConnectionIds.Remove(matricNo);

                    // Check if passive telemetry detected an issue (low battery or high latency) before drop
                    if (_sessionService.TryGetParticipantTelemetry(session.SessionId, matricNo, out var telemetry) && telemetry != null)
                    {
                        if (telemetry.BatteryLevel.HasValue && telemetry.BatteryLevel.Value <= 8 && telemetry.IsCharging != true)
                        {
                            _sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.BatteryLow);
                        }
                        else if ((telemetry.Rtt.HasValue && telemetry.Rtt.Value >= 1500) || string.Equals(telemetry.EffectiveType, "slow-2g", StringComparison.OrdinalIgnoreCase))
                        {
                            _sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.DataFinished);
                        }
                    }

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
            var matricNo = CurrentMatricNo();
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
            var matricNo = CurrentMatricNo();
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
        public async Task PromptEngagement(string sessionId, string? claimedMatricNo = null)
        {
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session == null)
            {
                Console.WriteLine($"[SessionHub] PromptEngagement rejected: session {sessionId} not found");
                return;
            }

            var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);
            if (string.IsNullOrEmpty(matricNo))
            {
                if (session.LecturerConnectionId == Context.ConnectionId)
                    matricNo = session.LecturerMatricNo;
                else
                    matricNo = CurrentMatricNo();
            }
            if (string.IsNullOrEmpty(matricNo) && !string.IsNullOrWhiteSpace(claimedMatricNo))
            {
                matricNo = claimedMatricNo.Trim();
            }

            bool isLecturer = Context.ConnectionId == session.LecturerConnectionId ||
                              (!string.IsNullOrEmpty(matricNo) && await IsSessionLecturerAsync(sessionId, matricNo)) ||
                              (!string.IsNullOrWhiteSpace(session.LecturerMatricNo) && string.Equals(session.LecturerMatricNo, claimedMatricNo?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (!isLecturer)
            {
                Console.WriteLine($"[SessionHub] PromptEngagement rejected: caller {Context.ConnectionId} / {matricNo} is not the lecturer of session {sessionId}");
                return;
            }

            Console.WriteLine($"[SessionHub] Lecturer {matricNo} manually prompted engagement for session {sessionId}");

            // Register engagement prompt for all student participants
            foreach (var participantId in session.ParticipantIds)
            {
                if (!await _sessionService.IsLecturerAsync(participantId))
                {
                    RegisterEngagementPrompt(participantId, DateTime.UtcNow);
                }
            }

            // Exclude lecturer connection IDs from receiving "AreYouThere"
            var exclude = new List<string>();
            if (!string.IsNullOrEmpty(Context.ConnectionId)) exclude.Add(Context.ConnectionId);
            if (!string.IsNullOrEmpty(session.LecturerConnectionId) && !exclude.Contains(session.LecturerConnectionId))
                exclude.Add(session.LecturerConnectionId);

            // Broadcast to all active students in the session group
            await Clients.GroupExcept(sessionId, exclude).SendAsync("AreYouThere");
            Console.WriteLine($"[SessionHub] AreYouThere broadcast sent to session {sessionId} (excluded {exclude.Count} lecturer connection(s))");
        }

        public record ActiveSessionQuiz(
            string QuizId,
            string Question,
            string[] Options,
            int CorrectIndex,
            string SourceQuote,
            int TimeoutSeconds,
            DateTime StartedAt,
            ConcurrentDictionary<string, int> Responses
        );

        private static readonly ConcurrentDictionary<string, ActiveSessionQuiz> _activeQuizzes = new();

        public static bool HasActiveQuiz(string sessionId)
        {
            if (_activeQuizzes.TryGetValue(sessionId, out var activeQuiz))
            {
                if (DateTime.UtcNow - activeQuiz.StartedAt < TimeSpan.FromSeconds(activeQuiz.TimeoutSeconds + 15))
                {
                    return true;
                }
                _activeQuizzes.TryRemove(sessionId, out _);
            }
            return false;
        }

        public async Task PromptEngagementWithQuiz(
            string sessionId, 
            string question, 
            string[] options, 
            int correctIndex, 
            int timeoutSeconds, 
            string? sourceQuote = null,
            string? claimedMatricNo = null)
        {
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session == null)
            {
                Console.WriteLine($"[SessionHub] PromptEngagementWithQuiz rejected: session {sessionId} not found");
                return;
            }

            var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);
            if (string.IsNullOrEmpty(matricNo))
            {
                if (session.LecturerConnectionId == Context.ConnectionId)
                    matricNo = session.LecturerMatricNo;
                else
                    matricNo = CurrentMatricNo();
            }
            if (string.IsNullOrEmpty(matricNo) && !string.IsNullOrWhiteSpace(claimedMatricNo))
            {
                matricNo = claimedMatricNo.Trim();
            }

            bool isLecturer = Context.ConnectionId == session.LecturerConnectionId ||
                              (!string.IsNullOrEmpty(matricNo) && await IsSessionLecturerAsync(sessionId, matricNo)) ||
                              (!string.IsNullOrWhiteSpace(session.LecturerMatricNo) && string.Equals(session.LecturerMatricNo, claimedMatricNo?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (!isLecturer)
            {
                Console.WriteLine($"[SessionHub] PromptEngagementWithQuiz rejected: caller {Context.ConnectionId} / {matricNo} is not the lecturer of session {sessionId}");
                return;
            }

            var quizId = Guid.NewGuid().ToString("N");
            var activeQuiz = new ActiveSessionQuiz(
                quizId,
                question,
                options,
                correctIndex,
                sourceQuote ?? "",
                timeoutSeconds > 0 ? timeoutSeconds : 30,
                DateTime.UtcNow,
                new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            );

            _activeQuizzes[sessionId] = activeQuiz;

            // Also register engagement prompt for all student participants
            foreach (var participantId in session.ParticipantIds)
            {
                if (!await _sessionService.IsLecturerAsync(participantId))
                {
                    RegisterEngagementPrompt(participantId, DateTime.UtcNow);
                }
            }

            // Broadcast quiz to all attendees in session
            await Clients.Group(sessionId).SendAsync(
                "ReceiveEngagementQuiz", 
                quizId, 
                question, 
                options, 
                activeQuiz.TimeoutSeconds, 
                activeQuiz.SourceQuote);

            Console.WriteLine($"[SessionHub] Quiz {quizId} sent to session {sessionId}: {question}");
        }

        public async Task SubmitQuizResponse(string sessionId, string quizId, int selectedIndex, string? claimedMatricNo = null)
        {
            var matricNo = await ResolveVerifiedIdentityAsync(sessionId, claimedMatricNo);
            if (string.IsNullOrEmpty(matricNo))
            {
                var sessionObj = await _sessionService.GetSessionByIdAsync(sessionId);
                if (sessionObj != null)
                {
                    matricNo = sessionObj.ParticipantConnectionIds.FirstOrDefault(kv => kv.Value == Context.ConnectionId).Key;
                }
            }
            if (string.IsNullOrEmpty(matricNo)) matricNo = CurrentMatricNo();
            if (string.IsNullOrEmpty(matricNo) && !string.IsNullOrWhiteSpace(claimedMatricNo)) matricNo = claimedMatricNo.Trim();

            if (string.IsNullOrWhiteSpace(matricNo))
            {
                Console.WriteLine($"[SessionHub] SubmitQuizResponse rejected: unable to resolve identity for connection {Context.ConnectionId}");
                return;
            }

            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            if (session == null || !session.IsSessionStarted) return;

            if (_activeQuizzes.TryGetValue(sessionId, out var activeQuiz) && activeQuiz.QuizId == quizId)
            {
                // Record response
                activeQuiz.Responses[matricNo] = selectedIndex;
                bool isCorrect = (selectedIndex == activeQuiz.CorrectIndex);

                _pendingEngagementPrompts.TryRemove(matricNo, out _);
                _lastSeen[matricNo] = DateTime.UtcNow;

                // Update participant status to Active
                _sessionService.UpdateParticipantStatus(sessionId, matricNo, Session.StudentStatus.Active);

                // Notify student of result
                await Clients.Caller.SendAsync("ReceiveQuizFeedback", isCorrect, activeQuiz.CorrectIndex);

                // Notify lecturer of live updated quiz stats
                int totalResponses = activeQuiz.Responses.Count;
                int correctCount = activeQuiz.Responses.Values.Count(v => v == activeQuiz.CorrectIndex);
                int totalEligible = session.ParticipantIds.Count(p => p != session.LecturerMatricNo);

                await Clients.Group($"{sessionId}-lecturers").SendAsync(
                    "ReceiveQuizLiveStats",
                    quizId,
                    totalEligible,
                    totalResponses,
                    correctCount
                );

                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                {
                    await Clients.Client(session.LecturerConnectionId).SendAsync(
                        "ReceiveQuizLiveStats",
                        quizId,
                        totalEligible,
                        totalResponses,
                        correctCount
                    );

                    // Push updated statuses & attendance scores to lecturer
                    var statuses = _sessionService.GetParticipantStatus(sessionId);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);

                    var scores = _sessionService.CalculateAttendanceScore(sessionId);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantScoreDetails", scores);
                }

                Console.WriteLine($"[SessionHub] Student {matricNo} answered quiz {quizId}: choice={selectedIndex}, correct={isCorrect}");
            }
            else
            {
                // Fallback: regular confirm active
                await ConfirmActive(claimedMatricNo, sessionId);
            }
        }
    }
}