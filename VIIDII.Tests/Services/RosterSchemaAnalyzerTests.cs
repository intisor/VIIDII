using VIIDII.Services.Roster;
using Xunit;

namespace VIIDII.Tests.Services;

/// <summary>
/// Tests for roster schema detection.
///
/// These encode the heuristics that decide which column is the join identifier
/// and which is the person's name. Getting this wrong means students cannot join
/// their own session, so the cases below are deliberately adversarial.
/// </summary>
public class RosterSchemaAnalyzerTests
{
    private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) =>
        rows.Select(r => (IReadOnlyList<string>)r).ToList();

    [Fact]
    public void PicksMatricNumberAsIdentifierAndFullNameAsName()
    {
        var headers = new[] { "S/N", "Matric Number", "Full Name", "Department" };
        var rows = Rows(
            new[] { "1", "CSC/2021/001", "Adeyemi Oluwaseun", "Computer Science" },
            new[] { "2", "CSC/2021/002", "Balogun Chisom", "Computer Science" },
            new[] { "3", "CSC/2021/003", "Chukwu Ngozi Ifeoma", "Computer Science" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.True(analysis.IsUsable);
        Assert.Equal("Matric Number", analysis.IdentifierColumn!.Name);
        Assert.Equal("Full Name", analysis.NameColumn!.Name);
        Assert.Equal("Department", analysis.GroupingColumn!.Name);
    }

    [Fact]
    public void HandlesMismatchedHeaderSpellings()
    {
        // Institutional exports are inconsistent. Same data, different headers.
        var headers = new[] { "regno", "name", "email", "phone" };
        var rows = Rows(
            new[] { "ENG2020001", "Fatima Abdullahi", "f.a@uni.edu.ng", "08031234567" },
            new[] { "ENG2020002", "Grace Nwachukwu", "g.n@uni.edu.ng", "08039876543" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.True(analysis.IsUsable);
        Assert.Equal("regno", analysis.IdentifierColumn!.Name);
        Assert.Equal("name", analysis.NameColumn!.Name);
        Assert.Equal("email", analysis.EmailColumn!.Name);
        Assert.Equal("phone", analysis.PhoneColumn!.Name);
    }

    [Fact]
    public void DoesNotPickSerialNumberColumnAsIdentifier()
    {
        // The trap: "S/N" is unique and numeric, so a naive uniqueness rule picks it.
        // Students must not have to type a row number.
        var headers = new[] { "S/N", "Name" };
        var rows = Rows(
            new[] { "1", "Aisha Bello" },
            new[] { "2", "Babatunde Cole" },
            new[] { "3", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        // With no better column present, nothing should be confidently selected.
        Assert.False(analysis.IsUsable);
    }

    [Fact]
    public void RejectsIdentifierColumnThatRepeats()
    {
        // A duplicated registration number would let two students share one join code.
        var headers = new[] { "Matric Number", "Full Name" };
        var rows = Rows(
            new[] { "CSC/001", "Aisha Bello" },
            new[] { "CSC/001", "Babatunde Cole" },
            new[] { "CSC/002", "Chioma Nwosu" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.Null(analysis.IdentifierColumn);
        Assert.Contains(analysis.Notes, n => n.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DoesNotTreatPersonNameColumnAsIdentifier()
    {
        var headers = new[] { "Full Name", "Matric Number" };
        var rows = Rows(
            new[] { "Aisha Bello", "CSC/001" },
            new[] { "Babatunde Cole", "CSC/002" },
            new[] { "Chioma Nwosu", "CSC/003" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.Equal("Matric Number", analysis.IdentifierColumn!.Name);
        Assert.Equal("Full Name", analysis.NameColumn!.Name);
    }

    [Fact]
    public void HandlesSingleWordNames()
    {
        // Some records hold a single name. A person-name column should still win
        // on the strength of the header.
        var headers = new[] { "Student ID", "Name" };
        var rows = Rows(
            new[] { "STU001", "Emeka" },
            new[] { "STU002", "Tunde" },
            new[] { "STU003", "Yusuf" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.Equal("Student ID", analysis.IdentifierColumn!.Name);
        Assert.Equal("Name", analysis.NameColumn!.Name);
    }

    [Fact]
    public void ReportsUsableFalseWhenNoNameColumnExists()
    {
        var headers = new[] { "Matric Number", "Score" };
        var rows = Rows(
            new[] { "CSC/001", "70" },
            new[] { "CSC/002", "85" },
            new[] { "CSC/003", "62" });

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rows);

        Assert.False(analysis.IsUsable);
        Assert.Contains(analysis.Notes, n => n.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdentifiesBothIdentifierAndNameAcrossWideRoster()
    {
        var headers = new[] { "Mat No", "Surname", "Firstname", "Email", "Level" };
        var rowsEnumerable = new List<IReadOnlyList<string>>();
        for (var i = 1; i <= 150; i++)
        {
            rowsEnumerable.Add(new[]
            {
                $"ENG/2024/{i:D4}",
                $"Surname{i}",
                $"Firstname{i}",
                $"student{i}@uni.edu.ng",
                "Level 200"
            });
        }

        var analysis = RosterSchemaAnalyzer.Analyze(headers, rowsEnumerable);

        Assert.True(analysis.IsUsable);
        Assert.Equal("Mat No", analysis.IdentifierColumn!.Name);
        Assert.Equal(150, analysis.RowCount);
        // "Level" is uniform, so it is not usable as an identifier.
        Assert.NotEqual("Level", analysis.IdentifierColumn!.Name);
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        var analysis = RosterSchemaAnalyzer.Analyze(Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());

        Assert.False(analysis.IsUsable);
        Assert.NotEmpty(analysis.Notes);
    }

    [Fact]
    public void HandlesHeaderOnlyFileWithNoRows()
    {
        var analysis = RosterSchemaAnalyzer.Analyze(
            new[] { "Matric Number", "Full Name" },
            Array.Empty<IReadOnlyList<string>>());

        Assert.Equal(0, analysis.RowCount);
        Assert.Contains(analysis.Notes, n => n.Contains("No data rows", StringComparison.OrdinalIgnoreCase));
    }
}