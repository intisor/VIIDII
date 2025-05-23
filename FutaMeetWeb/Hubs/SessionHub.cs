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
                session.LecturerConnectionId = Context.ConnectionId;
                if (IsSessionLecturer(sessionId,matricNo))
                {
                    session.LecturerConnectionId = Context.ConnectionId;
                }
                else
                {
                    var (joinedSession, error) = _sessionService.JoinSession(sessionId, matricNo);
                    if (joinedSession is null)
                    {
                        Console.WriteLine($"JoinSession failed: {error}");
                        return;
                    }
                }
                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                {
                    var participants = session.ParticipantIds.ToDictionary(id => id, id => id);
                    await Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipants", participants);
                    Console.WriteLine($"Sent participants to lecturer: {string.Join(", ", participants.Keys)}");
                }
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
    }
}