namespace VIIDII.Models;

/// <summary>
/// Predefined configuration templates for different industries and use-cases.
/// Keeps VIIDII completely domain-agnostic while providing sensible defaults.
/// </summary>
public static class SpacePreset
{
    public const string Academic = "academic";
    public const string Professional = "professional"; // ICAN, ACCA, CFA, Bar Prep, Medical
    public const string Corporate = "corporate";       // AGM, Shareholder meetings, Boardrooms
    public const string Masterclass = "masterclass";   // Tech bootcamps, VIP workshops, Trading
    public const string Custom = "custom";

    public record PresetMeta(
        string Key,
        string Title,
        string Description,
        string DefaultPrompt,
        string HostLabel,
        string AttendeeLabel,
        string IconEmoji
    );

    public static readonly IReadOnlyList<PresetMeta> All = new List<PresetMeta>
    {
        new(
            Academic,
            "Academic / University",
            "For university courses, polytechnics, and colleges.",
            "Enter your matric number or phone number",
            "Lecturer",
            "Student",
            "🎓"
        ),
        new(
            Professional,
            "Professional Exam / Tuition",
            "For ICAN, ACCA, CFA, Bar exam, and CIPM tuition centres.",
            "Enter your registration number or phone number",
            "Tutor / Facilitator",
            "Candidate",
            "📊"
        ),
        new(
            Corporate,
            "Corporate AGM & Boardroom",
            "For shareholder meetings, investor calls, and executive boards.",
            "Enter your shareholder ID or CSCS number",
            "Chairman / Host",
            "Shareholder / Delegate",
            "🏢"
        ),
        new(
            Masterclass,
            "Masterclass & Tech Bootcamp",
            "For paid developer bootcamps, trading rooms, and VIP summits.",
            "Enter your ticket reference or WhatsApp number",
            "Speaker / Lead Trainer",
            "Attendee / Member",
            "🚀"
        ),
        new(
            Custom,
            "Custom Space",
            "Tailor your own prompts, access keys, and security rules.",
            "Enter your access code or phone number",
            "Host",
            "Participant",
            "⚙️"
        )
    };

    public static PresetMeta GetMeta(string? presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
        {
            return All[0]; // Default to Academic
        }

        var match = All.FirstOrDefault(p => string.Equals(p.Key, presetKey.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? All[0];
    }
}
