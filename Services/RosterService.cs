using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services.Roster;

namespace VIIDII.Services;

public class RosterImportResult
{
    public int Imported { get; init; }
    public int Skipped { get; init; }
    public int LinkedToExistingUser { get; init; }
    public int UserAccountsCreated { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Human-readable label for the identifier column actually used.</summary>
    public string? IdentifierHeader { get; init; }

    /// <summary>Human-readable label for the display-name column actually used.</summary>
    public string? NameHeader { get; init; }
}

/// <summary>
/// Imports a student list for a class.
///
/// Replaces in-memory participant lists with a durable, queryable roster so that
/// attendance evidence can be re-derived from the database at any time.
/// </summary>
public partial class RosterService
{
    private readonly ViidiiDbContext _context;
    private readonly PasswordHasher<User> _passwordHasher;

    public RosterService(ViidiiDbContext context, PasswordHasher<User> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    /// <summary>
    /// Analyses a payload without writing anything. Used by the upload UI to show
    /// a lecturer which columns were detected before they commit.
    /// </summary>
    public (ParsedRoster Parsed, RosterAnalysis Analysis) Preview(string content)
    {
        var parsed = RosterParser.Parse(content);
        var analysis = RosterSchemaAnalyzer.Analyze(parsed.Headers, parsed.Rows);
        return (parsed, analysis);
    }

    /// <summary>
    /// Persists student entries for a class, creating User accounts where an
    /// attendee does not already exist.
    /// </summary>
    /// <param name="classId">Owning class primary key.</param>
    /// <param name="content">Raw CSV/TSV/JSON payload.</param>
    /// <param name="passcodeLength">Length of the generated join passcode.</param>
    /// <param name="generatePasscodes">
    /// When true, generates a passcode per attendee so students can join without
    /// pre-existing credentials. Only returned to the caller — never stored in clear text.
    /// </param>
    public async Task<RosterImportResult> ImportAsync(
        int classId,
        string content,
        RosterColumnMapping? mapping = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var errors = new List<string>();
        var warnings = new List<string>();

        var classInfo = await _context.Classes.FindAsync(classId);
        if (classInfo is null)
        {
            return new RosterImportResult
            {
                Errors = [$"Class {classId} was not found."]
            };
        }

        var (parsed, analysis) = Preview(content);
        warnings.AddRange(parsed.Warnings);

        // A lecturer's explicit choice always wins over the automatic pick.
        // This is the escape hatch for exports where the header names mislead.
        var (identifierIndex, nameIndex, emailIndex, phoneIndex, groupingIndex) =
            ResolveColumnIndices(parsed.Headers, analysis, mapping, errors);

        if (errors.Count > 0)
        {
            return new RosterImportResult { Errors = errors, Warnings = warnings };
        }

        var imported = 0;
        var skipped = 0;
        var linked = 0;

        // Identifiers must be unique within a class or two students collide on
        // the same join code and attendance becomes unprovable.
        var seenIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newEntries = new List<RosterEntry>();
        var newUsers = new List<User>();

        // Preload every existing user we might link to.
        //
        // A per-row lookup cost ~157ms per student (157s for 1000 students).
        // Replacing it with a single unbounded `Contains` was worse still
        // (522s): EF inlines the list as SQL parameters, and 1000 literals blows
        // past the parameter limits (SQL Server caps at 2100, older SQLite at
        // 999). Batching keeps the query count small while staying inside them.
        const int parameterBatchSize = 400;

        var candidateIdentifiers = parsed.Rows
            .Select(r => NormaliseIdentifier(CellAt(r, identifierIndex)))
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingByMatricNo = new Dictionary<string, User>(StringComparer.Ordinal);

        for (var offset = 0; offset < candidateIdentifiers.Count; offset += parameterBatchSize)
        {
            var batch = candidateIdentifiers.Skip(offset).Take(parameterBatchSize).ToList();

            var existingUsers = await _context.Users
                .Where(u => batch.Contains(u.MatricNo))
                .ToListAsync();

            foreach (var existing in existingUsers)
            {
                existingByMatricNo[existing.MatricNo] = existing;
            }
        }

        foreach (var row in parsed.Rows)
        {
            var identifier = CellAt(row, identifierIndex).Trim();
            var name = CellAt(row, nameIndex).Trim();

            if (name.Length == 0 && identifier.Length > 0)
            {
                name = identifier;
            }

            if (identifier.Length == 0)
            {
                skipped++;
                warnings.Add("Skipped a row missing an identifier.");
                continue;
            }

            identifier = NormaliseIdentifier(identifier);

            if (identifier.Length > 64)
            {
                skipped++;
                errors.Add($"Identifier exceeds 64 characters and was rejected: '{identifier[..40]}…'");
                continue;
            }

            if (name.Length > 200)
            {
                name = name[..200];
            }

            if (!seenIdentifiers.Add(identifier))
            {
                skipped++;
                errors.Add($"Duplicate identifier in the file: '{identifier}'. Both attendees would share a join code.");
                continue;
            }

            var email = emailIndex >= 0 ? Truncate(CellAt(row, emailIndex).Trim(), 200) : null;
            var phone = phoneIndex >= 0 ? Truncate(CellAt(row, phoneIndex).Trim(), 40) : null;
            var grouping = groupingIndex >= 0 ? Truncate(CellAt(row, groupingIndex).Trim(), 120) : null;

            if (phone is null && (analysis.IdentifierColumn?.PhoneScore >= 0.45 || PhoneCandidateShape().IsMatch(identifier)))
            {
                phone = Truncate(identifier, 40);
            }
            if (email is null && (analysis.IdentifierColumn?.EmailScore >= 0.45 || identifier.Contains('@')))
            {
                email = Truncate(identifier, 200);
            }

            var existingUser = existingByMatricNo.GetValueOrDefault(identifier);

            if (existingUser is null)
            {
                var user = new User
                {
                    MatricNo = identifier,
                    Name = name,

                    // No password hash. These accounts join with a passcode, and
                    // PBKDF2 costs ~164ms per hash — 164 seconds for a 1000-student
                    // import, to protect a credential that is never used.
                    PasswordHash = null,
                    Role = Role.Student,
                    CreatedAt = DateTime.UtcNow
                };

                newUsers.Add(user);
                existingByMatricNo[identifier] = user;
            }
            else
            {
                linked++;
            }

            newEntries.Add(BuildEntry(classId, identifier, name, email, phone, grouping, parsed.Format.ToString().ToLowerInvariant()));

            imported++;
        }

        if (newUsers.Count > 0)
        {
            _context.Users.AddRange(newUsers);
        }

        if (newEntries.Count > 0)
        {
            _context.RosterEntries.AddRange(newEntries);
        }

        await _context.SaveChangesAsync();

        return new RosterImportResult
        {
            Imported = imported,
            Skipped = skipped,
            LinkedToExistingUser = linked,
            UserAccountsCreated = newUsers.Count,
            Warnings = warnings.Distinct().ToList(),
            Errors = errors,
            IdentifierHeader = HeaderAt(parsed.Headers, identifierIndex),
            NameHeader = HeaderAt(parsed.Headers, nameIndex)
        };
    }

    /// <summary>
    private static string? HeaderAt(IReadOnlyList<string> headers, int index) =>
        index >= 0 && index < headers.Count ? headers[index] : null;

    /// <summary>Returns the student list for a class, ordered for display.</summary>
    public async Task<List<RosterEntry>> GetForClassAsync(int classId) =>
        await _context.RosterEntries
            .Where(r => r.ClassId == classId)
            .OrderBy(r => r.DisplayName)
            .ToListAsync();

    /// <summary>
    /// Marks a seat as claimed. Returns false when the seat is already held from a
    /// different device, so one student cannot occupy two seats and inflate their
    /// own attendance percentage.
    ///
    /// Note this is NOT identity proof. A student who knows another student's
    /// matric number and name can still join as them — see <see cref="ResolveAttendee"/>.
    /// </summary>
    public async Task<bool> ClaimSeatAsync(int rosterEntryId, string? claimToken)
    {
        var entry = await _context.RosterEntries.FindAsync(rosterEntryId);
        if (entry is null) return false;

        if (entry.HasClaimed)
        {
            return string.Equals(entry.ClaimToken, claimToken, StringComparison.Ordinal);
        }

        entry.HasClaimed = true;
        entry.ClaimedAtUtc = DateTime.UtcNow;
        entry.ClaimToken = claimToken;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Finds a roster entry from the two fields a student types: their identifier
    /// and their name. No password.
    ///
    /// Identity is therefore self-asserted. Anyone who knows a classmate's matric
    /// number and name can join as them, which means the resulting attendance record
    /// proves "someone with these details attended", not "this specific person
    /// attended". That is acceptable for participation tracking and not acceptable
    /// for accreditation-grade certification.
    /// </summary>
    /// <summary>
    /// Frees a seat a student is being held out of.
    /// <para>
    /// A claim is deliberately one-device-at-a-time, but that leaves a real trap: a
    /// student who loses their phone, changes number, or whose first claim response was
    /// lost mid-request can never get back in, and the student is told to ask the lecturer
    /// to reset the list when no such control existed. This is that control.
    /// </para>
    /// </summary>
    public async Task<bool> ReleaseSeatAsync(int rosterEntryId)
    {
        var entry = await _context.RosterEntries.FindAsync(rosterEntryId);

        if (entry is null || !entry.HasClaimed)
        {
            return false;
        }

        entry.HasClaimed = false;
        entry.ClaimedAtUtc = null;
        entry.ClaimToken = null;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<RosterEntry?> ResolveAttendeeAsync(int classId, string identifier, string? name)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var candidates = await _context.RosterEntries
            .Where(r => r.ClassId == classId)
            .ToListAsync();

        var entry = candidates.FirstOrDefault(r => IdentifiersMatch(r.Identifier, identifier));
        if (entry is null)
        {
            return null;
        }

        var hasSeparateDisplayName = !string.Equals(
            entry.DisplayName.Trim(),
            entry.Identifier.Trim(),
            StringComparison.OrdinalIgnoreCase);

        if (hasSeparateDisplayName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var target = NormaliseForComparison(name);
            if (NormaliseForComparison(entry.DisplayName) != target)
            {
                return null;
            }
        }
        else
        {
            // If the student provided a friendly display name on join, save it!
            if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), entry.Identifier.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var cleanName = name.Trim();
                if (cleanName.Length > 200) cleanName = cleanName[..200];
                entry.DisplayName = cleanName;

                var user = await _context.Users.FirstOrDefaultAsync(u => u.MatricNo == entry.Identifier);
                if (user is not null && (string.IsNullOrEmpty(user.Name) || user.Name == user.MatricNo))
                {
                    user.Name = cleanName;
                }

                await _context.SaveChangesAsync();
            }
        }

        return entry;
    }

