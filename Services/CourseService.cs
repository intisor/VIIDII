using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;

namespace VIIDII.Services;

/// <summary>
/// Courses owned by a lecturer.
///
/// Lecturer-first by design. Institution and department are deliberately absent:
/// a course may be attached to a centre later without moving data.
/// </summary>
public class CourseService
{
    private readonly ViidiiDbContext _context;

    public CourseService(ViidiiDbContext context) => _context = context;

    /// <summary>
    /// Creates a course. The code is upper-cased and space-stripped so join links
    /// and typed input stay consistent.
    /// </summary>
    public async Task<(Course? Course, string? Error)> CreateAsync(int lecturerId, string code, string title)
    {
        code = NormaliseCode(code);
        title = title?.Trim() ?? string.Empty;

        if (code.Length == 0)
        {
            return (null, "Enter a course code, for example CSC201.");
        }

        if (code.Length > 32)
        {
            return (null, "Course codes are limited to 32 characters.");
        }

        if (title.Length == 0)
        {
            return (null, "Enter a course title.");
        }

        if (title.Length > 200)
        {
            return (null, "Course titles are limited to 200 characters.");
        }

        if (await _context.Courses.AnyAsync(c => c.Code == code))
        {
            return (null, $"The course code '{code}' is already taken. Choose another.");
        }

        var course = new Course
        {
            Code = code,
            Title = title,
            LecturerId = lecturerId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Courses.Add(course);
        await _context.SaveChangesAsync();

        return (course, null);
    }

    /// <summary>
    /// Generates a course code that is not yet taken, e.g. "CSC201B".
    /// Offered as a convenience, never silently applied.
    /// </summary>
    public async Task<string> SuggestCodeAsync(string baseCode)
    {
        var normalised = NormaliseCode(baseCode);

        if (normalised.Length == 0)
        {
            normalised = "COURSE";
        }

        if (normalised.Length > 28)
        {
            normalised = normalised[..28];
        }

        if (!await _context.Courses.AnyAsync(c => c.Code == normalised))
        {
            return normalised;
        }

        for (var suffix = 'A'; suffix <= 'Z'; suffix++)
        {
            var candidate = $"{normalised}{suffix}";
            if (!await _context.Courses.AnyAsync(c => c.Code == candidate))
            {
                return candidate;
            }
        }

        return $"{normalised}-{DateTime.UtcNow:yyyyMMddHHmm}";
    }

    public async Task<List<Course>> GetForLecturerAsync(int lecturerId) =>
        await _context.Courses
            .Where(c => c.LecturerId == lecturerId)
            .OrderBy(c => c.Code)
            .ToListAsync();

    public async Task<Course?> GetByIdAsync(int courseId) =>
        await _context.Courses.FindAsync(courseId);

    /// <summary>Looks up a course by its public join code. Used by the student join screen.</summary>
    public async Task<Course?> GetByCodeAsync(string code)
    {
        var normalised = NormaliseCode(code);

        return normalised.Length == 0
            ? null
            : await _context.Courses.FirstOrDefaultAsync(c => c.Code == normalised);
    }

    public async Task<int> GetRosterCountAsync(int courseId) =>
        await _context.RosterEntries.CountAsync(r => r.CourseId == courseId);

    public async Task<int> GetSessionCountAsync(int courseId) =>
        await _context.Sessions.CountAsync(s => s.CourseId == courseId);

    /// <summary>
    /// Records the confirmed join prompt so the student screen can ask for the
    /// right thing instead of guessing.
    /// </summary>
    public async Task SetJoinPromptAsync(int courseId, string prompt, RosterColumnMapping mapping)
    {
        var course = await _context.Courses.FindAsync(courseId);
        if (course is null) return;

        mapping.IdentifierPrompt = prompt;
        course.RosterMappingJson = System.Text.Json.JsonSerializer.Serialize(mapping);
        course.JoinPrompt = prompt;

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Upper-cases and strips anything that is not a letter or digit, so
    /// "csc 201-a" and "CSC201A" are the same course.
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