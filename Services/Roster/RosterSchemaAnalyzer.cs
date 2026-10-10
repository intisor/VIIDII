using System.Globalization;
using System.Text.RegularExpressions;

namespace VIIDII.Services.Roster;

/// <summary>
/// A column discovered in an imported roster.
/// </summary>
public class ColumnProfile
{
    public string Name { get; init; } = string.Empty;
    public int Index { get; init; }

    /// <summary>Distinct non-empty values across all rows.</summary>
    public int DistinctCount { get; init; }

    /// <summary>Total non-empty values. Rows with a blank cell are ignored.</summary>
    public int PopulatedCount { get; init; }

    /// <summary>0.0 – 1.0. Ratio of distinct values to populated values.</summary>
    public double Uniqueness { get; init; }

    /// <summary>0.0 – 1.0. How strongly the header and value shape look like a personal name.</summary>
    public double PersonNameScore { get; init; }

    /// <summary>0.0 – 1.0. How strongly this looks like a machine-issued identifier.</summary>
    public double IdentifierScore { get; init; }

    /// <summary>
    /// Raw shape match rate, independent of header hints. Separate from
    /// IdentifierScore because a serial-number column can score well on header
    /// and length consistency while matching no identifier shape at all.
    /// </summary>
    public double IdentifierShapeScore { get; init; }

    /// <summary>
    /// True when the values form a consecutive run of integers (1,2,3… or
    /// 101,102,103…). That is a definitive row-number signature and disqualifies
    /// the column as a join key.
    ///
    /// Needed because a shape-only gate is not enough: past row 99 a single digit
    /// serial reaches three characters and starts matching identifier patterns, so
    /// a 1000-row "S/N" column scored 0.62 and was offered to lecturers.
    /// </summary>
    public bool LooksLikeSerialSequence { get; init; }

    /// <summary>0.0 – 1.0. How strongly this looks like an email address.</summary>
    public double EmailScore { get; init; }

    /// <summary>0.0 – 1.0. How strongly this looks like a phone number.</summary>
    public double PhoneScore { get; init; }

    /// <summary>0.0 – 1.0. How strongly this looks like a department, level or unit.</summary>
    public double GroupingScore { get; init; }

    public double AverageLength { get; init; }

    public IReadOnlyList<string> Samples { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when the header and value shape together read as an institutional
    /// identifier.
    ///
    /// The shape gate is essential. Without it a "S/N" or "Index" column scores
    /// 0.55 purely from its header hint plus length consistency, and would be
    /// offered as a join key — forcing students to type row numbers.
    /// </summary>
    public bool IsLikelyIdentifier =>
        IdentifierScore >= 0.5
        && Uniqueness >= 0.95
        && IdentifierShapeScore >= 0.5
        && !LooksLikeSerialSequence;

    /// <summary>True when this column reads as a person's name.</summary>
    public bool IsLikelyPersonName => PersonNameScore >= 0.5 && Uniqueness >= 0.95;

    public override string ToString() =>
        $"{Name} (uniq {Uniqueness:P0}, id {IdentifierScore:P0}, name {PersonNameScore:P0})";
}

/// <summary>
/// The result of analysing an imported dataset.
/// </summary>
public class RosterAnalysis
{
    public IReadOnlyList<ColumnProfile> Columns { get; init; } = Array.Empty<ColumnProfile>();

    public int RowCount { get; init; }

    /// <summary>Best candidate for the attendee's join identifier.</summary>
    public ColumnProfile? IdentifierColumn { get; init; }

    /// <summary>Best candidate for the attendee's display name.</summary>
    public ColumnProfile? NameColumn { get; init; }

    public ColumnProfile? EmailColumn { get; init; }

    public ColumnProfile? PhoneColumn { get; init; }

    public ColumnProfile? GroupingColumn { get; init; }

    /// <summary>
    /// Ranked alternatives for the identifier role, best first.
    /// Surfaced so a lecturer can override the automatic pick — institutional
    /// exports are inconsistent enough that the top guess is sometimes wrong.
    /// </summary>
    public IReadOnlyList<ColumnProfile> RankedIdentifiers { get; init; } = Array.Empty<ColumnProfile>();

    /// <summary>Ranked alternatives for the display-name role, best first.</summary>
    public IReadOnlyList<ColumnProfile> RankedNames { get; init; } = Array.Empty<ColumnProfile>();

    /// <summary>
    /// Roles where the automatic choice is weak, so a lecturer should confirm.
    /// Empty when detection is unambiguous.
    /// </summary>
    public IReadOnlyList<string> NeedsConfirmation { get; init; } = Array.Empty<string>();

