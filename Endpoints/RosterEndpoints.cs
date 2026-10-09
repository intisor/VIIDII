using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Services.Roster;

namespace VIIDII.Endpoints;

/// <summary>
/// Roster management endpoints.
///
/// The roster is the expected-attendee list for a session. It replaces
/// in-memory participant collections with durable rows, so attendance evidence
/// can be re-derived from the database at any time.
/// </summary>
public static class RosterEndpoints
{
    public static void MapRosterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roster").RequireAuthorization();

        // Analyse an upload without persisting. Lets a lecturer see which columns
        // were detected before committing to an import.
        group.MapPost("/analyze", async (HttpContext http, RosterService rosterService) =>
        {
            var (parsed, analysis) = await Task.FromResult(rosterService.Preview(await ReadBodyAsync(http)));

            return Results.Ok(new
            {
                format = parsed.Format.ToString(),
                headers = parsed.Headers,
                rowCount = analysis.RowCount,
                isUsable = analysis.IsUsable,
                identifierColumn = Describe(analysis.IdentifierColumn),
                nameColumn = Describe(analysis.NameColumn),
                emailColumn = Describe(analysis.EmailColumn),
                phoneColumn = Describe(analysis.PhoneColumn),
                groupingColumn = Describe(analysis.GroupingColumn),
                columns = analysis.Columns.Select(c => new
                {
                    c.Name,
                    c.Uniqueness,
                    c.PersonNameScore,
                    c.IdentifierScore,
                    c.EmailScore,
                    c.PhoneScore,
                    c.GroupingScore,
                    c.Samples
                }),
                notes = analysis.Notes,
                warnings = parsed.Warnings
            });
        });

        // Import a roster for a session.
        group.MapPost("/sessions/{sessionId:int}/import", async (
            int sessionId,
            HttpContext http,
            RosterService rosterService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            if (!await IsSessionOwnerAsync(context, sessionId, currentUser.MatricNo))
            {
                return Results.Forbid();
            }

            var content = await ReadBodyAsync(http);
            var result = await rosterService.ImportAsync(sessionId, content);

            if (result.Errors.Count > 0 && result.Imported == 0)
            {
                return Results.BadRequest(new
                {
                    result.Errors,
                    result.Warnings
                });
            }

            return Results.Ok(new
            {
                result.Imported,
                result.Skipped,
                result.LinkedToExistingUser,
                result.UserAccountsCreated,
                result.Warnings,
                result.Errors,

                // Passcodes are returned once and never stored in clear text.
                result.Passcodes
            });
        });

        group.MapGet("/sessions/{sessionId:int}", async (
            int sessionId,
            RosterService rosterService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            if (!await IsSessionOwnerAsync(context, sessionId, currentUser.MatricNo))
            {
                return Results.Forbid();
            }

            var entries = await rosterService.GetForSessionAsync(sessionId);

            return Results.Ok(entries.Select(e => new
            {
                e.Id,
                e.Identifier,
                e.DisplayName,
                e.Email,
                e.Phone,
                e.Grouping,
                e.HasClaimed,
                e.ClaimedAtUtc,
                e.Source
            }));
        });

        // Fetch a roster from an institutional API, then analyse and import it.
        // Basic auth via Authorization header; the URL must be explicitly allowed.
        group.MapPost("/sessions/{sessionId:int}/fetch", async (
            int sessionId,
            HttpContext http,
            RosterService rosterService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            if (!await IsSessionOwnerAsync(context, sessionId, currentUser.MatricNo))
            {
                return Results.Forbid();
            }

            var request = await http.Request.ReadFromJsonAsync<FetchRosterRequest>();
            if (request is null || string.IsNullOrWhiteSpace(request.Url))
            {
                return Results.BadRequest(new { error = "A source URL is required." });
            }

            var allowList = configuration.GetSection("RosterSources:AllowedHosts").Get<string[]>()
                            ?? Array.Empty<string>();

            if (allowList.Length == 0)
            {
                return Results.BadRequest(new
                {
                    error = "No roster sources are allowed. Configure RosterSources:AllowedHosts."
                });
            }

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            {
                return Results.BadRequest(new { error = "Invalid URL." });
            }

            // Guard against server-side request forgery: without this an
            // authenticated lecturer could make the server fetch internal addresses.
            var host = uri.Host.ToLowerInvariant();
            if (!allowList.Any(h => string.Equals(h.ToLowerInvariant(), host, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest(new { error = $"Host '{host}' is not in the allowed list." });
            }

            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                return Results.BadRequest(new { error = "Roster sources must use HTTPS." });
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            if (!string.IsNullOrWhiteSpace(request.BearerToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.BearerToken);
            }

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(uri);
            }
            catch (Exception ex)
            {
                var logger = loggerFactory.CreateLogger("VIIDII.Roster");
                logger.LogWarning(ex, "Failed to fetch roster from {Host}", host);
                return Results.BadRequest(new { error = $"Could not reach the source: {ex.Message}" });
            }

            if (!response.IsSuccessStatusCode)
            {
                return Results.BadRequest(new
                {
                    error = $"Source returned {(int)response.StatusCode} {response.ReasonPhrase}."
                });
            }

            var body = await response.Content.ReadAsStringAsync();
            var result = await rosterService.ImportAsync(sessionId, body);

            return Results.Ok(new
            {
                source = host,
                result.Imported,
                result.Skipped,
                result.UserAccountsCreated,
                result.Warnings,
                result.Errors,
                result.Passcodes
            });
        });
    }

    private static async Task<bool> IsSessionOwnerAsync(
        ViidiiDbContext context, int sessionId, string? matricNo)
    {
        if (string.IsNullOrEmpty(matricNo))
        {
            return false;
        }

        var user = await context.Users.FirstOrDefaultAsync(u => u.MatricNo == matricNo);
        if (user is null)
        {
            return false;
        }

        // Admins may manage any roster; otherwise only the owning lecturer.
        if (user.Role == Role.Admin)
        {
            return true;
        }

        return await context.Sessions
            .AnyAsync(s => s.Id == sessionId && s.LecturerMatricNo == matricNo);
    }

    private static async Task<string> ReadBodyAsync(HttpContext http)
    {
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static object? Describe(ColumnProfile? column) =>
        column is null
            ? null
            : new
            {
                column.Name,
                column.Index,
                column.Uniqueness,
                column.Samples
            };

    private sealed record FetchRosterRequest(string? Url, string? BearerToken);
}