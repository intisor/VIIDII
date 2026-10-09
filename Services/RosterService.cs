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
    public IReadOnlyList<GeneratedPasscode> Passcodes { get; init; } = Array.Empty<GeneratedPasscode>();

    /// <summary>Human-readable label for the identifier column actually used.</summary>
    public string? IdentifierHeader { get; init; }

    /// <summary>Human-readable label for the display-name column actually used.</summary>
    public string? NameHeader { get; init; }
}

/// <summary>A generated join passcode, returned once and never persisted in plain text.</summary>
public class GeneratedPasscode
{
    public required string Identifier { get; init; }
    public required string DisplayName { get; init; }
    public required string Passcode { get; init; }
}

/// <summary>
/// Imports an expected attendee list for a session.
///
/// Replaces in-memory participant lists with a durable, queryable roster so that
/// attendance evidence can be re-derived from the database at any time.
/// </summary>
public class RosterService
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
    /// Persists roster entries for a session, creating User accounts where an
    /// attendee does not already exist.
    /// </summary>
    /// <param name="sessionId">Owning session primary key.</param>
    /// <param name="content">Raw CSV/TSV/JSON payload.</param>
    /// <param name="passcodeLength">Length of the generated join passcode.</param>
    /// <param name="generatePasscodes">
    /// When true, generates a passcode per attendee so students can join without
    /// pre-existing credentials. Only returned to the caller — never stored in clear text.
    /// </param>
    public async Task<RosterImportResult> ImportAsync(
        int sessionId,
        string content,
        int passcodeLength = 8,
        bool generatePasscodes = true,
        RosterColumnMapping? mapping = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (passcodeLength < 6 || passcodeLength > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(passcodeLength), passcodeLength, "Passcode length must be between 6 and 32.");
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        var passcodes = new List<GeneratedPasscode>();

        var session = await _context.Sessions.FindAsync(sessionId);
        if (session is null)
        {
            return new RosterImportResult
            {
                Errors = [$"Session {sessionId} was not found."]
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
        var created = 0;

        // Identifiers must be unique within a session or two students collide on
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

            if (identifier.Length == 0 || name.Length == 0)
            {
                skipped++;
                warnings.Add($"Skipped a row missing an identifier or name: '{identifier}' / '{name}'.");
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

            newEntries.Add(BuildEntry(sessionId, identifier, name, email, phone, grouping, parsed.Format.ToString().ToLowerInvariant()));

            if (generatePasscodes)
            {
                passcodes.Add(new GeneratedPasscode
                {
                    Identifier = identifier,
                    DisplayName = name,
                    Passcode = PasscodeGenerator.Generate(passcodeLength)
                });
            }

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
            Passcodes = passcodes,
            IdentifierHeader = HeaderAt(parsed.Headers, identifierIndex),
            NameHeader = HeaderAt(parsed.Headers, nameIndex)
        };
    }

    /// <summary>
    /// Persists the confirmed column mapping on the session so the student join
    /// screen can say "enter your matric number" instead of a generic prompt.
    /// </summary>
    public async Task SetColumnMappingAsync(int sessionId, RosterColumnMapping mapping, string identifierPrompt)
    {
        var session = await _context.Sessions.FindAsync(sessionId)
                      ?? throw new InvalidOperationException($"Session {sessionId} was not found.");

        mapping.IdentifierPrompt = identifierPrompt;
        session.RosterMappingJson = System.Text.Json.JsonSerializer.Serialize(mapping);
        session.RosterIdentifierPrompt = identifierPrompt;

        await _context.SaveChangesAsync();
    }

    private static string? HeaderAt(IReadOnlyList<string> headers, int index) =>
        index >= 0 && index < headers.Count ? headers[index] : null;

    /// <summary>Returns the roster for a session, ordered for display.</summary>
    public async Task<List<RosterEntry>> GetForSessionAsync(int sessionId) =>
        await _context.RosterEntries
            .Where(r => r.SessionId == sessionId)
            .OrderBy(r => r.DisplayName)
            .ToListAsync();

    /// <summary>
    /// Marks a seat as claimed. Returns false when already claimed by someone
    /// else, which prevents a shared passcode being used on a second device.
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

            return (
                mapping.IndexOf(headers, mapping.IdentifierHeader),
                mapping.IndexOf(headers, mapping.NameHeader),
                mapping.IndexOf(headers, mapping.EmailHeader),
                mapping.IndexOf(headers, mapping.PhoneHeader),
                mapping.IndexOf(headers, mapping.GroupingHeader));
        }

        if (!analysis.IsUsable)
        {
            errors.Add("Could not identify both an identifier column and a name column.");
            errors.AddRange(analysis.Notes);
            return (-1, -1, -1, -1, -1);
        }

        return (
            analysis.IdentifierColumn!.Index,
            analysis.NameColumn!.Index,
            analysis.EmailColumn?.Index ?? -1,
            analysis.PhoneColumn?.Index ?? -1,
            analysis.GroupingColumn?.Index ?? -1);
    }

    private static RosterEntry BuildEntry(
        int sessionId, string identifier, string name, string? email, string? phone, string? grouping, string source) =>
        new()
        {
            SessionId = sessionId,
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
    /// Normalises an identifier for consistent matching. Case is preserved because
    /// institutions treat "ABC123" and "abc123" as different students, but
    /// surrounding whitespace from spreadsheet exports is stripped.
    /// </summary>
    internal static string NormaliseIdentifier(string value) => value.Trim();
}

/// <summary>
/// Generates human-typeable passcodes.
///
/// Excludes visually ambiguous characters (0/O, 1/I/L) because passcodes get read
/// aloud and typed on phones. Uses a cryptographic RNG, not Random.
/// </summary>
public static class PasscodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 8)
    {
        if (length < 6 || length > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, "Passcode length must be between 6 and 32.");
        }

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}