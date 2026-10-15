using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;

namespace VIIDII.Services;

/// <summary>
/// A lecturer's named lists of students.
///
/// Intentionally minimal: create a class, give it a code, upload the list once.
/// No course catalogue, no semesters, no institution. Those can be attached later
/// without moving data.
/// </summary>
public class ClassService
{
    private readonly ViidiiDbContext _context;

    public ClassService(ViidiiDbContext context) => _context = context;

    /// <summary>
    /// Creates a space / class. The code is upper-cased and stripped to letters and digits,
    /// so "cs 100-a" and "CS100A" are the same class.
    /// </summary>
    public async Task<(Class? Created, string? Error)> CreateAsync(
        int lecturerId,
        string code,
        string name,
        string? presetType = null,
        string? joinPrompt = null,
        bool enableWatermark = true,
        string? paymentLink = null,
        bool allowSelfRegistration = true)
    {
        code = NormaliseCode(code);
        name = name?.Trim() ?? string.Empty;

        if (code.Length == 0)
        {
            return (null, "Enter a code for the space, for example CS100 or ICAN-TAX.");
        }

        if (code.Length > 32)
        {
            return (null, "Codes are limited to 32 characters.");
        }

        if (name.Length == 0)
        {
            return (null, "Give the space a name, for example 100 Level Computer Science or ICAN Skills Taxation.");
        }

        if (name.Length > 200)
        {
            return (null, "Names are limited to 200 characters.");
        }

        if (await _context.Classes.AnyAsync(c => c.Code == code))
        {
            return (null, $"The code '{code}' is already in use. Try another.");
        }

        var presetMeta = SpacePreset.GetMeta(presetType);
        var finalPrompt = string.IsNullOrWhiteSpace(joinPrompt) ? presetMeta.DefaultPrompt : joinPrompt.Trim();

        var created = new Class
        {
            Code = code,
            Name = name,
            LecturerId = lecturerId,
            CreatedAt = DateTime.UtcNow,
            PresetType = presetMeta.Key,
            JoinPrompt = finalPrompt,
            EnableWatermark = enableWatermark,
            PaymentLink = string.IsNullOrWhiteSpace(paymentLink) ? null : paymentLink.Trim(),
            AllowSelfRegistration = allowSelfRegistration
        };

        _context.Classes.Add(created);
        await _context.SaveChangesAsync();

        return (created, null);
    }

    /// <summary>
    /// Updates configuration, presets, payment links, and security settings for an existing space.
    /// </summary>
    public async Task<bool> UpdatePresetAsync(
        int classId,
        string? presetType,
        string? joinPrompt,
        bool? enableWatermark,
        string? paymentLink = null,
        bool? allowSelfRegistration = null)
    {
        var target = await _context.Classes.FindAsync(classId);
        if (target is null) return false;

        if (!string.IsNullOrWhiteSpace(presetType))
        {
            target.PresetType = SpacePreset.GetMeta(presetType).Key;
        }

        if (joinPrompt is not null)
        {
            target.JoinPrompt = joinPrompt.Trim();
        }

        if (enableWatermark.HasValue)
        {
            target.EnableWatermark = enableWatermark.Value;
        }

        if (paymentLink is not null)
        {
            target.PaymentLink = string.IsNullOrWhiteSpace(paymentLink) ? null : paymentLink.Trim();
        }

        if (allowSelfRegistration.HasValue)
        {
            target.AllowSelfRegistration = allowSelfRegistration.Value;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Self-registers an attendee into a space (RSVP).
    /// </summary>
    public async Task<(RosterEntry? Entry, string? Error)> RegisterAttendeeAsync(
        int classId,
        string identifier,
        string name,
        string? email = null,
        string? phone = null)
    {
        var target = await _context.Classes.FindAsync(classId);
        if (target is null) return (null, "Space not found.");
        if (!target.AllowSelfRegistration) return (null, "Registration is closed for this event.");

        var cleanId = identifier?.Trim() ?? string.Empty;
        var cleanName = name?.Trim() ?? string.Empty;

        if (cleanId.Length == 0) return (null, "Please enter your phone number or identifier.");
        if (cleanName.Length == 0) cleanName = cleanId;

        var existing = await _context.RosterEntries
            .FirstOrDefaultAsync(r => r.ClassId == classId && r.Identifier == cleanId);

        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(name)) existing.DisplayName = cleanName;
            if (!string.IsNullOrWhiteSpace(email)) existing.Email = email.Trim();
            if (!string.IsNullOrWhiteSpace(phone)) existing.Phone = phone.Trim();
            await _context.SaveChangesAsync();
            return (existing, null);
        }

        if (string.IsNullOrWhiteSpace(target.RosterMappingJson))
        {
            var defaultMapping = new RosterColumnMapping
            {
                IdentifierHeader = "Phone / Identifier",
                NameHeader = "Name",
                IdentifierPrompt = target.JoinPrompt ?? "Phone Number or Identifier"
            };
            target.RosterMappingJson = System.Text.Json.JsonSerializer.Serialize(defaultMapping);
            if (string.IsNullOrWhiteSpace(target.JoinPrompt))
            {
                target.JoinPrompt = "Phone Number or Identifier";
            }
        }

        var entry = new RosterEntry
        {
            ClassId = classId,
            Identifier = cleanId,
            DisplayName = cleanName,
            Email = email?.Trim(),
            Phone = phone?.Trim(),
            Grouping = "RSVP Registered",
            Source = "rsvp",
            Role = RosterRole.Student,
            ImportedAtUtc = DateTime.UtcNow
        };

        _context.RosterEntries.Add(entry);
        await _context.SaveChangesAsync();
        return (entry, null);
    }

