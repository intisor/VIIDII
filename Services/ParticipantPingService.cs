using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading;
using System.Threading.Tasks;
using VIIDII.Services;
using VIIDII.Hubs;
using VIIDII.Models;

namespace VIIDII.Services
{
    public class ParticipantPingService : BackgroundService
    {
        private readonly IHubContext<SessionHub> _hubContext;
        private readonly SessionService _sessionService;
        private readonly Random _rand = new();
        private TimeSpan Interval => TimeSpan.FromMinutes(_rand.Next(2,5));
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
                var sessions = await _sessionService.GetActiveSessionsAsync();
                foreach (var session in sessions)
                {
                    foreach (var participantId in session.ParticipantIds)
                    {
                        if (!_sessionService.IsLecturer(participantId))
                        {
                            if(session.ParticipantConnectionIds.TryGetValue(participantId, out var connectionId))
                            {
                                SessionHub.RegisterEngagementPrompt(participantId, DateTime.UtcNow);
                                await _hubContext.Clients.Client(connectionId).SendAsync("AreYouThere");
                            }

                            if (SessionHub.TryGetPendingEngagementPrompt(participantId, out var promptedAt) &&
                                DateTime.UtcNow - promptedAt > Timeout)
                            {
                                SessionHub.ClearPendingEngagementPrompt(participantId);

                                if (session.ParticipantStatuses.TryGetValue(participantId, out var currentStatus) &&
                                    currentStatus != Session.StudentStatus.InActive)
                                {
                                    _sessionService.UpdateParticipantStatus(session.SessionId, participantId, Session.StudentStatus.InActive);
                                    if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                                    {
                                        var statuses = _sessionService.GetParticipantStatus(session.SessionId);
                                        await _hubContext.Clients.Client(session.LecturerConnectionId).SendAsync("ReceiveParticipantStatuses", statuses);
                                        Console.WriteLine($"Marked {participantId} as Inactive in session {session.SessionId}");
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
