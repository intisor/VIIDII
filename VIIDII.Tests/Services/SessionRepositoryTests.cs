using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Tests.TestInfrastructure;
using Xunit;

namespace VIIDII.Tests.Services;

public class SessionRepositoryTests
{
    [Fact]
    public async Task AddAttendanceLogAsync_WhenAddingNextStatus_ClosesPreviousSegmentDuration()
    {
        using var factory = new TestDbContextFactory();
        var (_, student, session) = await factory.SeedSessionAsync();

        await using var context = factory.CreateDbContext();
        var repository = new SessionRepository(context);
        var firstTimestamp = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var secondTimestamp = firstTimestamp.AddMinutes(15);

        await repository.AddAttendanceLogAsync(session.Id, student.Id, Session.StudentStatus.Active, firstTimestamp);
        await repository.AddAttendanceLogAsync(session.Id, student.Id, Session.StudentStatus.InActive, secondTimestamp);

        var logs = await repository.GetAttendanceLogsAsync(session.Id);

        Assert.Equal(2, logs.Count);
        Assert.Equal(TimeSpan.FromMinutes(15), logs[0].Duration);
        Assert.Equal(TimeSpan.Zero, logs[1].Duration);
    }

    [Fact]
    public async Task FinalizeAttendanceLogsAsync_WhenLatestSegmentIsOpen_ClosesLatestDuration()
    {
        using var factory = new TestDbContextFactory();
        var (_, student, session) = await factory.SeedSessionAsync();

        await using var context = factory.CreateDbContext();
        var repository = new SessionRepository(context);
        var startTimestamp = new DateTime(2025, 1, 1, 10, 5, 0, DateTimeKind.Utc);
        var endTimestamp = startTimestamp.AddMinutes(20);

        await repository.AddAttendanceLogAsync(session.Id, student.Id, Session.StudentStatus.Active, startTimestamp);
        await repository.FinalizeAttendanceLogsAsync(session.Id, endTimestamp);

        var logs = await repository.GetAttendanceLogsAsync(session.Id);

        Assert.Single(logs);
        Assert.Equal(TimeSpan.FromMinutes(20), logs[0].Duration);
    }
}
