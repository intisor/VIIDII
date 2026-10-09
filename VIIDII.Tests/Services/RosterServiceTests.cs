using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Tests.TestInfrastructure;
using Xunit;

namespace VIIDII.Tests.Services;

/// <summary>
/// End-to-end roster import against SQLite, covering the persistence contract.
/// </summary>
public class RosterServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private RosterService CreateService(ViidiiDbContext context) =>
        new(context, new PasswordHasher<User>());

    public void Dispose() => _factory.Dispose();

    private async Task<Session> SeedSessionAsync(string lecturerMatricNo = "LEC100")
    {
        var context = _factory.CreateDbContext();

        var lecturer = new User
        {
            Name = "Lecturer",
            MatricNo = lecturerMatricNo,
            PasswordHash = "hash",
            Role = Role.Lecturer
        };

        // Save first so lecturer.Id is populated before it is used as a foreign key.
        context.Users.Add(lecturer);
        await context.SaveChangesAsync();

        var session = new Session
        {
            SessionId = "20260101-ROSTER",
            LecturerId = lecturer.Id,
            LecturerMatricNo = lecturer.MatricNo,
            Title = "Roster Import Test",
            StartTime = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        };

        context.Sessions.Add(session);
        await context.SaveChangesAsync();

        return session;
    }

    [Fact]
    public async Task ImportAsync_CreatesRosterEntriesAndUserAccounts()
    {
        var session = await SeedSessionAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        Matric Number,Full Name,Email,Department
        CSC/2021/001,Aisha Bello,a.bello@uni.edu.ng,Computer Science
        CSC/2021/002,Babatunde Cole,b.cole@uni.edu.ng,Computer Science
        CSC/2021/003,Chioma Nwosu,c.nwosu@uni.edu.ng,Computer Science
        """;

        var result = await service.ImportAsync(session.Id, csv);

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Imported);
        Assert.Equal(3, result.UserAccountsCreated);
        Assert.Equal(3, result.Passcodes.Count);

        var entries = await service.GetForSessionAsync(session.Id);
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Identifier == "CSC/2021/001" && e.DisplayName == "Aisha Bello");
        Assert.All(entries, e => Assert.Equal("csv", e.Source));

        var users = await context.Users.Where(u => u.MatricNo.StartsWith("CSC/")).ToListAsync();
        Assert.Equal(3, users.Count);
        Assert.All(users, u => Assert.Equal(Role.Student, u.Role));
    }

    [Fact]
    public async Task ImportAsync_RejectsDuplicateIdentifiersRatherThanColliding()
    {
        var session = await SeedSessionAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        Matric Number,Full Name
        CSC/001,Aisha Bello
        CSC/001,Babatunde Cole
        """;

        var result = await service.ImportAsync(session.Id, csv);

        // Duplicated identifiers make the column non-unique, so the schema
        // analyser rejects the file outright rather than importing half of it.
        // Importing would let two attendees share one join code, which makes the
        // attendance record unprovable.
        Assert.Equal(0, result.Imported);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("identifier column", StringComparison.OrdinalIgnoreCase));

        var entries = await service.GetForSessionAsync(session.Id);
        Assert.Empty(entries);
        Assert.Empty(await context.Users.Where(u => u.MatricNo == "CSC/001").ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_LinksExistingUsersWithoutDuplicatingThem()
    {
        var session = await SeedSessionAsync();

        using (var seedContext = _factory.CreateDbContext())
        {
            seedContext.Users.Add(new User
            {
                Name = "Existing Student",
                MatricNo = "EXIST001",
                PasswordHash = "hash",
                Role = Role.Student
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        Matric Number,Full Name
        EXIST001,Existing Student
        NEW001,New Student
        """;

        var result = await service.ImportAsync(session.Id, csv);

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.LinkedToExistingUser);
        Assert.Equal(1, result.UserAccountsCreated);

        var matching = await context.Users.Where(u => u.MatricNo == "EXIST001").ToListAsync();
        Assert.Single(matching);
    }

    [Fact]
    public async Task ImportAsync_FailsClearlyWhenColumnsCannotBeIdentified()
    {
        var session = await SeedSessionAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        S/N,Score
        1,70
        2,85
        """;

        var result = await service.ImportAsync(session.Id, csv);

        Assert.Equal(0, result.Imported);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(await service.GetForSessionAsync(session.Id));
    }

    [Fact]
    public async Task ImportAsync_ReturnsErrorForUnknownSession()
    {
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        var result = await service.ImportAsync(9999, "Matric,Name\nCSC/001,Aisha Bello");

        Assert.Equal(0, result.Imported);
        Assert.Contains(result.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_DoesNotPersistAnything()
    {
        var session = await SeedSessionAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        var (_, analysis) = service.Preview("Matric Number,Full Name\nCSC/001,Aisha Bello");

        Assert.True(analysis.IsUsable);
        Assert.Empty(await service.GetForSessionAsync(session.Id));
        Assert.Empty(await context.Users.Where(u => u.MatricNo == "CSC/001").ToListAsync());
    }

    [Fact]
    public async Task ClaimSeat_BlocksSecondDeviceFromReusingAPasscode()
    {
        var session = await SeedSessionAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        await service.ImportAsync(session.Id, "Matric Number,Full Name\nCSC/001,Aisha Bello");
        var entry = (await service.GetForSessionAsync(session.Id)).Single();

        // First device claims.
        Assert.True(await service.ClaimSeatAsync(entry.Id, "device-token-a"));

        // Same device reconnecting is fine.
        Assert.True(await service.ClaimSeatAsync(entry.Id, "device-token-a"));

        // A different device presenting the same passcode is refused.
        Assert.False(await service.ClaimSeatAsync(entry.Id, "device-token-b"));
    }

    [Fact]
    public async Task ImportAsync_AcceptsJsonPayload()
    {
        var session = await SeedSessionAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string json = """
        [
          { "matricNumber": "CSC/001", "fullName": "Aisha Bello" },
          { "matricNumber": "CSC/002", "fullName": "Babatunde Cole" }
        ]
        """;

        var result = await service.ImportAsync(session.Id, json);

        Assert.Equal(2, result.Imported);
        Assert.All(await service.GetForSessionAsync(session.Id), e => Assert.Equal("json", e.Source));
    }
}