    /// <summary>
    /// Generates a code that is not yet taken, e.g. "CS100A".
    /// Offered to the lecturer as a suggestion, never applied silently.
    /// </summary>
    public async Task<string> SuggestCodeAsync(string baseCode)
    {
        var normalised = NormaliseCode(baseCode);

        if (normalised.Length == 0)
        {
            normalised = "CLASS";
        }

        if (normalised.Length > 28)
        {
            normalised = normalised[..28];
        }

        if (!await _context.Classes.AnyAsync(c => c.Code == normalised))
        {
            return normalised;
        }

        for (var suffix = 'A'; suffix <= 'Z'; suffix++)
        {
            var candidate = $"{normalised}{suffix}";
            if (!await _context.Classes.AnyAsync(c => c.Code == candidate))
            {
                return candidate;
            }
        }

        return $"{normalised}{DateTime.UtcNow:HHmmss}";
    }

    public async Task<List<Class>> GetForLecturerAsync(int lecturerId) =>
        await _context.Classes
            .Where(c => c.LecturerId == lecturerId)
            .OrderBy(c => c.Code)
            .ToListAsync();

    public async Task<Class?> GetByIdAsync(int classId) =>
        await _context.Classes.FindAsync(classId);

    /// <summary>Looks up a class by its public join code, for the student join screen.</summary>
    public async Task<Class?> GetByCodeAsync(string code)
    {
        var normalised = NormaliseCode(code);

        return normalised.Length == 0
            ? null
            : await _context.Classes.FirstOrDefaultAsync(c => c.Code == normalised);
    }

    public async Task<int> GetStudentCountAsync(int classId) =>
        await _context.RosterEntries.CountAsync(r => r.ClassId == classId);

    public async Task<int> GetSessionCountAsync(int classId) =>
        await _context.Sessions.CountAsync(s => s.ClassId == classId);

    /// <summary>
    /// Records what students will be asked for on the join screen, so the student
    /// page prompts for the right thing instead of guessing.
    /// </summary>
    public async Task SetJoinPromptAsync(int classId, string prompt, RosterColumnMapping mapping)
    {
        var target = await _context.Classes.FindAsync(classId);
        if (target is null) return;

        mapping.IdentifierPrompt = prompt;
        target.RosterMappingJson = System.Text.Json.JsonSerializer.Serialize(mapping);
        target.JoinPrompt = prompt;

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Upper-cases and strips anything that is not a letter or digit, so
    /// "cs 100-a" and "CS100A" resolve to the same class.
    /// </summary>
    internal static string NormaliseCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}