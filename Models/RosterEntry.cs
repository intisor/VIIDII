using System.ComponentModel.DataAnnotations;

namespace VIIDII.Models;

/// <summary>
/// A single expected attendee on a course class list.
/// Persisted rather than held in memory so attendance evidence survives restarts
/// and can be re-derived from the database alone.
///
/// Belongs to a Course, not a Session. A lecturer teaching 30 lectures uploads their
/// class list once; each session then draws its own attendance from this list.
/// </summary>
public class RosterEntry
{
    public int Id { get; set; }

    /// <summary>Owning course (FK to Courses.Id).</summary>
    public int CourseId { get; set; }

    /// <summary>The unique identifier the attendee types to join, e.g. matric number or staff ID.</summary>
    [Required, StringLength(64)]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Display name resolved from the roster.</summary>
    [Required, StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional email captured for absentee follow-up.</summary>
    [StringLength(200)]
    public string? Email { get; set; }

    /// <summary>Optional phone captured for WhatsApp absentee follow-up.</summary>
    [StringLength(40)]
    public string? Phone { get; set; }

    /// <summary>Role in this session. Staff run sessions; students attend them.</summary>
    public RosterRole Role { get; set; } = RosterRole.Student;

    /// <summary>Optional free-text department/level/unit, used for filtering and grouping.</summary>
    [StringLength(120)]
    public string? Grouping { get; set; }

    /// <summary>True once the attendee has claimed their seat for this session.</summary>
    public bool HasClaimed { get; set; }

    /// <summary>
    /// Opaque token recorded on first claim, so a leaked passcode cannot be reused
    /// from a second device to duplicate or steal an attendance record.
    /// </summary>
    [StringLength(128)]
    public string? ClaimToken { get; set; }

    public DateTime? ClaimedAtUtc { get; set; }

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

    public Session? Session { get; set; }

    public Course? Course { get; set; }

    /// <summary>Where this row came from: "csv", "json", or "api".</summary>
    [StringLength(20)]
    public string Source { get; set; } = "csv";
}

public enum RosterRole { Student, Lecturer, Admin }