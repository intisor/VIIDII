using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
public static partial class RosterParser
{
    private static readonly char[] Delimiters = { ',', '\t', ';', '|' };

    private static readonly string[] CommonHeaderKeywords =
    {
        "name", "student", "matric", "phone", "email", "id", "code", "no", "number",
        "reg", "dept", "sn", "s/n", "contact", "participant", "member", "tel",
        "mobile", "whatsapp", "first", "last", "surname", "gender", "sex", "level",
        "faculty", "group", "score", "grade", "index", "roll", "title", "address",
        "programme", "program", "class", "cohort"
    };

    [GeneratedRegex(@"^[+()\-.\s\d]{7,25}$", RegexOptions.None, 200)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$", RegexOptions.None, 200)]
    private static partial Regex EmailRegex();

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
    /// Parses delimited text, detecting delimiter and automatically recognizing
    /// whether the first row is a header row or attendee data.
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
        var firstRowCells = SplitRespectingQuotes(lines[0], delimiter);

        if (firstRowCells.Count == 0 || firstRowCells.All(h => string.IsNullOrWhiteSpace(h)))
        {
            return new ParsedRoster { Format = RosterSourceFormat.Unknown, Warnings = ["Could not read a header row."] };
        }

        var isHeaderRow = LooksLikeHeaderRow(firstRowCells);
        List<string> headers;
        var startLineIndex = 1;

        if (isHeaderRow)
        {
            headers = firstRowCells.Select(h => h.Trim()).ToList();
        }
        else
        {
            headers = SynthesizeHeaders(firstRowCells);
            startLineIndex = 0;
            warnings.Add("No header row detected; inferred column types from data rows.");
        }

        var rows = new List<IReadOnlyList<string>>();
        for (var i = startLineIndex; i < lines.Count; i++)
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
            Headers = headers,
            Rows = rows,
            Format = delimiter == '\t' ? RosterSourceFormat.Tsv : RosterSourceFormat.Csv,
            Warnings = warnings
        };
    }

    private static bool LooksLikeHeaderRow(IReadOnlyList<string> cells)
    {
        if (cells.Count == 0) return false;

        foreach (var cell in cells)
        {
            var trimmed = cell.Trim();
            if (trimmed.Length == 0) continue;

            var tokens = trimmed.Split(new[] { ' ', '_', '-', '/', '.', ',', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (CommonHeaderKeywords.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> SynthesizeHeaders(IReadOnlyList<string> sampleCells)
    {
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sampleCells.Count; i++)
        {
            var cell = sampleCells[i].Trim();
            string candidate;

            if (PhoneRegex().IsMatch(cell) && cell.Count(char.IsAsciiDigit) >= 7)
            {
                candidate = "Phone Number";
            }
            else if (EmailRegex().IsMatch(cell))
            {
                candidate = "Email";
            }
            else if (sampleCells.Count == 1)
            {
                candidate = "Identifier";
            }
            else if (i == 0)
            {
                candidate = "Identifier";
            }
            else if (i == 1)
            {
                candidate = "Full Name";
            }
            else
            {
                candidate = $"Column {i + 1}";
            }

            var uniqueCandidate = candidate;
            var suffix = 2;
            while (!seen.Add(uniqueCandidate))
            {
                uniqueCandidate = $"{candidate} {suffix++}";
            }

            headers.Add(uniqueCandidate);
        }

        return headers;
    }

    /// <summary>
    /// Parses a JSON array of objects or strings. Also accepts an object wrapping the array
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

            var firstItem = arrayElement.EnumerateArray().FirstOrDefault();
            if (firstItem.ValueKind != JsonValueKind.Object)
            {
                var sample = ValueToString(firstItem).Trim();
                var singleHeader = PhoneRegex().IsMatch(sample) && sample.Count(char.IsAsciiDigit) >= 7
                    ? "Phone Number"
                    : EmailRegex().IsMatch(sample) ? "Email" : "Identifier";

                var primitiveRows = new List<IReadOnlyList<string>>();
                foreach (var element in arrayElement.EnumerateArray())
                {
                    primitiveRows.Add(new[] { ValueToString(element).Trim() });
                }

                return new ParsedRoster
                {
                    Headers = new[] { singleHeader },
                    Rows = primitiveRows,
                    Format = RosterSourceFormat.Json,
                    Warnings = warnings
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