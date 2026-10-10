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

        // Import a class list for a course.
        //
        // Accepts either a JSON envelope { content, mapping } (used by the upload
        // screen) or a raw text/csv body (used by scripts and integrations).
        group.MapPost("/courses/{courseId:int}/import", async (
            int courseId,
            HttpContext http,
            RosterService rosterService,
            CourseService courseService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            if (!await IsCourseOwnerAsync(context, courseId, currentUser.MatricNo))
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
            var result = await rosterService.ImportAsync(courseId, content, mapping: mapping);

            if (result.Errors.Count > 0 && result.Imported == 0)
            {
                return Results.BadRequest(new
                {
                    result.Errors,
                    result.Warnings
                });
            }

            // Persist the confirmed mapping so the student join screen can prompt with the
            // right label rather than a generic request.
            if (result.Imported > 0 && courseId > 0)
            {
                var prompt = mapping?.IdentifierPrompt
                             ?? $"Enter your {result.IdentifierHeader?.ToLowerInvariant()}";

                await courseService.SetJoinPromptAsync(
                    courseId,
                    prompt,
                    mapping ?? new RosterColumnMapping
                    {
                        IdentifierHeader = result.IdentifierHeader,
                        NameHeader = result.NameHeader
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
                result.IdentifierHeader,
                result.NameHeader
            });
        });

        // What a student sees before typing anything. Deliberately returns only labels,
        // never the class list, which would expose every attendee's name and
        // identifier to anyone holding the link.
        group.MapGet("/courses/{courseId:int}/join-prompt", async (
            int courseId,
            ViidiiDbContext context) =>
        {
            var course = await context.Courses
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == courseId);

            if (course is null)
            {
                return Results.NotFound(new { error = "Course not found." });
            }

            return Results.Ok(new
            {
                code = course.Code,
                title = course.Title,
                prompt = string.IsNullOrWhiteSpace(course.JoinPrompt)
                    ? "Enter your details to join"
                    : course.JoinPrompt,
                hasRoster = course.RosterMappingJson is not null
            });
        });

        group.MapGet("/courses/{courseId:int}", async (
            int courseId,
            RosterService rosterService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            if (!await IsCourseOwnerAsync(context, courseId, currentUser.MatricNo))
            {
                return Results.Forbid();
            }

            var entries = await rosterService.GetForCourseAsync(courseId);

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

        // Fetch a class list from an institutional API, then analyse and import it.
        // Basic auth via Authorization header; the URL must be explicitly allowed.
        group.MapPost("/courses/{courseId:int}/fetch", async (
            int courseId,
            HttpContext http,
            RosterService rosterService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            if (!await IsCourseOwnerAsync(context, courseId, currentUser.MatricNo))
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
            var result = await rosterService.ImportAsync(courseId, body);

            return Results.Ok(new
            {
                source = host,
                result.Imported,
                result.Skipped,
                result.UserAccountsCreated,
                result.Warnings,
                result.Errors
            });
        });

        // The student's join check: two fields, no password.
        //
        // Identity is self-asserted. This proves "someone with these details is on
        // the roster", not "this specific person attended" — see the warning in
        // RosterService.ResolveAttendeeAsync. Adequate for participation tracking;
        // not adequate for accreditation-grade certification.
        group.MapPost("/courses/{courseId:int}/resolve", async (
            int courseId,
            ResolveAttendeeRequest request,
            RosterService rosterService) =>
        {
            var entry = await rosterService.ResolveAttendeeAsync(
                courseId,
                request.Identifier ?? string.Empty,
                request.Name ?? string.Empty);

            if (entry is null)
            {
                // Deliberately vague: confirming that the identifier exists would
                // let anyone enumerate a class list.
                return Results.NotFound(new
                {
                    error = "Those details were not found on the class list. Check them and try again."
                });
            }

            return Results.Ok(new
            {
                entry.Id,
                entry.Identifier,
                entry.DisplayName,
                entry.HasClaimed
            });
        });
    }

    private static async Task<bool> IsCourseOwnerAsync(
        ViidiiDbContext context, int courseId, string? matricNo)
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

        // Admins may manage any class list; otherwise only the owning lecturer.
        if (user.Role == Role.Admin)
        {
            return true;
        }

        return await context.Courses
            .AnyAsync(c => c.Id == courseId && c.LecturerId == user.Id);
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

    /// <summary>Student-supplied identity: matric number plus name. No password.</summary>
    private sealed class ResolveAttendeeRequest
    {
        public string? Identifier { get; set; }
        public string? Name { get; set; }
    }

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