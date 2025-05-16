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

        public async Task StartSession(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            await Clients.Group(sessionId).SendAsync("StartSession", sessionId);
            //ListGroupMembers(sessionId);
        }

        public async Task SessionStarted(string sessionId)
        {
            await Clients.Group(sessionId).SendAsync("SessionStarted", sessionId);
        }

        public Task SendMessage(string user, string message) => Clients.Others.SendAsync("ReceiveMessage", user, message);
        public void ListGroupMembers(string sessionId)
        {
            var group = Clients.Group(sessionId);
            Console.WriteLine($"Group {sessionId} members: {group}");
        }
    }
}