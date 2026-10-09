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
        // were detected, the ranked alternatives, and any cases needing confirmation.
        group.MapPost("/analyze", async (HttpContext http, RosterService rosterService) =>
        {
            var (parsed, analysis) = rosterService.Preview(await ReadBodyAsync(http));

            return Results.Ok(new
            {
                format = parsed.Format.ToString(),
                headers = parsed.Headers,
                rowCount = analysis.RowCount,
                isUsable = analysis.IsUsable,

                // Automatic picks.
                identifierColumn = Describe(analysis.IdentifierColumn),
                nameColumn = Describe(analysis.NameColumn),
                emailColumn = Describe(analysis.EmailColumn),
                phoneColumn = Describe(analysis.PhoneColumn),
                groupingColumn = Describe(analysis.GroupingColumn),

                // Ranked alternatives, so the lecturer can override.
                rankedIdentifiers = analysis.RankedIdentifiers.Select(Describe),
                rankedNames = analysis.RankedNames.Select(Describe),

                // Cases where the automatic pick is probably wrong.
                needsConfirmation = analysis.NeedsConfirmation,

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
        //
        // Accepts either a JSON envelope { content, mapping } (used by the upload
        // screen) or a raw text/csv body (used by scripts and integrations).
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

            ImportRosterRequest? request = null;
            string content;

            if (http.Request.HasJsonContentType())
            {
                request = await http.Request.ReadFromJsonAsync<ImportRosterRequest>();
                if (request is null || string.IsNullOrWhiteSpace(request.Content))
                {
                    return Results.BadRequest(new { error = "A 'content' field is required." });
                }

                content = request.Content;
            }
            else
            {
                content = await ReadBodyAsync(http);
            }

            var mapping = request?.Mapping;

            // An explicit mapping always wins over the automatic column pick.
            var result = await rosterService.ImportAsync(sessionId, content, mapping: mapping);

            if (result.Errors.Count > 0 && result.Imported == 0)
            {
                return Results.BadRequest(new
                {
                    result.Errors,
                    result.Warnings
                });
            }

            // Persist the confirmed mapping so the student join screen can prompt
            // with the right label rather than a generic request.
            if (result.Imported > 0)
            {
                var prompt = mapping?.IdentifierPrompt
                             ?? (mapping is not null
                                 ? $"Enter your {mapping.IdentifierHeader?.ToLowerInvariant()}"
                                 : $"Enter your {result.IdentifierHeader?.ToLowerInvariant()}");

                await rosterService.SetColumnMappingAsync(
                    sessionId,
                    mapping ?? new RosterColumnMapping
                    {
                        IdentifierHeader = result.IdentifierHeader,
                        NameHeader = result.NameHeader
                    },
                    prompt);
            }

            return Results.Ok(new
            {
                result.Imported,
                result.Skipped,
                result.LinkedToExistingUser,
                result.UserAccountsCreated,
                result.Warnings,
                result.Errors,
                result.IdentifierHeader,
                result.NameHeader,

                // Passcodes are returned once and never stored in clear text.
                result.Passcodes
            });
        });

        // The prompt a student sees on the join screen. Deliberately returns only
        // the label — never the roster itself, which would expose every attendee's
        // name and identifier to anyone holding the link.
        group.MapGet("/sessions/{sessionId:int}/join-prompt", async (
            int sessionId,
            ViidiiDbContext context) =>
        {
            var session = await context.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session is null)
            {
                return Results.NotFound(new { error = "Session not found." });
            }

            return Results.Ok(new
            {
                sessionId = session.SessionId,
                title = session.Title,
                prompt = string.IsNullOrWhiteSpace(session.RosterIdentifierPrompt)
                    ? "Enter your details to join"
                    : session.RosterIdentifierPrompt,
                hasRoster = session.RosterMappingJson is not null,
                started = session.Status == SessionStatus.Started
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

    /// <summary>
    /// JSON envelope used by the upload screen. Plain text bodies are also accepted.
    /// </summary>
    private sealed class ImportRosterRequest
    {
        public string? Content { get; set; }

        /// <summary>
        /// Lecturer's confirmed column mapping. When null the automatic pick is used.
        /// </summary>
        public RosterColumnMapping? Mapping { get; set; }
    }
}