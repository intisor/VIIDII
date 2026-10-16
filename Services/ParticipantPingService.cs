using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
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

        // Random interval within 10 minutes (between 4 and 10 minutes: 240s to 600s)
        private TimeSpan GetNextInterval() => TimeSpan.FromSeconds(_rand.Next(240, 601));
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

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
            _logger.LogInformation("Engagement ping loop started. Next interval {Interval}s ({Minutes:F1} min)", nextInterval.TotalSeconds, nextInterval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(nextInterval, stoppingToken);

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

                nextInterval = GetNextInterval();
                _logger.LogDebug("Next engagement ping scheduled in {Interval}s ({Minutes:F1} min)", nextInterval.TotalSeconds, nextInterval.TotalMinutes);
            }
        }

        private async Task RunCycleAsync(SessionService sessionService, CancellationToken stoppingToken)
        {
            var sessions = await sessionService.GetActiveSessionsAsync();
            _logger.LogDebug("Checking {Count} active session(s) for engagement", sessions.Count);

            foreach (var session in sessions)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                // Only ping if the session has actually started
                if (!session.IsSessionStarted)
                {
                    continue;
                }

                // If a quiz is active in this session, skip this ping cycle so we don't interfere with the quiz
                if (SessionHub.HasActiveQuiz(session.SessionId))
                {
                    _logger.LogInformation("Session {SessionId} has an active quiz; skipping engagement ping.", session.SessionId);
                    continue;
                }

                foreach (var participantId in session.ParticipantIds)
                {
                    if (await sessionService.IsLecturerAsync(participantId))
                    {
                        continue;
                    }

                    // Check if participant has unconfirmed prompt that timed out
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

                    // Skip prompting if the student was active recently (e.g. within 3 minutes)
                    if (SessionHub.TryGetLastSeen(participantId, out var lastSeen) &&
                        DateTime.UtcNow - lastSeen < TimeSpan.FromMinutes(3))
                    {
                        continue;
                    }

                    SessionHub.RegisterEngagementPrompt(participantId, DateTime.UtcNow);

                    if (session.ParticipantConnectionIds.TryGetValue(participantId, out var connectionId) &&
                        !string.IsNullOrWhiteSpace(connectionId))
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync("AreYouThere");
                    }
                }
            }
        }
    }
}
