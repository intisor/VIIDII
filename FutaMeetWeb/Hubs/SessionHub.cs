using Microsoft.AspNetCore.SignalR;

namespace FutaMeetWeb.Hubs
{
    public class SessionHub : Hub
    {
        public async Task JoinSession(string sessionId, string userId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine($"[ERROR] JoinSession failed: sessionId is empty for user {userId}");
                return;
            }
            if (string.IsNullOrEmpty(userId))
            {
                Console.WriteLine($"[ERROR] JoinSession failed: userId is empty for session {sessionId}");
                return;
            }
            Console.WriteLine($"[INFO] Joining {userId} to session {sessionId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            Console.WriteLine($"[INFO] {userId} joined session {sessionId}");
            Console.WriteLine($"[INFO] Sending UserJoined: userId={userId}, connectionId={Context.ConnectionId}");
            await Clients.Group(sessionId).SendAsync("UserJoined", userId, Context.ConnectionId);
        }

        public async Task SendMessage(string sessionId, string userId, string message)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine($"[ERROR] SendMessage failed: sessionId is empty for user {userId}");
                return;
            }
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(message))
            {
                Console.WriteLine($"[ERROR] SendMessage failed: userId={userId}, message={message}");
                return;
            }
            Console.WriteLine($"[INFO] Broadcasting message from {userId} in session {sessionId}: {message}");
            await Clients.Group(sessionId).SendAsync("ReceiveMessage", userId, message);
            Console.WriteLine($"[INFO] Broadcasted to group {sessionId}");
        }

        public async Task SendOffer(string sessionId, string offer)
        {
            await Clients.Group(sessionId).SendAsync("ReceiveOffer", offer);
        }

        public async Task SendAnswer(string sessionId, string answer)
        {
            await Clients.Group(sessionId).SendAsync("ReceiveAnswer", answer);
        }

        public async Task SendIceCandidate(string sessionId, string candidate)
        {
            await Clients.Group(sessionId).SendAsync("ReceiveIceCandidate", candidate);
        }
    }
}