    /// <summary>Human-readable reasons the chosen columns were selected.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>True when both an identifier and a name could be resolved.</summary>
    public bool IsUsable => IdentifierColumn is not null && NameColumn is not null;
}

/// <summary>
/// Inspects an arbitrary tabular roster and works out which column is the
/// identifier and which is the person's name.
///
/// Deliberately deterministic heuristics, not a model: the same input always
/// produces the same answer, results are explainable to a lecturer, and it runs
/// with no network call and no cost.
///
/// Scanning rules, in priority order:
///   1. Header keyword match (matric, reg, index, staff id, code …) — strongest signal
///   2. Value shape (compact alphanumeric, no spaces, consistent length)
///   3. Uniqueness — an identifier is useless if it repeats
/// </summary>
public static partial class RosterSchemaAnalyzer
{
    private static readonly string[] IdentifierHeaderHints =
    {
        "matric", "mat no", "matnumber", "reg no", "regnumber", "registration",
        "registration no", "index", "student id", "studentid", "staff id", "staffid",
        "id no", "idnumber", "code", "adm no", "admission", "account", "tag",
        "enrol", "enroll", "roll no", "serial", "number", "no"
    };

    private static readonly string[] NameHeaderHints =
    {
        "fullname", "full name", "name", "surname", "lastname", "last name",
        "firstname", "first name", "surname,", "student name", "participant"
    };

    private static readonly string[] EmailHeaderHints =
    {
        "email", "e-mail", "mail", "emailaddress", "email address"
    };

    private static readonly string[] PhoneHeaderHints =
    {
        // NOTE: "number" is deliberately absent. It appears in "Matric Number",
        // "Roll Number" and "Registration Number", so including it made every
        // identifier column score as a phone column.
        "phone", "mobile", "telephone", "contact", "whatsapp", "msisdn",
        "phone number", "mobile number", "contact number", "tel"
    };

    private static readonly string[] GroupingHeaderHints =
    {
        "department", "dept", "faculty", "level", "programme", "program",
        "Class", "unit", "departmentname", "class", "cohort", "group"
    };

