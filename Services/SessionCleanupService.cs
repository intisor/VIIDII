using VIIDII.Data;
using VIIDII.Models;
using Microsoft.EntityFrameworkCore;

namespace VIIDII.Services;

/// <summary>
/// Background service that periodically cleans up expired sessions from the database.
/// Runs every hour to delete sessions that have exceeded their expiry time.
/// </summary>
public class SessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    public SessionCleanupService(IServiceProvider serviceProvider, ILogger<SessionCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SessionCleanupService started");

        // Initial delay to allow app to fully start
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }

            // Wait for the interval before next cleanup
            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("SessionCleanupService stopped");
    }

    private async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViidiiDbContext>();

        var now = DateTime.UtcNow;

        // Expired means ExpiresAt has passed. This used to be written as two branches,
        // one for ended sessions and one for the rest, both testing ExpiresAt < now, so
        // the status check contributed nothing and ExpiresAt was doing all the work.
        // Which means there is currently no distinction between an old ended session
        // and a stale one that was never closed: both go when they expire.
        var expiredSessions = await dbContext.Sessions
            .Where(s => s.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (expiredSessions.Any())
        {
            _logger.LogInformation($"Found {expiredSessions.Count} expired sessions to delete");

            // Also delete associated records due to cascading deletes
            dbContext.Sessions.RemoveRange(expiredSessions);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation($"Successfully deleted {expiredSessions.Count} expired sessions");
        }
        else
        {
            _logger.LogDebug("No expired sessions found during cleanup");
        }
    }
}
