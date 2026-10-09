using VIIDII.Services;
using VIIDII.Services.Roster;
using Xunit;

namespace VIIDII.Tests.Services;

/// <summary>
/// Tests for roster parsing across CSV, TSV and JSON.
/// </summary>
public class RosterParserTests
{
    [Fact]
    public void ParsesSimpleCsv()
    {
        var parsed = RosterParser.Parse(
            "Matric Number,Full Name,Department\n" +
            "CSC/001,Aisha Bello,Computer Science\n" +
            "CSC/002,Babatunde Cole,Computer Science");

        Assert.Equal(RosterSourceFormat.Csv, parsed.Format);
        Assert.Equal(new[] { "Matric Number", "Full Name", "Department" }, parsed.Headers);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("Aisha Bello", parsed.Rows[0][1]);
    }

    [Fact]
    public void DetectsTabDelimitedInput()
    {
        var parsed = RosterParser.Parse(
            "Matric Number\tFull Name\n" +
            "CSC/001\tAisha Bello");

        Assert.Equal(RosterSourceFormat.Tsv, parsed.Format);
        Assert.Equal(2, parsed.Headers.Count);
    }

    [Fact]
    public void HandlesQuotedFieldsContainingCommas()
    {
        var parsed = RosterParser.Parse(
            "Matric Number,Full Name,Notes\n" +
            "\"CSC/001\",\"Bello, Aisha\",\"Split across, two columns\"");

        Assert.Equal(3, parsed.Headers.Count);
        Assert.Equal("Bello, Aisha", parsed.Rows[0][1]);
        Assert.Equal("Split across, two columns", parsed.Rows[0][2]);
    }

    [Fact]
    public void HandlesEscapedQuotesInsideQuotedField()
    {
        var parsed = RosterParser.Parse(
            "Matric Number,Full Name,Notes\n" +
            "\"CSC/001\",\"Aisha Bello\",\"She said \"\"go home\"\"\"");

        Assert.Equal("She said \"go home\"", parsed.Rows[0][2]);
    }

    [Fact]
    public void ParsesJsonArrayOfObjects()
    {
        const string json = """
        [
          { "matricNumber": "CSC/001", "fullName": "Aisha Bello" },
          { "matricNumber": "CSC/002", "fullName": "Babatunde Cole" }
        ]
        """;

        var parsed = RosterParser.Parse(json);

        Assert.Equal(RosterSourceFormat.Json, parsed.Format);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Contains("matricNumber", parsed.Headers);
        Assert.Equal("Aisha Bello", parsed.Rows[0][1]);
    }

    [Fact]
    public void UnwrapsJsonArrayNestedUnderDataKey()
    {
        const string json = """
        { "data": [ { "matric": "CSC/001", "name": "Aisha Bello" } ] }
        """;

        var parsed = RosterParser.Parse(json);

        Assert.Equal(1, parsed.Rows.Count);
        Assert.Contains(parsed.Warnings, w => w.Contains("unwrapped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AlignsRowsWithDifferingPropertySets()
    {
        // Row 2 lacks "email". The column must still exist and be blank there,
        // otherwise the remaining values shift and names get mangled.
        const string json = """
        [
          { "matric": "CSC/001", "name": "Aisha Bello", "email": "a@uni.edu.ng" },
          { "matric": "CSC/002", "name": "Babatunde Cole" }
        ]
        """;

        var parsed = RosterParser.Parse(json);

        Assert.Equal(3, parsed.Headers.Count);
        Assert.Equal("CSC/002", parsed.Rows[1][0]);
        Assert.Equal("Babatunde Cole", parsed.Rows[1][1]);
        Assert.Equal(string.Empty, parsed.Rows[1][2]);
    }

    [Fact]
    public void PadsRowsWithMissingTrailingCells()
    {
        var parsed = RosterParser.Parse(
            "Matric,Name,Department,Level\n" +
            "CSC/001,Aisha Bello,Computer Science");

        Assert.Single(parsed.Rows);
        Assert.Equal(4, parsed.Rows[0].Count);
        Assert.Equal(string.Empty, parsed.Rows[0][3]);
        Assert.Contains(parsed.Warnings, w => w.Contains("Row 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalisesWindowsLineEndings()
    {
        var parsed = RosterParser.Parse("Matric,Name\r\nCSC/001,Aisha Bello\r\nCSC/002,Babatunde Cole");

        Assert.Equal(2, parsed.Rows.Count);
    }

    [Fact]
    public void HandlesByteOrderMark()
    {
        var parsed = RosterParser.Parse("\uFEFFMatric,Name\nCSC/001,Aisha Bello");

        Assert.Equal("Matric", parsed.Headers[0]);
    }

    [Fact]
    public void ReportsEmptyContentRatherThanThrowing()
    {
        var parsed = RosterParser.Parse("   ");

        Assert.Empty(parsed.Rows);
        Assert.NotEmpty(parsed.Warnings);
    }

    [Fact]
    public void ReportsInvalidJsonRatherThanThrowing()
    {
        var parsed = RosterParser.Parse("[ { broken json");

        Assert.NotEmpty(parsed.Warnings);
    }

    [Fact]
    public void SkipsBlankLines()
    {
        var parsed = RosterParser.Parse(
            "Matric,Name\n" +
            "CSC/001,Aisha Bello\n" +
            "\n" +
            "CSC/002,Babatunde Cole\n");

        Assert.Equal(2, parsed.Rows.Count);
    }
}

/// <summary>
/// Tests for the two-field identity match used at student join.
/// </summary>
public class NormaliseForComparisonTests
{
    [Theory]
    [InlineData("Aisha Bello", "aisha bello")]
    [InlineData("AISHA BELLO", "aisha bello")]
    [InlineData("  Aisha   Bello  ", "aisha bello")]
    [InlineData("Aisha\tBello", "aisha bello")]
    [InlineData("Bello, Aisha", "aisha bello")]
    [InlineData("BELLO , AISHA", "aisha bello")]
    public void FoldsCaseSpacingAndSurnameForenameOrder(string input, string expected)
    {
        Assert.Equal(expected, RosterService.NormaliseForComparison(input));
    }

    [Fact]
    public void LeavesAnOrdinaryNameUnchangedApartFromCase()
    {
        Assert.Equal("babatunde cole", RosterService.NormaliseForComparison("Babatunde Cole"));
    }

    [Fact]
    public void DoesNotReorderWhenTheCommaIsInsideAName()
    {
        // "Bello, Aisha" is surname-first. "Bello A. Junior" is not.
        Assert.Equal("junior bello", RosterService.NormaliseForComparison("Bello, Junior"));
    }
}