using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Universal attendee management, API ingestion, voucher generation, and attendance exports.
/// Allows any CRM, Zapier, ticketing system, or external script to programmatically manage space access.
/// </summary>
public static class AttendeeEndpoints
{
    public static void MapAttendeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/spaces");

        // 1. Programmatic single or batch attendee enrollment
        group.MapPost("/{code}/attendees", async (
            string code,
            HttpContext http,
            ClassService classService,
            ViidiiDbContext context) =>
        {
            var space = await classService.GetByCodeAsync(code);
            if (space is null)
            {
                return Results.NotFound(new { error = $"Space '{code}' not found." });
            }

            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            var attendees = ParseAttendeePayload(rawBody);
            if (attendees.Count == 0)
            {
                return Results.BadRequest(new { error = "Provide an attendee object with 'identifier' or an array of attendees." });
            }

            var added = 0;
            var updated = 0;
            var existingEntries = await context.RosterEntries
                .Where(r => r.ClassId == space.Id)
                .ToDictionaryAsync(r => r.Identifier, StringComparer.OrdinalIgnoreCase);

            foreach (var item in attendees)
            {
                var cleanId = item.Identifier?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cleanId)) continue;

                var cleanName = string.IsNullOrWhiteSpace(item.Name) ? cleanId : item.Name.Trim();
                var cleanEmail = item.Email?.Trim();
                var cleanPhone = item.Phone?.Trim();
                var cleanGroup = item.Grouping?.Trim() ?? "API Ingested";

                if (existingEntries.TryGetValue(cleanId, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(item.Name)) existing.DisplayName = cleanName;
                    if (!string.IsNullOrWhiteSpace(cleanEmail)) existing.Email = cleanEmail;
                    if (!string.IsNullOrWhiteSpace(cleanPhone)) existing.Phone = cleanPhone;
                    if (!string.IsNullOrWhiteSpace(cleanGroup)) existing.Grouping = cleanGroup;
                    updated++;
                }
                else
                {
                    var entry = new RosterEntry
                    {
                        ClassId = space.Id,
                        Identifier = cleanId,
                        DisplayName = cleanName,
                        Email = cleanEmail,
                        Phone = cleanPhone,
                        Grouping = cleanGroup,
                        Source = "api",
                        Role = RosterRole.Student,
                        ImportedAtUtc = DateTime.UtcNow
                    };
                    context.RosterEntries.Add(entry);
                    existingEntries[cleanId] = entry;
                    added++;
                }
            }

