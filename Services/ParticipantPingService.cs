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
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ParticipantPingService> _logger;
        private readonly Random _rand = new();
        private TimeSpan GetNextInterval() => TimeSpan.FromSeconds(_rand.Next(40, 61));
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(35);

        public ParticipantPingService(
            IHubContext<SessionHub> hubContext,
            IServiceScopeFactory scopeFactory,
            ILogger<ParticipantPingService> logger)
        {
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var nextInterval = GetNextInterval();
            _logger.LogInformation("Engagement ping loop started. Next interval {Interval}s", nextInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                // A BackgroundService is a singleton, so it cannot take a scoped
                // SessionService directly. Create a scope per cycle instead — this
                // is also what keeps the DbContext inside that scope from leaking.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

                try
                {
                    await RunCycleAsync(sessionService, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A failed cycle must not kill the background service; attendance
                    // would silently stop updating and nobody would notice until a recap.
                    _logger.LogError(ex, "Engagement ping cycle failed; continuing.");
                }

                await Task.Delay(nextInterval, stoppingToken);
                nextInterval = GetNextInterval();
            }
        }

        private async Task RunCycleAsync(SessionService sessionService, CancellationToken stoppingToken)
        {
            var sessions = await sessionService.GetActiveSessionsAsync();
            _logger.LogDebug("Checking {Count} active session(s)", sessions.Count);

            foreach (var session in sessions)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                var promptedConnectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var participantId in session.ParticipantIds)
                {
                    if (await sessionService.IsLecturerAsync(participantId))
                    {
                        continue;
                    }

                    SessionHub.RegisterEngagementPrompt(participantId, DateTime.UtcNow);

                    if (session.ParticipantConnectionIds.TryGetValue(participantId, out var connectionId) &&
                        !string.IsNullOrWhiteSpace(connectionId))
                    {
                        promptedConnectionIds.Add(connectionId);
                        await _hubContext.Clients.Client(connectionId).SendAsync("AreYouThere");
                    }

                    if (SessionHub.TryGetPendingEngagementPrompt(participantId, out var promptedAt) &&
                        DateTime.UtcNow - promptedAt > Timeout)
                    {
                        SessionHub.ClearPendingEngagementPrompt(participantId);

                        if (session.ParticipantStatuses.TryGetValue(participantId, out var currentStatus) &&
                            currentStatus != Session.StudentStatus.InActive)
                        {
                            sessionService.UpdateParticipantStatus(session.SessionId, participantId, Session.StudentStatus.InActive);
                            if (!string.IsNullOrEmpty(session.LecturerConnectionId))
                            {
                                var statuses = sessionService.GetParticipantStatus(session.SessionId);
                                await _hubContext.Clients.Client(session.LecturerConnectionId)
                                    .SendAsync("ReceiveParticipantStatuses", statuses);
                            }
                        }
                    }
                }

                await _hubContext.Clients
                    .GroupExcept(session.SessionId, promptedConnectionIds.ToArray())
                    .SendAsync("AreYouThere");
            }
        }
    }
}