    /// <summary>
    /// Collapses case and repeated whitespace so "Aisha  Bello" matches
    /// "aisha bello". Also folds the common Nigerian surname-forename comma split,
    /// so "Bello, Aisha" matches "Aisha Bello".
    /// </summary>
    internal static string NormaliseForComparison(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',', 2);
            if (parts.Length == 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0)
            {
                trimmed = $"{parts[1].Trim()} {parts[0].Trim()}";
            }
        }

        var builder = new System.Text.StringBuilder(trimmed.Length);
        var previousWasSpace = false;

        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
            }
            else
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Works out which column index serves each role.
    ///
    /// Uses the lecturer's explicit mapping when supplied; otherwise falls back to
    /// the analyser's ranked pick. Returns -1 indices when a role is optional and
    /// undetected, which callers treat as "this column is absent".
    /// </summary>
    private static (int Identifier, int Name, int Email, int Phone, int Grouping) ResolveColumnIndices(
        IReadOnlyList<string> headers,
        RosterAnalysis analysis,
        RosterColumnMapping? mapping,
        List<string> errors)
    {
        if (mapping is not null)
        {
            var validationError = mapping.Validate(headers);
            if (validationError is not null)
            {
                errors.Add(validationError);
                return (-1, -1, -1, -1, -1);
            }

            var idIndex = mapping.IndexOf(headers, mapping.IdentifierHeader);
            var nameIndex = string.IsNullOrWhiteSpace(mapping.NameHeader)
                ? idIndex
                : mapping.IndexOf(headers, mapping.NameHeader);

            if (nameIndex < 0)
            {
                nameIndex = idIndex;
            }

            return (
                idIndex,
                nameIndex,
                mapping.IndexOf(headers, mapping.EmailHeader),
                mapping.IndexOf(headers, mapping.PhoneHeader),
                mapping.IndexOf(headers, mapping.GroupingHeader));
        }

        if (!analysis.IsUsable)
        {
            errors.Add("Could not identify an identifier column.");
            errors.AddRange(analysis.Notes);
            return (-1, -1, -1, -1, -1);
        }

        var detectedIdIndex = analysis.IdentifierColumn!.Index;
        var detectedNameIndex = analysis.NameColumn?.Index ?? detectedIdIndex;

        return (
            detectedIdIndex,
            detectedNameIndex,
            analysis.EmailColumn?.Index ?? -1,
            analysis.PhoneColumn?.Index ?? -1,
            analysis.GroupingColumn?.Index ?? -1);
    }

    private static RosterEntry BuildEntry(
        int classId, string identifier, string name, string? email, string? phone, string? grouping, string source) =>
        new()
        {
            ClassId = classId,
            Identifier = identifier,
            DisplayName = name,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            Grouping = string.IsNullOrWhiteSpace(grouping) ? null : grouping,
            Role = RosterRole.Student,
            Source = source,
            ImportedAtUtc = DateTime.UtcNow
        };

    private static string CellAt(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];

    /// <summary>
    /// Normalises an identifier for consistent matching. Surrounding whitespace from
    /// spreadsheet exports is stripped. Case is preserved here because the stored
    /// value is the institution's own record, but every comparison against it is
    /// case-insensitive.
    /// </summary>
    internal static string NormaliseIdentifier(string value) => value.Trim();

    [GeneratedRegex(@"^[+()\-.\s\d]{7,25}$", RegexOptions.None, 200)]
    private static partial Regex PhoneCandidateShape();

    internal static bool IdentifiersMatch(string stored, string candidate)
    {
        var s = stored.Trim();
        var c = candidate.Trim();

        if (string.Equals(s, c, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (PhoneCandidateShape().IsMatch(s) && PhoneCandidateShape().IsMatch(c))
        {
            if (PhonesMatch(s, c))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool PhonesMatch(string p1, string p2)
    {
        var d1 = ExtractDigits(p1);
        var d2 = ExtractDigits(p2);

        if (d1.Length < 7 || d2.Length < 7)
        {
            return false;
        }

        if (d1 == d2) return true;

        if (d1.StartsWith("234") && d1.Length == 13 && d2.StartsWith("0") && d2.Length == 11)
        {
            return d1.AsSpan(3).SequenceEqual(d2.AsSpan(1));
        }

        if (d2.StartsWith("234") && d2.Length == 13 && d1.StartsWith("0") && d1.Length == 11)
        {
            return d2.AsSpan(3).SequenceEqual(d1.AsSpan(1));
        }

        if (d1.Length == 10 && d2.StartsWith("0") && d2.Length == 11)
        {
            return d1 == d2[1..];
        }

        if (d2.Length == 10 && d1.StartsWith("0") && d1.Length == 11)
        {
            return d2 == d1[1..];
        }

        if (d1.Length == 10 && d2.StartsWith("234") && d2.Length == 13)
        {
            return d1 == d2[3..];
        }

        if (d2.Length == 10 && d1.StartsWith("234") && d1.Length == 13)
        {
            return d2 == d1[3..];
        }

        return false;
    }

    private static string ExtractDigits(string value) =>
        new string(value.Where(char.IsAsciiDigit).ToArray());
}