            await context.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                space = space.Code,
                added,
                updated,
                totalAttendees = existingEntries.Count
            });
        });

        // 2. Batch Voucher / Passcode Generator (Scratch cards, ticket badges, physical passes)
        group.MapPost("/{code}/vouchers", async (
            string code,
            GenerateVouchersRequest request,
            ClassService classService,
            ViidiiDbContext context) =>
        {
            var space = await classService.GetByCodeAsync(code);
            if (space is null)
            {
                return Results.NotFound(new { error = $"Space '{code}' not found." });
            }

            var count = Math.Clamp(request.Count ?? 10, 1, 500);
            var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? "PASS" : request.Prefix.Trim().ToUpperInvariant();
            var grouping = string.IsNullOrWhiteSpace(request.Grouping) ? "Access Voucher" : request.Grouping.Trim();

            var existingIds = new HashSet<string>(
                await context.RosterEntries
                    .Where(r => r.ClassId == space.Id)
                    .Select(r => r.Identifier)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            var generatedCodes = new List<string>();
            var newEntries = new List<RosterEntry>();

            for (var i = 0; i < count; i++)
            {
                string codeCandidate;
                do
                {
                    var randomDigits = RandomNumberGenerator.GetInt32(100000, 999999);
                    codeCandidate = $"{prefix}-{randomDigits}";
                }
                while (existingIds.Contains(codeCandidate));

                existingIds.Add(codeCandidate);
                generatedCodes.Add(codeCandidate);

                newEntries.Add(new RosterEntry
                {
                    ClassId = space.Id,
                    Identifier = codeCandidate,
                    DisplayName = codeCandidate,
                    Grouping = grouping,
                    Source = "voucher",
                    Role = RosterRole.Student,
                    ImportedAtUtc = DateTime.UtcNow
                });
            }

            context.RosterEntries.AddRange(newEntries);
            await context.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                space = space.Code,
                count = generatedCodes.Count,
                vouchers = generatedCodes
            });
        });

        // 3. Complete Space Attendance Audit CSV Export
        group.MapGet("/{code}/attendance.csv", async (
            string code,
            ClassService classService,
            ViidiiDbContext context) =>
        {
            var space = await classService.GetByCodeAsync(code);
            if (space is null)
            {
                return Results.NotFound(new { error = $"Space '{code}' not found." });
            }

            var entries = await context.RosterEntries
                .Where(r => r.ClassId == space.Id)
                .OrderBy(r => r.DisplayName)
                .ToListAsync();

            var csv = new StringBuilder();
            csv.AppendLine("Identifier,DisplayName,Phone,Email,Grouping,Status,ClaimedAtUtc,Source");

            foreach (var e in entries)
            {
                var status = e.HasClaimed ? "Claimed / Present" : "Unclaimed / Absent";
                var claimedAt = e.ClaimedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "";
                csv.AppendLine(string.Join(",",
                    EscapeCsv(e.Identifier),
                    EscapeCsv(e.DisplayName),
                    EscapeCsv(e.Phone ?? ""),
                    EscapeCsv(e.Email ?? ""),
                    EscapeCsv(e.Grouping ?? ""),
                    EscapeCsv(status),
                    EscapeCsv(claimedAt),
                    EscapeCsv(e.Source)));
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return Results.File(bytes, "text/csv", $"{space.Code}_attendance_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        });
    }

    private static List<AttendeeDto> ParseAttendeePayload(string raw)
    {
        var list = new List<AttendeeDto>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var dto = ExtractDto(el);
                    if (dto is not null) list.Add(dto);
                }
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("attendees", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var dto = ExtractDto(el);
                        if (dto is not null) list.Add(dto);
                    }
                }
                else
                {
                    var dto = ExtractDto(doc.RootElement);
                    if (dto is not null) list.Add(dto);
                }
            }
        }
        catch
        {
            // Plain text newline fallback (e.g. list of phone numbers or emails)
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    list.Add(new AttendeeDto(trimmed, trimmed, null, null, "Manual Feed"));
                }
            }
        }

        return list;
    }

    private static AttendeeDto? ExtractDto(System.Text.Json.JsonElement el)
    {
        string? id = null;
        string? name = null;
        string? email = null;
        string? phone = null;
        string? grouping = null;

        foreach (var prop in el.EnumerateObject())
        {
            var pName = prop.Name.ToLowerInvariant();
            var val = prop.Value.GetString();
            if (pName is "identifier" or "id" or "phone" or "phonenumber" or "matric" or "matricno" or "ticket" or "ticketref")
            {
                id ??= val;
            }
            if (pName is "name" or "displayname" or "fullname")
            {
                name = val;
            }
            if (pName is "email")
            {
                email = val;
            }
            if (pName is "phone" or "phonenumber")
            {
                phone = val;
            }
            if (pName is "group" or "grouping" or "tier")
            {
                grouping = val;
            }
        }

        if (string.IsNullOrWhiteSpace(id)) return null;
        return new AttendeeDto(id, name, email, phone, grouping);
    }

    private static string EscapeCsv(string val)
    {
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }

    public record AttendeeDto(string Identifier, string? Name, string? Email, string? Phone, string? Grouping);

    public record GenerateVouchersRequest(int? Count, string? Prefix, string? Grouping);
}
