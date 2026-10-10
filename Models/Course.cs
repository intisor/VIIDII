using System.ComponentModel.DataAnnotations;

namespace VIIDII.Models;

/// <summary>
/// A course taught by a lecturer, e.g. "CSC201 - Data Structures".
///
/// Owns the class list. This is the fix for the workflow problem: a lecturer who
/// runs 30 lectures a semester should upload their class list once, not 30 times.
///
/// Institution-first is deferred. A course is owned by a lecturer and may later be
/// attached to a department or centre without moving any data.
/// </summary>
public class Course
{
    public int Id { get; set; }

    /// <summary>
    /// Short public code used in the join link, e.g. "CSC201A". Unique across the
    /// platform so the link is guessable by nobody but the students.
    /// </summary>
    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Owning lecturer's user id.</summary>
    public int LecturerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Prompt shown to students on the join screen, e.g. "Enter your matric number".
    /// Set when the lecturer confirms the class list columns.
    /// </summary>
    [StringLength(120)]
    public string? JoinPrompt { get; set; }

    /// <summary>Confirmed roster column mapping as JSON.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RosterMappingJson { get; set; }

    // Navigation
    public User? Lecturer { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ICollection<RosterEntry> Roster { get; set; } = new List<RosterEntry>();

    [System.Text.Json.Serialization.JsonIgnore]
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}