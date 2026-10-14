using VIIDII.Services.Roster;

namespace VIIDII.Services;

/// <summary>
/// Explicit column mapping chosen by a lecturer, overriding the automatic pick.
///
/// Populated from the upload screen's ranked suggestions. Header names are used
/// rather than indexes because indexes shift when a spreadsheet is edited, and a
/// silent mis-mapping would put the wrong field in front of students.
/// </summary>
public class RosterColumnMapping
{
    /// <summary>Header of the column students type to join.</summary>
    public string? IdentifierHeader { get; set; }

    /// <summary>Header of the column shown as the attendee's name.</summary>
    public string? NameHeader { get; set; }

    public string? EmailHeader { get; set; }
    public string? PhoneHeader { get; set; }
    public string? GroupingHeader { get; set; }

    /// <summary>
    /// Phrasing for the student join screen, e.g. "Enter your matric number".
    /// Falls back to a generic prompt when unset.
    /// </summary>
    public string? IdentifierPrompt { get; set; }

    /// <summary>
    /// Resolves a header name to its column index, case-insensitively.
    /// Returns -1 when the header is absent from the dataset.
    /// </summary>
    public int IndexOf(IReadOnlyList<string> headers, string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return -1;
        }

        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i]?.Trim(), header.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Validates that the mapping is usable for a given dataset.
    /// Returns the failure reason, or null when the mapping is valid.
    /// </summary>
    public string? Validate(IReadOnlyList<string> headers)
    {
        var identifierIndex = IndexOf(headers, IdentifierHeader);

        if (identifierIndex < 0)
        {
            return $"Column '{IdentifierHeader}' was not found in this file.";
        }

        if (!string.IsNullOrWhiteSpace(NameHeader))
        {
            var nameIndex = IndexOf(headers, NameHeader);
            if (nameIndex < 0)
            {
                return $"Column '{NameHeader}' was not found in this file.";
            }

            if (headers.Count > 1 && identifierIndex == nameIndex)
            {
                return "The join identifier and the display name cannot be the same column.";
            }
        }

        return null;
    }
}