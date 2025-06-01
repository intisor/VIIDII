using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace FutaMeetWeb.Hubs
{
    public class SessionHub : Hub
    {
        private readonly MessageService _messageService;
        private readonly SessionService _sessionService;
        private static readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();

        public SessionHub(MessageService messageService, SessionService sessionService)
        {
            _messageService = messageService;
            _sessionService = sessionService;
        }

        public async Task StartSession(string sessionId)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            await Clients.Group(sessionId).SendAsync("StartSession", sessionId);
            var session = _sessionService.GetSessionById(sessionId);
            if(session != null)
            {
                if (IsSessionLecturer(sessionId,matricNo))
                {
                    session.LecturerConnectionId = Context.ConnectionId;
                    Console.WriteLine($"Lecturer {matricNo} set LecturerConnectionId: {session.LecturerConnectionId}");
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
                    var participants = session.ParticipantIds.ToDictionary(id => id, id => id);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                    Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
                }
            }
        }

        public async Task JoinSession(string sessionId)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = _sessionService.GetSessionById(sessionId);
            if (session == null)
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
                Console.WriteLine($"Student {matricNo} joined session {sessionId}, ParticipantIds: {string.Join(", ", session.ParticipantIds)}");
            }

            if (session.IsSessionStarted)
            {
                await Clients.Caller.SendAsync("StartSession", sessionId);
                await Clients.Caller.SendAsync("SessionStarted", sessionId);
            }

            if (!string.IsNullOrEmpty(session.LecturerConnectionId))
            {
                var participants = session.ParticipantIds.ToDictionary(id => id, id => id);
                await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
            }
        }

        public async Task SessionStarted(string sessionId)
        {
            await Clients.Group(sessionId).SendAsync("SessionStarted", sessionId);
        }

        public Task SendMessage(string user, string message) => Clients.Others.SendAsync("ReceiveMessage", user, message);

        public async Task CreatePost(string sessionId, string content)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var post = _messageService.CreatePost(sessionId, matricNo, content, true);
            await Clients.Others.SendAsync("ReceivePost", post);
            await Clients.Caller.SendAsync("PostCreated", post.id);
        }

        public async Task CreateComment(string sessionId, string postId, string content)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var isLecturer = IsSessionLecturer(sessionId, matricNo);
            var comment = _messageService.CreateComment(sessionId, matricNo, content, postId, isLecturer);
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
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = _sessionService.GetSessionByParticipant(matricNo);
            if (session is not null && !IsSessionLecturer(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                var status = isActive ? Session.StudentStatus.Active : Session.StudentStatus.InActive;
                if (_sessionService.UpdateParticipantStatus(session.SessionId, matricNo, status))
                {
                    if (session.LecturerConnectionId != null)
                    {
                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                    }
                }
            }
        }

        public async Task FlagIssue(string issue)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = _sessionService.GetSessionByParticipant(matricNo);
            if (session is not null && !IsSessionLecturer(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                var status = issue == "BatteryLow" ? Session.StudentStatus.BatteryLow : Session.StudentStatus.DataFinished;
                _sessionService.UpdateParticipantStatus(session.SessionId, matricNo, status);
                if (session.LecturerConnectionId != null)
                {
                    var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                }
            }
        }

        public async Task ConfirmActive()
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            var session = _sessionService.GetSessionByParticipant(matricNo);
            if (session is not null && !IsSessionLecturer(session.SessionId, matricNo) && session.IsSessionStarted)
            {
                _lastSeen[matricNo] = DateTime.UtcNow;
                _sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.Active);
                if (session.LecturerConnectionId != null)
                {
                    var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var matricNo = Context.GetHttpContext()?.Session.GetString("MatricNo");
            if (!string.IsNullOrEmpty(matricNo))
            {
                var session = _sessionService.GetSessionByParticipant(matricNo);
                if (session is not null && !IsSessionLecturer(session.SessionId, matricNo))
                {
                    _sessionService.UpdateParticipantStatus(session.SessionId, matricNo, Session.StudentStatus.Disconnected);
                    if (session.LecturerConnectionId != null)
                    {
                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                        await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
        public static bool TryGetLastSeen(string participantId, out DateTime lastSeen) =>
            _lastSeen.TryGetValue(participantId, out lastSeen);
    }
}