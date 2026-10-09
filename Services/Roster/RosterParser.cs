using System.Text;
using System.Text.Json;

namespace VIIDII.Services.Roster;

/// <summary>
/// A parsed roster: headers plus rows, independent of the source format.
/// </summary>
public class ParsedRoster
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public RosterSourceFormat Format { get; init; }

    /// <summary>True when the parser had to guess or repair the structure.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public enum RosterSourceFormat { Csv, Tsv, Json, Unknown }

/// <summary>
/// Parses roster data from CSV, TSV or JSON.
///
/// Deliberately forgiving: institution exports are inconsistent, and rejecting a
/// cohort list over a stray comma would be the wrong trade-off. Problems are
/// recorded as warnings rather than thrown.
/// </summary>
public static class RosterParser
{
    private static readonly char[] Delimiters = { ',', '\t', ';', '|' };

    /// <summary>
    /// Detects the format and parses. JSON is tried first when the payload
    /// starts with '[' or '{'.
    /// </summary>
    public static ParsedRoster Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var trimmed = content.TrimStart('﻿', ' ', '\t', '\r', '\n');

        if (trimmed.Length == 0)
        {
            return new ParsedRoster { Warnings = ["Content was empty."] };
        }

        return trimmed[0] is '[' or '{'
            ? ParseJson(trimmed)
            : ParseDelimited(trimmed);
    }

    /// <summary>
    /// Parses delimited text, detecting the delimiter from the header line.
    /// </summary>
    public static ParsedRoster ParseDelimited(string content)
    {
        var warnings = new List<string>();
        var lines = SplitLines(content);

        if (lines.Count == 0)
        {
            return new ParsedRoster { Warnings = ["No content."] };
        }

        var delimiter = DetectDelimiter(lines[0], warnings);
        var headers = SplitRespectingQuotes(lines[0], delimiter);

        if (headers.Count == 0 || headers.All(h => string.IsNullOrWhiteSpace(h)))
        {
            return new ParsedRoster { Format = RosterSourceFormat.Unknown, Warnings = ["Could not read a header row."] };
        }

        var rows = new List<IReadOnlyList<string>>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = SplitRespectingQuotes(lines[i], delimiter);

            if (cells.Count != headers.Count)
            {
                warnings.Add($"Row {i + 1}: expected {headers.Count} columns but found {cells.Count}. Padded or trimmed.");

                // Pad short rows, truncate long ones — do not silently drop the attendee.
                var repaired = new List<string>(headers.Count);
                for (var c = 0; c < headers.Count; c++)
                {
                    repaired.Add(c < cells.Count ? cells[c] : string.Empty);
                }

                cells = repaired;
            }

            rows.Add(cells);
        }

        return new ParsedRoster
        {
            Headers = headers.Select(h => h.Trim()).ToList(),
            Rows = rows,
            Format = delimiter == '\t' ? RosterSourceFormat.Tsv : RosterSourceFormat.Csv,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Parses a JSON array of objects. Also accepts an object wrapping the array
    /// under common keys (data, results, records, items, students).
    /// </summary>
    public static ParsedRoster ParseJson(string content)
    {
        var warnings = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            JsonElement arrayElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     TryFindArrayProperty(root, out var found))
            {
                arrayElement = found;
                warnings.Add("Array was nested under an object; unwrapped automatically.");
            }
            else
            {
                return new ParsedRoster
                {
                    Format = RosterSourceFormat.Json,
                    Warnings = ["JSON must be an array of objects, or an object containing one."]
                };
            }

            if (arrayElement.ValueKind != JsonValueKind.Array || arrayElement.GetArrayLength() == 0)
            {
                return new ParsedRoster
                {
                    Format = RosterSourceFormat.Json,
                    Warnings = ["JSON array was empty."]
                };
            }

            // Union of all keys so rows with differing shapes stay aligned.
            var headers = new List<string>();
            var headerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in arrayElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                foreach (var property in element.EnumerateObject())
                {
                    if (headerSet.Add(property.Name))
                    {
                        headers.Add(property.Name);
                    }
                }
            }

            if (headers.Count == 0)
            {
                return new ParsedRoster
                {
                    Format = RosterSourceFormat.Json,
                    Warnings = ["No properties found on the JSON objects."]
                };
            }

            var rows = new List<IReadOnlyList<string>>();
            foreach (var element in arrayElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    warnings.Add("Skipped a JSON array entry that was not an object.");
                    continue;
                }

                var row = new List<string>(headers.Count);
                for (var i = 0; i < headers.Count; i++)
                {
                    row.Add(element.TryGetProperty(headers[i], out var value)
                        ? ValueToString(value)
                        : string.Empty);
                }

                rows.Add(row);
            }

            return new ParsedRoster
            {
                Headers = headers,
                Rows = rows,
                Format = RosterSourceFormat.Json,
                Warnings = warnings
            };
        }
        catch (JsonException ex)
        {
            return new ParsedRoster
            {
                Format = RosterSourceFormat.Unknown,
                Warnings = [$"Could not parse JSON: {ex.Message}"]
            };
        }
    }

    private static bool TryFindArrayProperty(JsonElement root, out JsonElement array)
    {
        foreach (var name in new[] { "data", "results", "records", "items", "students", "roster", "rows" })
        {
            if (root.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
            {
                array = candidate;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static string ValueToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText()
    };

    private static char DetectDelimiter(string headerLine, List<string> warnings)
    {
        var counts = Delimiters.ToDictionary(d => d, d => headerLine.Count(c => c == d));

        var best = counts.OrderByDescending(kv => kv.Value).First();
        var runnerUp = counts.OrderByDescending(kv => kv.Value).Skip(1).First();

        if (best.Value == 0)
        {
            warnings.Add("No delimiter detected in the header row; treating the file as a single column.");
            return ',';
        }

        // An ambiguous tie means quoted content probably contains the delimiter.
        if (best.Value == runnerUp.Value)
        {
            warnings.Add("Delimiter is ambiguous; the most common delimiter was used.");
        }

        return best.Key;
    }

    private static List<string> SplitLines(string content)
    {
        // Normalise CRLF/CR so pasted-from-Excel content works.
        var normalised = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return normalised.Split('\n').ToList();
    }

    /// <summary>
    /// Splits a delimited line, honouring double-quoted fields and "" escapes.
    /// A quoted field may contain the delimiter or newlines.
    /// </summary>
    public static List<string> SplitRespectingQuotes(string line, char delimiter)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    result.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(c);
                }
            }
        }

        result.Add(builder.ToString());
        return result;
    }
}