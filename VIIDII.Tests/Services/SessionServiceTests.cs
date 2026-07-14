using Microsoft.Extensions.DependencyInjection;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Tests.TestInfrastructure;
using Xunit;

namespace VIIDII.Tests.Services;

public class SessionServiceTests
{
    [Fact]
    public async Task CalculateAttendanceScoreFromPersistenceAsync_UsesPersistedDurationsAndReturnsExpectedScore()
    {
        using var factory = new TestDbContextFactory();
        var (_, student, session) = await factory.SeedSessionAsync();

        await using (var arrangeContext = factory.CreateDbContext())
        {
            var repository = new SessionRepository(arrangeContext);
            await repository.AddParticipantAsync(session.Id, student.Id);
            await repository.AddAttendanceLogAsync(
                session.Id,
                student.Id,
                Session.StudentStatus.Active,
                session.StartTime);
            await repository.AddAttendanceLogAsync(
                session.Id,
                student.Id,
                Session.StudentStatus.InActive,
                session.StartTime.AddMinutes(30));

            var persistedSession = await arrangeContext.Sessions.FindAsync(session.Id);
            Assert.NotNull(persistedSession);
            persistedSession!.EndTime = session.StartTime.AddMinutes(60);
            persistedSession.Status = SessionStatus.Ended;
            await arrangeContext.SaveChangesAsync();

            await repository.FinalizeAttendanceLogsAsync(session.Id, persistedSession.EndTime.Value);
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => factory.CreateDbContext());
        services.AddScoped<UserService>();
        services.AddScoped<SessionRepository>();
        services.AddScoped<SessionPersistenceService>();
        services.AddSingleton<IServiceProvider>(sp => sp);
        services.AddSingleton<SessionService>();

        await using var provider = services.BuildServiceProvider();
        var sessionService = provider.GetRequiredService<SessionService>();

        var result = await sessionService.CalculateAttendanceScoreFromPersistenceAsync(session.SessionId);

        Assert.True(result.ContainsKey(student.MatricNo));
        var score = result[student.MatricNo];
        Assert.Equal("Student One", score.ParticipantName);
        Assert.Equal(50, score.FinalScorePercentage);
        Assert.Equal(30, score.TimeActiveMinutes);
        Assert.Equal(30, score.TimeInactiveMinutes);
    }
}
