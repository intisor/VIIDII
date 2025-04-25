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
            ListGroupMembers(sessionId);
        }

        public Task SendMessage(string user, string message) => Clients.Others.SendAsync("ReceiveMessage", user, message);
        public Task SendSignal(string sessionId, object data)
        {
            string serializedData = System.Text.Json.JsonSerializer.Serialize(data);
            return Clients.Others.SendAsync("ReceiveSignal", Context.ConnectionId, serializedData);
        }

        public void ListGroupMembers(string sessionId)
        {
            var group = Clients.Group(sessionId);
            Console.WriteLine($"Group {sessionId} members: {group}");
        }
    }
}