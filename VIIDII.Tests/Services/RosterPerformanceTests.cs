using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Services.Roster;
using VIIDII.Tests.TestInfrastructure;
using Xunit;
using Xunit.Abstractions;

namespace VIIDII.Tests.Services;

/// <summary>
/// Timing harness for roster processing at realistic cohort sizes.
///
/// These are measurements, not pass/fail thresholds on the maths. The point is to
/// know where the time actually goes before optimising the wrong thing.
/// </summary>
public class RosterPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TestDbContextFactory _factory = new();

    public RosterPerformanceTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _factory.Dispose();

    private static readonly string[] FirstNames =
    {
        "Aisha", "Babatunde", "Chioma", "Emeka", "Yusuf", "Zainab", "Oluwaseun", "Ngozi",
        "Ibrahim", "Folake", "Chukwuemeka", "Adaeze", "Sadiq", "Bukola", "Ifeanyi", "Halima",
        "Tunde", "Amaka", "Musa", "Uche", "Fatima", "Segun", "Kemi", "Aliyu",
        "Blessing", "Kwame", "Nnamdi", "Suleiman", "Omolara", "Yewande", "Toyin", "Adaobi"
    };

    private static readonly string[] LastNames =
    {
        "Bello", "Cole", "Nwosu", "Obi", "Lawal", "Adeyemi", "Ifeoma", "Balogun",
        "Eze", "Okafor", "Chukwu", "Adebayo", "Okonkwo", "Mohammed", "Oyelaran", "Nwachukwu",
        "Sanni", "Emenike", "Bamidele", "Aluko", "Onyeka", "Danjuma", "Fashola", "Ibeh",
        "Ogunleye", "Salisu", "Anyanwu", "Ene", "Akinyemi", "Ogundipe", "Ojo", "Okoro"
    };

    private static string BuildCsv(int rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("S/N,Matric Number,Full Name,Email,Phone,Department");

        for (var i = 0; i < rows; i++)
        {
            // Independent cycles so names stay unique up to 32 x 32 = 1024 combos.
            var first = FirstNames[i % FirstNames.Length];
            var last = LastNames[(i / FirstNames.Length) % LastNames.Length];
            sb.AppendLine(
                $"{i + 1},CSC/2021/{i + 1:D5},{first} {last}," +
                $"student{i + 1}@uni.edu.ng,0803{i + 1:D7},Computer Science");
        }

        return sb.ToString();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void Measure_ParsingAndAnalysis(int rowCount)
    {
        var csv = BuildCsv(rowCount);

        // Parse
        var sw = Stopwatch.StartNew();
        var parsed = RosterParser.Parse(csv);
        var parseMs = sw.ElapsedMilliseconds;

        // Analyse
        sw.Restart();
        var analysis = RosterSchemaAnalyzer.Analyze(parsed.Headers, parsed.Rows);
        var analyseMs = sw.ElapsedMilliseconds;

        _output.WriteLine(
            $"rows={rowCount,-6} parse={parseMs,5}ms  analyse={analyseMs,5}ms  " +
            $"identifier={analysis.IdentifierColumn?.Name}  name={analysis.NameColumn?.Name}");

        Assert.True(analysis.IsUsable);
    }

    [Fact]
    public void Measure_At5000Rows()
    {
        // The synthetic generator only produces 32x32 = 1024 distinct name pairs, so
        // at 5000 rows names repeat. That is a property of the test data, not a
        // product defect: the analyser is right to refuse a non-unique name column,
        // because two attendees sharing a display name is unprovable at recap time.
        const int rowCount = 5000;
        var csv = BuildCsv(rowCount);

        var sw = Stopwatch.StartNew();
        var parsed = RosterParser.Parse(csv);
        var parseMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var analysis = RosterSchemaAnalyzer.Analyze(parsed.Headers, parsed.Rows);
        var analyseMs = sw.ElapsedMilliseconds;

        _output.WriteLine(
            $"rows={rowCount,-6} parse={parseMs,5}ms  analyse={analyseMs,5}ms  " +
            $"identifier={analysis.IdentifierColumn?.Name}  name={analysis.NameColumn?.Name ?? "(null)"}");

        // The identifier column is still fine — matric numbers never repeat.
        Assert.Equal("Matric Number", analysis.IdentifierColumn?.Name);
        // The name column is correctly rejected as non-unique.
        Assert.Null(analysis.NameColumn);
        Assert.False(analysis.IsUsable);
    }

    [Theory]
    [InlineData(1000)]
    public void Measure_FullImport_IncludesDatabaseWrites(int rowCount)
    {
        var csv = BuildCsv(rowCount);

        var context = _factory.CreateDbContext();
        var service = new RosterService(context, new PasswordHasher<User>());

        var lecturer = new User
        {
            Name = "Lecturer",
            MatricNo = "LEC300",
            PasswordHash = "hash",
            Role = Role.Lecturer
        };
        context.Users.Add(lecturer);
        context.SaveChanges();

        var seeded = new Class
        {
            Code = "PERF1",
            Name = "Perf",
            LecturerId = lecturer.Id,
            CreatedAt = DateTime.UtcNow
        };
        context.Classes.Add(seeded);
        context.SaveChanges();

        var sw = Stopwatch.StartNew();
        var result = service.ImportAsync(seeded.Id, csv).GetAwaiter().GetResult();
        var importMs = sw.ElapsedMilliseconds;

        _output.WriteLine(
            $"import rows={rowCount} took {importMs}ms " +
            $"({(double)importMs / rowCount:F2}ms/student), created {result.UserAccountsCreated}");

        Assert.Equal(rowCount, result.Imported);
    }

    [Fact]
    public void Measure_WhereTheTimeGoes()
    {
        var rows = 1000;

        // Isolate: parsing only
        var csv = BuildCsv(rows);
        var sw = Stopwatch.StartNew();
        var parsed = RosterParser.Parse(csv);
        var parseMs = sw.ElapsedMilliseconds;

        // Isolate: per-column profiling without scoring
        sw.Restart();
        var distinctWork = 0;
        for (var c = 0; c < parsed.Headers.Count; c++)
        {
            var values = parsed.Rows
                .Where(r => c < r.Count)
                .Select(r => (r[c] ?? string.Empty).Trim())
                .Where(v => v.Length > 0)
                .ToList();
            distinctWork += values.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
        var distinctMs = sw.ElapsedMilliseconds;

        // Isolate: full analysis
        sw.Restart();
        RosterSchemaAnalyzer.Analyze(parsed.Headers, parsed.Rows);
        var fullMs = sw.ElapsedMilliseconds;

        _output.WriteLine($"rows={rows}");
        _output.WriteLine($"  parse only:            {parseMs}ms");
        _output.WriteLine($"  column extract+distinct: {distinctMs}ms");
        _output.WriteLine($"  full analysis:         {fullMs}ms");
        _output.WriteLine($"  scoring overhead:      {fullMs - distinctMs}ms (regex passes)");
        _output.WriteLine($"  per student (analysis): {((double)fullMs / rows):F3}ms");

        var analysis = RosterSchemaAnalyzer.Analyze(parsed.Headers, parsed.Rows);
        foreach (var c in analysis.Columns)
        {
            _output.WriteLine(
                $"    {c.Name,-16} uniq={c.Uniqueness:F2} id={c.IdentifierScore:F2} " +
                $"shape={c.IdentifierShapeScore:F2} name={c.PersonNameScore:F2} sample='{c.Samples.FirstOrDefault()}'");
        }

        _output.WriteLine($"  picked identifier: {analysis.IdentifierColumn?.Name ?? "(null)"}");
        _output.WriteLine($"  picked name:       {analysis.NameColumn?.Name ?? "(null)"}");
        _output.WriteLine($"  rankedIdentifiers: {string.Join(", ", analysis.RankedIdentifiers.Select(n => n.Name))}");
        _output.WriteLine($"  rankedNames:       {string.Join(", ", analysis.RankedNames.Select(n => n.Name))}");

        Assert.True(distinctWork > 0);
    }
}