    [GeneratedRegex(@"^[\p{L}]+(\s+[\p{L}'\-]+){1,3}$", RegexOptions.None, 200)]
    private static partial Regex PersonNameShape();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9/\-_]{2,19}$", RegexOptions.None, 200)]
    private static partial Regex IdentifierShape();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$", RegexOptions.None, 200)]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^[+()\-.\s\d]{7,20}$", RegexOptions.None, 200)]
    private static partial Regex PhoneShape();

    /// <summary>
    /// Analyses rows and returns a ranked column profile with the best
    /// identifier/name/email/phone/column selections.
    /// </summary>
    /// <param name="headers">Column headers in source order.</param>
    /// <param name="rows">Data rows, each matching the header count.</param>
    public static RosterAnalysis Analyze(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var notes = new List<string>();

        if (headers.Count == 0)
        {
            return new RosterAnalysis { RowCount = rows.Count, Notes = ["No headers found."] };
        }

        var columns = new List<ColumnProfile>(headers.Count);

        for (var i = 0; i < headers.Count; i++)
        {
            var values = rows
                .Where(r => i < r.Count)
                .Select(r => (r[i] ?? string.Empty).Trim())
                .Where(v => v.Length > 0)
                .ToList();

            var header = (headers[i] ?? string.Empty).Trim();
            var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var uniqueness = values.Count == 0 ? 0d : (double)distinct / values.Count;

            var identifierShapeScore = values.Count == 0
                ? 0d
                : (double)values.Count(v => IdentifierShape().IsMatch(v)) / values.Count;

            columns.Add(new ColumnProfile
            {
                Name = header,
                Index = i,
                DistinctCount = distinct,
                PopulatedCount = values.Count,
                Uniqueness = uniqueness,
                PersonNameScore = ScorePersonName(header, values),
                IdentifierScore = ScoreIdentifier(header, values),
                IdentifierShapeScore = identifierShapeScore,
                LooksLikeSerialSequence = LooksLikeSerialSequence(values),
                EmailScore = ScoreByShape(header, values, EmailShape(), EmailHeaderHints),
                PhoneScore = ScoreByShape(header, values, PhoneShape(), PhoneHeaderHints),
                GroupingScore = ScoreByHeader(header, GroupingHeaderHints),
                AverageLength = values.Count == 0 ? 0 : values.Average(v => (double)v.Length),
                Samples = values.Take(3).ToList()
            });
        }

        // An identifier must be unique across the cohort. Without this rule a
        // duplicated "Registration No" column would be chosen and students would
        // collide on the same identifier.
        var rankedIdentifiers = Rank(columns.Where(c => c.IsLikelyIdentifier), c => c.IdentifierScore)
            .Take(5)
            .ToList();

        var rankedNames = Rank(
                columns.Where(c => c.Uniqueness >= 0.95),
                c => c.PersonNameScore)
            .Take(5)
            .ToList();

        var identifier = rankedIdentifiers.FirstOrDefault();

        if (identifier is null)
        {
            notes.Add(
                "No column was unique enough to use as a join identifier. " +
                "Add a matric or registration number column, or remove duplicate rows.");
        }
        else
        {
            notes.Add($"Identifier: '{identifier.Name}' (header match + shape + {identifier.Uniqueness:P0} unique).");
        }

        // Prefer a name column that is not the identifier column.
        var name = rankedNames
            .Where(c => c.Index != identifier?.Index && c.PersonNameScore >= 0.5)
            .FirstOrDefault();

        if (name is null)
        {
            notes.Add("No column looked like a person's name. A display name is required.");
        }
        else
        {
            notes.Add($"Display name: '{name.Name}'.");
        }

        var email = columns
            .Where(c => c.Index != identifier?.Index)
            .Where(c => c.EmailScore >= 0.5)
            .OrderByDescending(c => c.EmailScore)
            .FirstOrDefault();

        var phone = columns
            .Where(c => c.Index != identifier?.Index && c.Index != email?.Index)
            .Where(c => c.PhoneScore >= 0.5)
            .OrderByDescending(c => c.PhoneScore)
            .FirstOrDefault();

        if (email is not null) notes.Add($"Email: '{email.Name}'.");
        if (phone is not null) notes.Add($"Phone: '{phone.Name}'.");

        var grouping = columns
            .Where(c => c.Index != identifier?.Index && c.Index != name?.Index)
            .Where(c => c.GroupingScore >= 0.5)
            .OrderByDescending(c => c.GroupingScore)
            .FirstOrDefault();

        if (grouping is not null) notes.Add($"Department/level: '{grouping.Name}'.");

        if (rows.Count == 0)
        {
            notes.Add("No data rows were found below the header.");
        }

        var needsConfirmation = BuildConfirmationPrompts(identifier, name, rankedIdentifiers, rankedNames);

        return new RosterAnalysis
        {
            Columns = columns,
            RowCount = rows.Count,
            IdentifierColumn = identifier,
            NameColumn = name,
            EmailColumn = email,
            PhoneColumn = phone,
            GroupingColumn = grouping,
            RankedIdentifiers = rankedIdentifiers,
            RankedNames = rankedNames,
            NeedsConfirmation = needsConfirmation,
            Notes = notes
        };
    }

    /// <summary>
    /// Flags the cases where the automatic pick is likely to be wrong, so the
    /// upload screen can ask a lecturer to confirm instead of silently guessing.
    ///
    /// These are the observed failure modes, not hypotheticals:
    ///  - A phone-number column scores 0.70 as an identifier because digits are
    ///    alphanumeric and phone numbers have consistent length. It only loses to
    ///    a real ID column by a small margin.
    ///  - "Surname, Firstname" stores a whole person in one cell and can outrank
    ///    a separate surname column.
    ///  - A close runner-up means the ordering is fragile across exports.
    /// </summary>
    private static List<string> BuildConfirmationPrompts(
        ColumnProfile? identifier,
        ColumnProfile? name,
        List<ColumnProfile> rankedIdentifiers,
        List<ColumnProfile> rankedNames)
    {
        var prompts = new List<string>();

        if (identifier is not null)
        {
            if (identifier.PhoneScore >= 0.5)
            {
                prompts.Add(
                    $"'{identifier.Name}' looks like a phone number rather than a student ID. " +
                    "A phone number works as a join key but changes if a student changes number.");
            }

            if (rankedIdentifiers.Count > 1 &&
                identifier.IdentifierScore - rankedIdentifiers[1].IdentifierScore < 0.15)
            {
                prompts.Add(
                    $"'{identifier.Name}' only narrowly beat '{rankedIdentifiers[1].Name}'. " +
                    "Confirm which column students should type to join.");
            }

            if (identifier.Uniqueness < 1.0)
            {
                prompts.Add(
                    $"'{identifier.Name}' contains duplicate values, so two attendees may share it.");
            }
        }

        if (name is not null)
        {
            if (name.Name.Contains(',', StringComparison.Ordinal))
            {
                prompts.Add(
                    $"'{name.Name}' appears to store a surname and forename in one column.");
            }

            if (rankedNames.Count > 1 &&
                name.PersonNameScore - rankedNames[1].PersonNameScore < 0.15)
            {
                prompts.Add(
                    $"'{name.Name}' only narrowly beat '{rankedNames[1].Name}' for the display name.");
            }
        }

        return prompts;
    }

    /// <summary>
    /// Orders columns for a role: highest score, then most unique, then shortest
    /// values (shorter identifiers are less error-prone to type on a phone).
    /// </summary>
    private static IEnumerable<ColumnProfile> Rank(
        IEnumerable<ColumnProfile> candidates,
        Func<ColumnProfile, double> score) =>
        candidates
            .OrderByDescending(score)
            .ThenByDescending(c => c.Uniqueness)
            .ThenBy(c => c.AverageLength);

    private static double ScoreByHeader(string header, string[] hints)
    {
        var normalised = Normalise(header);
        if (normalised.Length == 0) return 0;

        foreach (var hint in hints)
        {
            if (normalised == hint) return 1.0;
        }

        foreach (var hint in hints.OrderByDescending(h => h.Length))
        {
            if (normalised.Contains(hint, StringComparison.Ordinal)) return 0.85;
        }

        return 0;
    }

    private static double ScoreByShape(string header, List<string> values, Regex shape, string[] hints)
    {
        var headerScore = ScoreByHeader(header, hints);
        if (values.Count == 0) return headerScore;

        var matches = values.Count(v => shape.IsMatch(v));
        var shapeScore = (double)matches / values.Count;

        // Header and shape must agree; a column called "name" full of numbers is not a name.
        if (headerScore == 0) return shapeScore >= 0.9 ? 0.4 : 0;
        return (headerScore * 0.6) + (shapeScore * 0.4);
    }

    private static double ScoreIdentifier(string header, List<string> values)
    {
        var headerScore = ScoreByHeader(header, IdentifierHeaderHints);
        if (values.Count == 0) return headerScore;

        var shapeMatches = values.Count(v => IdentifierShape().IsMatch(v));
        var shapeScore = (double)shapeMatches / values.Count;

        // Identifiers have consistent length; names do not.
        var lengths = values.Select(v => v.Length).ToList();
        var consistentLength = lengths.Count == 0 ? 0 : 1.0 - ((lengths.Max() - lengths.Min()) / 20.0);

        var combined = (shapeScore * 0.45) + (Math.Max(0, consistentLength) * 0.25) + (headerScore * 0.30);

        // Reject obvious person names even if the header said "number".
        var nameShape = values.Count(v => PersonNameShape().IsMatch(v)) / (double)values.Count;
        if (nameShape > 0.5)
        {
            combined *= 0.35;
        }

        // A header hint plus uniform length must not be enough on their own. A pure
        // serial column ("S/N", "Index") matches no identifier shape, and allowing it
        // to pass would make students type row numbers to join.
        if (shapeScore < 0.5)
        {
            combined *= 0.4;
        }

        return Math.Clamp(combined, 0, 1);
    }

    private static double ScorePersonName(string header, List<string> values)
    {
        var headerScore = ScoreByHeader(header, NameHeaderHints);
        if (values.Count == 0) return headerScore;

        var shapeMatches = values.Count(v => PersonNameShape().IsMatch(v));
        var shapeScore = (double)shapeMatches / values.Count;

        if (headerScore == 0) return shapeScore >= 0.8 ? 0.45 : 0;
        return (headerScore * 0.6) + (shapeScore * 0.4);
    }

    /**
     * Detects a row-number column: every value parses as an integer, and the set
     * is exactly consecutive (count == max - min + 1).
     *
     * A shape gate alone is not sufficient. Below row 100 a serial column fails
     * identifier shape, but from row 100 onwards three-digit values match and the
     * column scores well enough to be offered to a lecturer as a join key.
     */
    private static bool LooksLikeSerialSequence(List<string> values)
    {
        if (values.Count < 3)
        {
            return false;
        }

        var numbers = new List<long>(values.Count);
        foreach (var value in values)
        {
            if (!long.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            numbers.Add(parsed);
        }

        var distinct = numbers.Distinct().Count();
        if (distinct != numbers.Count)
        {
            return false;
        }

        var min = numbers.Min();
        var max = numbers.Max();

        // Exactly consecutive and no gaps.
        return (max - min + 1) == numbers.Count;
    }

    private static string Normalise(string value) =>
        new string(value.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).ToLowerInvariant().Trim();
}