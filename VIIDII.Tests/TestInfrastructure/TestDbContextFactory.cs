using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;

namespace VIIDII.Tests.TestInfrastructure;

internal sealed class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ViidiiDbContext> _options;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ViidiiDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public ViidiiDbContext CreateDbContext() => new(_options);

    public async Task<(User Lecturer, User Student, Session Session)> SeedSessionAsync()
    {
        using var context = CreateDbContext();

        var lecturer = new User
        {
            Name = "Lecturer One",
            MatricNo = "LEC100",
            PasswordHash = "hash",
            Role = Role.Lecturer
        };

        var student = new User
        {
            Name = "Student One",
            MatricNo = "STD100",
            PasswordHash = "hash",
            Role = Role.Student,
            Department = User.Departments.SoftwareEngineering,
            Level = User.Levels.Level200
        };

        context.Users.AddRange(lecturer, student);
        await context.SaveChangesAsync();

        var session = new Session
        {
            SessionId = "20250101-ABCDEF",
            LecturerId = lecturer.Id,
            LecturerMatricNo = lecturer.MatricNo,
            Title = "Integration Testing",
            AllowedDepartments = [User.Departments.Any],
            AllowedLevels = [User.Levels.Any],
            Status = SessionStatus.Started,
            StartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2025, 1, 1, 9, 55, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc)
        };

        context.Sessions.Add(session);
        await context.SaveChangesAsync();

        return (lecturer, student, session);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
