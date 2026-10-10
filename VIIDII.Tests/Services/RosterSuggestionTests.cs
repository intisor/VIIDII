using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Services.Roster;
using VIIDII.Tests.TestInfrastructure;
using Xunit;

namespace VIIDII.Tests.Services;

/// <summary>
/// Tests for the lecturer-facing suggestions: ranked alternatives, the
/// confidence prompts, and the manual override.
/// </summary>
public class RosterSuggestionTests
{
    private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) =>
        rows.Select(r => (IReadOnlyList<string>)r).ToList();

    [Fact]
    public void OffersRankedAlternativesSoLecturerCanOverride()
    {
        var headers = new[] { "Student Code", "Candidate Number", "Full Name" };
        var rows = Rows(
            new[] { "SC001", "CAN001", "Aisha Bello" },
            new[] { "SC002", "CAN002", "Babatunde Cole" },
            new[] { "SC003", "CAN003", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.True(analysis.IsUsable);
        // Both identifier-like columns must be offered, not just the winner.
        Assert.True(analysis.RankedIdentifiers.Count >= 2);
        Assert.Contains(analysis.RankedIdentifiers, c => c.Name == "Student Code");
        Assert.Contains(analysis.RankedIdentifiers, c => c.Name == "Candidate Number");
        Assert.True(analysis.RankedIdentifiers[0].IdentifierScore >= analysis.RankedIdentifiers[1].IdentifierScore);
    }

    [Fact]
    public void WarnsWhenTheIdentifierLooksLikeAPhoneNumber()
    {
        // Digits are alphanumeric and phone numbers have consistent length, so a
        // phone column scores 0.70 as an identifier. That needs a human check.
        var headers = new[] { "Phone", "Full Name" };
        var rows = Rows(
            new[] { "08031234567", "Aisha Bello" },
            new[] { "08039876543", "Babatunde Cole" },
            new[] { "08057654321", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.True(analysis.IsUsable);
        Assert.Contains(analysis.NeedsConfirmation, p => p.Contains("phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WarnsWhenTheWinningColumnOnlyNarrowlyBeatsTheRunnerUp()
    {
        // Two plausible identifier columns of similar strength.
        var headers = new[] { "Student Code", "Registration No", "Full Name" };
        var rows = Rows(
            new[] { "SC001", "R001", "Aisha Bello" },
            new[] { "SC002", "R002", "Babatunde Cole" },
            new[] { "SC003", "R003", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.True(analysis.IsUsable);
        Assert.True(analysis.RankedIdentifiers.Count >= 2);
        Assert.Contains(analysis.NeedsConfirmation,
            p => p.Contains("narrowly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SerialNumberColumnIsNotOfferedAsAJoinKey()
    {
        // Regression: "Index" is an exact header hint and has perfectly consistent
        // length, which alone scored 0.55 — enough to be offered as a join key.
        // A shape gate is required so students are never asked to type row numbers.
        var headers = new[] { "Index", "Full Name" };
        var rows = Rows(
            new[] { "1", "Aisha Bello" },
            new[] { "2", "Babatunde Cole" },
            new[] { "3", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.Null(analysis.IdentifierColumn);
        Assert.DoesNotContain(analysis.RankedIdentifiers, c => c.Name == "Index");
        Assert.False(analysis.IsUsable);
    }

    [Fact]
    public void MatricNumberHeaderDoesNotTriggerThePhoneWarning()
    {
        // Regression: "number" was in the phone header hints, so "Matric Number"
        // scored 0.51 as a phone column and every clean file raised a false warning.
        var headers = new[] { "Matric Number", "Full Name" };
        var rows = Rows(
            new[] { "CSC/2021/001", "Aisha Bello" },
            new[] { "CSC/2021/002", "Babatunde Cole" },
            new[] { "CSC/2021/003", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.Equal("Matric Number", analysis.IdentifierColumn!.Name);
        Assert.DoesNotContain(analysis.NeedsConfirmation,
            p => p.Contains("phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProducesNoConfirmationPromptsForACleanFile()
    {
        var headers = new[] { "Matric Number", "Full Name", "Department" };
        var rows = Rows(
            new[] { "CSC/2021/001", "Aisha Bello", "Computer Science" },
            new[] { "CSC/2021/002", "Babatunde Cole", "Computer Science" },
            new[] { "CSC/2021/003", "Chioma Nwosu", "Computer Science" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.True(analysis.IsUsable);
        Assert.Empty(analysis.NeedsConfirmation);
    }
}

/// <summary>
/// Tests for explicit lecturer override of the automatic column pick.
/// </summary>
public class RosterColumnMappingTests
{
    private static readonly string[] Headers =
        { "S/N", "Student Code", "Candidate Number", "Full Name" };

    [Fact]
    public void IndexOf_FindsHeaderCaseInsensitively()
    {
        var mapping = new RosterColumnMapping { IdentifierHeader = "student code" };

        Assert.Equal(1, mapping.IndexOf(Headers, "student code"));
    }

    [Fact]
    public void IndexOf_ReturnsMinusOneForMissingHeader()
    {
        var mapping = new RosterColumnMapping();

        Assert.Equal(-1, mapping.IndexOf(Headers, "Does Not Exist"));
        Assert.Equal(-1, mapping.IndexOf(Headers, null));
    }

    [Fact]
    public void Validate_RejectsMissingIdentifierColumn()
    {
        var mapping = new RosterColumnMapping
        {
            IdentifierHeader = "No Such Column",
            NameHeader = "Full Name"
        };

        var error = mapping.Validate(Headers);

        Assert.NotNull(error);
        Assert.Contains("not found", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsIdentifierAndNameBeingTheSameColumn()
    {
        var mapping = new RosterColumnMapping
        {
            IdentifierHeader = "Full Name",
            NameHeader = "Full Name"
        };

        var error = mapping.Validate(Headers);

        Assert.NotNull(error);
        Assert.Contains("same column", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsADistinctPair()
    {
        var mapping = new RosterColumnMapping
        {
            IdentifierHeader = "Student Code",
            NameHeader = "Full Name"
        };

        Assert.Null(mapping.Validate(Headers));
    }
}

/// <summary>
/// Tests that an explicit mapping actually overrides the automatic pick.
/// </summary>
public class RosterOverrideIntegrationTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<Class> SeedClassAsync()
    {
        var context = _factory.CreateDbContext();

        var lecturer = new User
        {
            Name = "Lecturer",
            MatricNo = "LEC200",
            PasswordHash = "hash",
            Role = Role.Lecturer
        };

        context.Users.Add(lecturer);
        await context.SaveChangesAsync();

        var seeded = new Class
        {
            Code = "CS100",
            Name = "Data Structures",
            LecturerId = lecturer.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.Classes.Add(seeded);
        await context.SaveChangesAsync();
        return seeded;
    }

    [Fact]
    public async Task ExplicitMappingOverridesTheAutomaticPick()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = new RosterService(context, new PasswordHasher<User>());

        const string csv = """
        S/N,Student Code,Candidate Number,Full Name
        1,SC001,CAN001,Aisha Bello
        2,SC002,CAN002,Babatunde Cole
        3,SC003,CAN003,Chioma Nwosu
        """;

        var analysis = service.Preview(csv).Analysis;
        Assert.Equal("Student Code", analysis.IdentifierColumn!.Name);

        // Lecturer confirms Candidate Number instead.
        var mapping = new RosterColumnMapping
        {
            IdentifierHeader = "Candidate Number",
            NameHeader = "Full Name",
            IdentifierPrompt = "Enter your candidate number"
        };

        var result = await service.ImportAsync(seededClass.Id, csv, mapping: mapping);

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Imported);
        Assert.Equal("Candidate Number", result.IdentifierHeader);
        Assert.Equal("Full Name", result.NameHeader);

        var entries = await service.GetForClassAsync(seededClass.Id);
        Assert.All(entries, e => Assert.StartsWith("CAN", e.Identifier));
    }

    [Fact]
    public async Task InvalidMappingIsRejectedWithoutWritingAnything()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = new RosterService(context, new PasswordHasher<User>());

        var mapping = new RosterColumnMapping
        {
            IdentifierHeader = "Does Not Exist",
            NameHeader = "Full Name"
        };

        var result = await service.ImportAsync(
            seededClass.Id,
            "Matric,Name\nCSC/001,Aisha Bello",
            mapping: mapping);

        Assert.Equal(0, result.Imported);
        Assert.Contains(result.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await service.GetForClassAsync(seededClass.Id));
    }

    [Fact]
    public async Task ConfirmedPromptIsPersistedForTheStudentJoinScreen()
    {
        var seededClass = await SeedClassAsync();
        using var context = _factory.CreateDbContext();
        var service = new RosterService(context, new PasswordHasher<User>());

        const string csv = """
        Matric Number,Full Name
        CSC/001,Aisha Bello
        CSC/002,Babatunde Cole
        """;

        var mapping = new RosterColumnMapping
        {
            IdentifierHeader = "Matric Number",
            NameHeader = "Full Name",
            IdentifierPrompt = "Enter your matric number"
        };

        await service.ImportAsync(seededClass.Id, csv, mapping: mapping);

        var ClassService = new ClassService(context);
        await ClassService.SetJoinPromptAsync(seededClass.Id, "Enter your matric number", mapping);

        context.ChangeTracker.Clear();

        var reloaded = await context.Classes.AsNoTracking().FirstAsync(c => c.Id == seededClass.Id);

        Assert.Equal("Enter your matric number", reloaded.JoinPrompt);
        Assert.NotNull(reloaded.RosterMappingJson);
        Assert.Contains("Matric Number", reloaded.RosterMappingJson!);
    }
}