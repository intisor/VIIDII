using System.ComponentModel.DataAnnotations;

namespace VIIDII.Models;

/// <summary>
/// A named list of students a lecturer teaches, e.g. "100 Level Computer Science".
///
/// This is the whole container. A lecturer has one of these per class they teach,
/// uploads the student list once, and every session they run can point at it.
///
/// Deliberately simple: no course catalogue, no semesters, no institution. Those can
/// be attached later without moving data.
/// </summary>
public class Class
{
    public int Id { get; set; }

    /// <summary>
    /// Short code used in the join link, e.g. "CS100". Unique across the platform so
    /// the link is not guessable by anyone outside the class.
    /// </summary>
    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    /// <summary>The lecturer's own name for this group, e.g. "100 Level Computer Science".</summary>
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Owning lecturer's user id.</summary>
    public int LecturerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// What students are asked for on the join screen, e.g. "Enter your matric number".
    /// Set when the lecturer confirms the class list columns.
    /// </summary>
    [StringLength(120)]
    public string? JoinPrompt { get; set; }

    /// <summary>
    /// Industry / use-case preset: "academic", "professional", "corporate", "masterclass", "custom".
    /// </summary>
    [StringLength(32)]
    public string PresetType { get; set; } = SpacePreset.Academic;

    /// <summary>
    /// Whether dynamic anti-piracy screen watermarking is active during live sessions.
    /// </summary>
    public bool EnableWatermark { get; set; } = true;

    /// <summary>Confirmed class list column mapping as JSON.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RosterMappingJson { get; set; }

    // Navigation
    public User? Lecturer { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ICollection<RosterEntry> Students { get; set; } = new List<RosterEntry>();

    [System.Text.Json.Serialization.JsonIgnore]
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}