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

    [Fact]
    public void ParsesSingleColumnPhoneNumbersWithoutHeader()
    {
        var parsed = RosterParser.Parse(
            "08031234567\n" +
            "08091234567\n" +
            "07011223344\n");

        Assert.Single(parsed.Headers);
        Assert.Equal("Phone Number", parsed.Headers[0]);
        Assert.Equal(3, parsed.Rows.Count);
        Assert.Equal("08031234567", parsed.Rows[0][0]);
        Assert.Equal("08091234567", parsed.Rows[1][0]);
        Assert.Equal("07011223344", parsed.Rows[2][0]);
    }

    [Fact]
    public void ParsesSingleColumnPhoneNumbersWithHeader()
    {
        var parsed = RosterParser.Parse(
            "Phone Number\n" +
            "08031234567\n" +
            "08091234567\n");

        Assert.Single(parsed.Headers);
        Assert.Equal("Phone Number", parsed.Headers[0]);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("08031234567", parsed.Rows[0][0]);
    }

    [Fact]
    public void ParsesSingleColumnEmailsWithoutHeader()
    {
        var parsed = RosterParser.Parse(
            "student1@uni.edu.ng\n" +
            "student2@uni.edu.ng\n");

        Assert.Single(parsed.Headers);
        Assert.Equal("Email", parsed.Headers[0]);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("student1@uni.edu.ng", parsed.Rows[0][0]);
    }

    [Fact]
    public void ParsesJsonArrayOfStrings()
    {
        var parsed = RosterParser.Parse("""
        [
            "08031234567",
            "08091234567"
        ]
        """);

        Assert.Single(parsed.Headers);
        Assert.Equal("Phone Number", parsed.Headers[0]);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("08031234567", parsed.Rows[0][0]);
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

    [Theory]
    [InlineData("08031234567", "08031234567", true)]
    [InlineData("08031234567", "+2348031234567", true)]
    [InlineData("2348031234567", "08031234567", true)]
    [InlineData("+234 803 123 4567", "08031234567", true)]
    [InlineData("080-3123-4567", "08031234567", true)]
    [InlineData("8031234567", "08031234567", true)]
    [InlineData("CSC/2021/001", "csc/2021/001", true)]
    [InlineData("08031234567", "08099999999", false)]
    [InlineData("CSC/2021/001", "CSC/2021/002", false)]
    public void IdentifiersMatch_HandlesPhonesAndCodesFlexibly(string stored, string candidate, bool expected)
    {
        Assert.Equal(expected, RosterService.IdentifiersMatch(stored, candidate));
    }
}