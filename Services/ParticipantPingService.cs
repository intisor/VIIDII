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
        private TimeSpan GetNextInterval() => TimeSpan.FromSeconds(_rand.Next(40, 61));
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
            var nextInterval = GetNextInterval();
            Console.WriteLine($"[PingService] Engagement ping loop started. Next interval={nextInterval.TotalSeconds}s");

            while (!stoppingToken.IsCancellationRequested)
            {
                var sessions = await _sessionService.GetActiveSessionsAsync();
                Console.WriteLine($"[PingService] Checking {sessions.Count} active session(s) at {DateTime.UtcNow:O}");

                foreach (var session in sessions)
                {
                    var promptedConnectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine($"[PingService] Session {session.SessionId}: {session.ParticipantIds.Count} participant(s)");

                    foreach (var participantId in session.ParticipantIds)
                    {
                        if (_sessionService.IsLecturer(participantId))
                        {
                            continue;
                        }

                        Console.WriteLine($"[PingService] Preparing ping for participant {participantId} in session {session.SessionId}");
                        SessionHub.RegisterEngagementPrompt(participantId, DateTime.UtcNow);

                        if (session.ParticipantConnectionIds.TryGetValue(participantId, out var connectionId) &&
                            !string.IsNullOrWhiteSpace(connectionId))
                        {
                            promptedConnectionIds.Add(connectionId);
                            await _hubContext.Clients.Client(connectionId).SendAsync("AreYouThere");
                            Console.WriteLine($"[PingService] Sent direct AreYouThere to {participantId} via connection {connectionId}");
                        }
                        else
                        {
                            Console.WriteLine($"[PingService] No direct SignalR connection for {participantId}; fallback broadcast will be used");
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
                                    Console.WriteLine($"[PingService] Marked {participantId} as Inactive in session {session.SessionId}");
                                }
                            }
                        }
                    }

                    await _hubContext.Clients.GroupExcept(session.SessionId, promptedConnectionIds.ToArray()).SendAsync("AreYouThere");
                    Console.WriteLine($"[PingService] Broadcast fallback AreYouThere to session {session.SessionId} for {promptedConnectionIds.Count} direct recipient(s)");
                }

                await Task.Delay(nextInterval, stoppingToken);
                nextInterval = GetNextInterval();
                Console.WriteLine($"[PingService] Next ping cycle will run in {nextInterval.TotalSeconds}s");
            }
        }
    }
}
