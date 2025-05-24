using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading;
using System.Threading.Tasks;
using FutaMeetWeb.Services;
using FutaMeetWeb.Hubs;
using FutaMeetWeb.Models;

namespace FutaMeetWeb.Services
{
    public class ParticipantPingService : BackgroundService
    {
        private readonly IHubContext<SessionHub> _hubContext;
        private readonly SessionService _sessionService;
        private readonly Random _rand = new();
        private TimeSpan Interval => TimeSpan.FromMinutes(_rand.Next(5, 20));
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(35);

        public ParticipantPingService(
            IHubContext<SessionHub> hubContext,
            SessionService sessionService)
        {
            _hubContext = hubContext;
            _sessionService = sessionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var sessions = _sessionService.GetActiveSessions();
                foreach (var session in sessions)
                {
                    foreach (var participantId in session.ParticipantIds)
                    {
                        if (!_sessionService.IsLecturer(participantId))
                        {
                            if(session.ParticipantConnectionIds.TryGetValue(participantId, out var connectionId))
                            {
                                await _hubContext.Clients.Client(connectionId).SendAsync("AreYouThere");
                            }
                            if(SessionHub.TryGetLastSeen(participantId, out var last) &&
                            DateTime.UtcNow - last > Timeout)
                            {
                                if (session.PartipantStatuses[participantId] != Session.StudentStatus.InActive)
                                {
                                    _sessionService.UpdateParticipantStatus(session.SessionId, participantId, Session.StudentStatus.InActive);
                                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                                    {
                                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                                        await _hubContext.Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                                    }
                                }
                            }
                        }
                    }  
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
    }
}
