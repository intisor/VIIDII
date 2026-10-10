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

    private async Task<Class> SeedClassAsync(string lecturerMatricNo = "LEC100")
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

        var seeded = new Class
        {
            Code = "CS100",
            Name = "Roster Import Test",
            LecturerId = lecturer.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.Classes.Add(seeded);
        await context.SaveChangesAsync();

        return seeded;
    }

    [Fact]
    public async Task ImportAsync_CreatesRosterEntriesAndUserAccounts()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        Matric Number,Full Name,Email,Department
        CSC/2021/001,Aisha Bello,a.bello@uni.edu.ng,Computer Science
        CSC/2021/002,Babatunde Cole,b.cole@uni.edu.ng,Computer Science
        CSC/2021/003,Chioma Nwosu,c.nwosu@uni.edu.ng,Computer Science
        """;

        var result = await service.ImportAsync(seededClass.Id, csv);

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Imported);
        Assert.Equal(3, result.UserAccountsCreated);
        // No passcodes are generated: students join with matric number and name.

        var entries = await service.GetForClassAsync(seededClass.Id);
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
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        Matric Number,Full Name
        CSC/001,Aisha Bello
        CSC/001,Babatunde Cole
        """;

        var result = await service.ImportAsync(seededClass.Id, csv);

        // Duplicated identifiers make the column non-unique, so the schema
        // analyser rejects the file outright rather than importing half of it.
        // Importing would let two attendees share one join code, which makes the
        // attendance record unprovable.
        Assert.Equal(0, result.Imported);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("identifier column", StringComparison.OrdinalIgnoreCase));

        var entries = await service.GetForClassAsync(seededClass.Id);
        Assert.Empty(entries);
        Assert.Empty(await context.Users.Where(u => u.MatricNo == "CSC/001").ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_LinksExistingUsersWithoutDuplicatingThem()
    {
        var seededClass = await SeedClassAsync();

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

        var result = await service.ImportAsync(seededClass.Id, csv);

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.LinkedToExistingUser);
        Assert.Equal(1, result.UserAccountsCreated);

        var matching = await context.Users.Where(u => u.MatricNo == "EXIST001").ToListAsync();
        Assert.Single(matching);
    }

    [Fact]
    public async Task ImportAsync_FailsClearlyWhenColumnsCannotBeIdentified()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string csv = """
        S/N,Score
        1,70
        2,85
        """;

        var result = await service.ImportAsync(seededClass.Id, csv);

        Assert.Equal(0, result.Imported);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(await service.GetForClassAsync(seededClass.Id));
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
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        var (_, analysis) = service.Preview("Matric Number,Full Name\nCSC/001,Aisha Bello");

        Assert.True(analysis.IsUsable);
        Assert.Empty(await service.GetForClassAsync(seededClass.Id));
        Assert.Empty(await context.Users.Where(u => u.MatricNo == "CSC/001").ToListAsync());
    }

    [Fact]
    public async Task ResolveAttendee_MatchesOnMatricNumberAndName()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        await service.ImportAsync(seededClass.Id,
            "Matric Number,Full Name\nCSC/001,Aisha Bello\nCSC/002,Babatunde Cole");

        var match = await service.ResolveAttendeeAsync(seededClass.Id, "CSC/001", "Aisha Bello");

        Assert.NotNull(match);
        Assert.Equal("Aisha Bello", match!.DisplayName);
    }

    [Fact]
    public async Task ResolveAttendee_RejectsAWhenTheNameDoesNotMatch()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        await service.ImportAsync(seededClass.Id,
            "Matric Number,Full Name\nCSC/001,Aisha Bello\nCSC/002,Babatunde Cole");

        // Right matric number, wrong name. Both fields must agree.
        Assert.Null(await service.ResolveAttendeeAsync(seededClass.Id, "CSC/001", "Babatunde Cole"));
    }

    [Theory]
    [InlineData("csc/001", "aisha bello")]
    [InlineData("CSC/001", "AISHA BELLO")]
    [InlineData(" CSC/001 ", "Aisha  Bello")]
    public async Task ResolveAttendee_ToleratesCaseAndSpacing(string identifier, string name)
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        await service.ImportAsync(seededClass.Id, "Matric Number,Full Name\nCSC/001,Aisha Bello");

        var match = await service.ResolveAttendeeAsync(seededClass.Id, identifier, name);

        Assert.NotNull(match);
    }

    [Fact]
    public async Task ResolveAttendee_ReturnsNullForUnknownDetails()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        await service.ImportAsync(seededClass.Id, "Matric Number,Full Name\nCSC/001,Aisha Bello");

        Assert.Null(await service.ResolveAttendeeAsync(seededClass.Id, "CSC/999", "Nobody Here"));
        Assert.Null(await service.ResolveAttendeeAsync(seededClass.Id, "", ""));
    }

    [Fact]
    public async Task ClaimSeat_BlocksSecondDeviceFromHoldingTheSameSeat()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        await service.ImportAsync(seededClass.Id, "Matric Number,Full Name\nCSC/001,Aisha Bello");
        var entry = (await service.GetForClassAsync(seededClass.Id)).Single();

        // First device claims.
        Assert.True(await service.ClaimSeatAsync(entry.Id, "device-token-a"));

        // Same device reconnecting is fine.
        Assert.True(await service.ClaimSeatAsync(entry.Id, "device-token-a"));

        // A different device holding the same seat is refused.
        Assert.False(await service.ClaimSeatAsync(entry.Id, "device-token-b"));
    }

    [Fact]
    public async Task ImportAsync_AcceptsJsonPayload()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = CreateService(context);

        const string json = """
        [
          { "matricNumber": "CSC/001", "fullName": "Aisha Bello" },
          { "matricNumber": "CSC/002", "fullName": "Babatunde Cole" }
        ]
        """;

        var result = await service.ImportAsync(seededClass.Id, json);

        Assert.Equal(2, result.Imported);
        Assert.All(await service.GetForClassAsync(seededClass.Id), e => Assert.Equal("json", e.Source));
    }
}