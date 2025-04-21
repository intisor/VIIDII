using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace FutaMeetWeb.Hubs
{
    public class SessionHub : Hub
    {
        private static readonly ConcurrentDictionary<string, (string LecturerConnectionId, ConcurrentBag<string> StudentConnectionIds, bool IsStarted, bool IsLecturerMuted)> Sessions = new();
        private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> SessionMessages = new();

        public async Task StartSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new HubException("Invalid sessionId");
            // Initialize or update session
            Sessions.AddOrUpdate(sessionId,
                (Context.ConnectionId, new ConcurrentBag<string>(), true, false),
                (key, oldValue) =>
                {
                    if (!string.IsNullOrEmpty(oldValue.LecturerConnectionId) && oldValue.LecturerConnectionId != Context.ConnectionId)
                        throw new HubException("Only the lecturer can start the session");
                    return (Context.ConnectionId, oldValue.StudentConnectionIds, true, oldValue.IsLecturerMuted);
                });
            await Clients.Group(sessionId).SendAsync("SessionStarted");
        }

        public async Task JoinSession(string sessionId, string userId, bool isLecturer)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(userId))
                throw new HubException("Invalid sessionId or userId");
            if (!Sessions.ContainsKey(sessionId) || !Sessions[sessionId].IsStarted)
                throw new HubException("Session not started");

            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            Sessions.AddOrUpdate(sessionId,
                (isLecturer ? Context.ConnectionId : null, new ConcurrentBag<string>(), true, false),
                (key, oldValue) =>
                {
                    if (isLecturer)
                    {
                        if (!string.IsNullOrEmpty(oldValue.LecturerConnectionId))
                        {
                            if (oldValue.LecturerConnectionId == Context.ConnectionId)
                                return oldValue; // Same lecturer, no change
                            oldValue.StudentConnectionIds.Add(Context.ConnectionId); // New lecturer joins as student
                            return (oldValue.LecturerConnectionId, oldValue.StudentConnectionIds, oldValue.IsStarted, oldValue.IsLecturerMuted);
                        }
                        return (Context.ConnectionId, oldValue.StudentConnectionIds, oldValue.IsStarted, oldValue.IsLecturerMuted); // First lecturer
                    }
                    oldValue.StudentConnectionIds.Add(Context.ConnectionId); // Non-lecturer joins as student
                    return (oldValue.LecturerConnectionId, oldValue.StudentConnectionIds, oldValue.IsStarted, oldValue.IsLecturerMuted);
                });

            var session = Sessions[sessionId];
            await Clients.Client(Context.ConnectionId).SendAsync("SessionStatus", session.IsStarted, session.IsLecturerMuted, session.LecturerConnectionId);

            if (!isLecturer || (isLecturer && !string.IsNullOrEmpty(session.LecturerConnectionId) && session.LecturerConnectionId != Context.ConnectionId))
            {
                if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                    await Clients.Client(session.LecturerConnectionId).SendAsync("UserJoined", userId, Context.ConnectionId);
            }
        }

        public Task<bool> GetSessionStatus(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new HubException("Invalid sessionId");
            return Task.FromResult(Sessions.ContainsKey(sessionId) && Sessions[sessionId].IsStarted);
        }

        // Update GetSessionStatus to include lecturerConnectionId
        public Task<(bool, bool, string)> GetSessionStatusFull(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new HubException("Invalid sessionId");
            if (!Sessions.ContainsKey(sessionId))
                return Task.FromResult<(bool, bool, string)>((false, false, null));
            var s = Sessions[sessionId];
            return Task.FromResult((s.IsStarted, s.IsLecturerMuted, s.LecturerConnectionId));
        }

        public async Task EndSession(string sessionId)
        {
            if (!Sessions.ContainsKey(sessionId) || Sessions[sessionId].LecturerConnectionId != Context.ConnectionId)
                throw new HubException("Only the lecturer can end the session");

            Sessions.TryRemove(sessionId, out _);
            SessionMessages.TryRemove(sessionId, out _);
            await Clients.Group(sessionId).SendAsync("SessionEnded");
        }

        public async Task UpdateMuteState(string sessionId, bool isMuted)
        {
            if (!Sessions.ContainsKey(sessionId) || Sessions[sessionId].LecturerConnectionId != Context.ConnectionId)
                throw new HubException("Only the lecturer can update mute state");

            Sessions.TryUpdate(sessionId, (Sessions[sessionId].LecturerConnectionId, Sessions[sessionId].StudentConnectionIds, Sessions[sessionId].IsStarted, isMuted), Sessions[sessionId]);
            await Clients.Group(sessionId).SendAsync("MuteStateUpdated", isMuted);
        }

        public async Task SendOffer(string sessionId, string offer, string toConnectionId)
        {
            if (!Sessions.ContainsKey(sessionId))
                throw new HubException("Session does not exist");
            await Clients.Client(toConnectionId).SendAsync("ReceiveOffer", offer, Context.ConnectionId);
        }

        public async Task SendAnswer(string sessionId, string answer, string toConnectionId)
        {
            if (!Sessions.ContainsKey(sessionId))
                throw new HubException("Session does not exist");
            await Clients.Client(toConnectionId).SendAsync("ReceiveAnswer", answer, Context.ConnectionId);
        }

        public async Task SendIceCandidate(string sessionId, string candidate, string toConnectionId)
        {
            if (!Sessions.ContainsKey(sessionId))
                throw new HubException("Session does not exist");
            await Clients.Client(toConnectionId).SendAsync("ReceiveIceCandidate", candidate, Context.ConnectionId);
        }

        public async Task SendMessage(string sessionId, string userId, string message)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(message))
                throw new HubException("Invalid sessionId, userId, or message");

            var timestamp = DateTime.Now.ToString("hh:mm tt");
            var messageKey = $"{userId}:{message}:{timestamp}";

            var messages = SessionMessages.GetOrAdd(sessionId, new ConcurrentBag<string>());
            if (messages.Contains(messageKey)) return;
            messages.Add(messageKey);

            await Clients.Group(sessionId).SendAsync("ReceiveMessage", userId, message, timestamp);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            foreach (var session in Sessions)
            {
                if (session.Value.LecturerConnectionId == Context.ConnectionId)
                {
                    Sessions.TryRemove(session.Key, out _);
                    SessionMessages.TryRemove(session.Key, out _);
                    await Clients.Group(session.Key).SendAsync("SessionEnded");
                }
                else if (session.Value.StudentConnectionIds.Contains(Context.ConnectionId))
                {
                    session.Value.StudentConnectionIds.TryTake(out _);
                    if (session.Value.LecturerConnectionId != null)
                        await Clients.Client(session.Value.LecturerConnectionId).SendAsync("UserLeft", Context.ConnectionId);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}