using Microsoft.AspNetCore.SignalR;

namespace FutaMeetWeb.Hubs
{
    public class SessionHub : Hub
    {
        public async Task JoinSession(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        }
        public async Task SendOffer(string sessionId,string offer)
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
        public async Task SendMessagee(string sessionId,string userId,string message)
        {
            await Clients.Group(sessionId).SendAsync("ReceiveMessage", userId, message);
        }
    }
}
