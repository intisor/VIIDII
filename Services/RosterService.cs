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
        bool generatePasscodes = true)
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

        if (!analysis.IsUsable)
        {
            errors.Add("Could not identify both an identifier column and a name column.");
            errors.AddRange(analysis.Notes);
            return new RosterImportResult { Errors = errors, Warnings = warnings };
        }

        var identifierIndex = analysis.IdentifierColumn!.Index;
        var nameIndex = analysis.NameColumn!.Index;
        var emailIndex = analysis.EmailColumn?.Index ?? -1;
        var phoneIndex = analysis.PhoneColumn?.Index ?? -1;
        var groupingIndex = analysis.GroupingColumn?.Index ?? -1;

        var imported = 0;
        var skipped = 0;
        var linked = 0;
        var created = 0;

        // Identifiers must be unique within a session or two students collide on
        // the same join code and attendance becomes unprovable.
        var seenIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newEntries = new List<RosterEntry>();
        var newUsers = new List<User>();
        var touchedUsers = new List<User>();

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

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.MatricNo == identifier);

            if (existingUser is null)
            {
                var user = new User
                {
                    MatricNo = identifier,
                    Name = name,
                    // Provisional password hash. The join passcode is the real
                    // credential path for roster-gated attendance; this only exists
                    // because User.PasswordHash is required.
                    PasswordHash = _passwordHasher.HashPassword(null!, Guid.NewGuid().ToString("N")),
                    Role = Role.Student,
                    CreatedAt = DateTime.UtcNow
                };

                newUsers.Add(user);
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
            }
            else
            {
                touchedUsers.Add(existingUser);
                newEntries.Add(BuildEntry(sessionId, identifier, name, email, phone, grouping, parsed.Format.ToString().ToLowerInvariant()));
                linked++;

                if (generatePasscodes)
                {
                    passcodes.Add(new GeneratedPasscode
                    {
                        Identifier = identifier,
                        DisplayName = name,
                        Passcode = PasscodeGenerator.Generate(passcodeLength)
                    });
                }
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
            Passcodes = passcodes
        };
    }

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