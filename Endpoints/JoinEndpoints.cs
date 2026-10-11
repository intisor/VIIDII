using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Student join flow. No account, no password.
///
/// A student enters two things the lecturer chose: their identifier (matric
/// number, or whatever column the lecturer picked) and their name. If both match a
/// row on the class list, they are in.
///
/// Identity is self-asserted. This proves "someone with these details is on the
/// list", not "this specific person attended". Fine for participation tracking,
/// not sufficient for accreditation-grade certification.
/// </summary>
public static class JoinEndpoints
{
    public static void MapJoinEndpoints(this IEndpointRouteBuilder app)
    {
        // Look up the class by its public code. Labels only, never the list.
        app.MapGet("/api/join/{code}", async (string code, ClassService classService) =>
        {
            var target = await classService.GetByCodeAsync(code);
            if (target is null)
            {
                return Results.NotFound(new { error = "No class was found with that code." });
            }

            return Results.Ok(new
            {
                target.Code,
                target.Name,
                prompt = string.IsNullOrWhiteSpace(target.JoinPrompt)
                    ? "Enter your details to join"
                    : target.JoinPrompt,
                hasList = target.RosterMappingJson is not null,
                // Signals to the UI that a list exists, so it can ask for two fields
                // rather than pretending a login is needed.
                requiresList = target.RosterMappingJson is not null
            });
        });

        // The form post behind the join screen. Server rendered like sign in, so it
        // costs one request on a slow network and issues the seat cookie in the same
        // response that validates the details.
        //
        // Posted to /join/claim/{code}, not /join/{code}. The join screen itself is a Razor
        // page registered for every method, so sharing the path made every POST match two
        // endpoints and fail routing with an ambiguous match.
        app.MapPost("/join/claim/{code}", async (
            string code,
            HttpContext http,
            IAntiforgery antiforgery,
            ClassService classService,
            RosterService rosterService,
            SessionService sessionService,
            StudentSeatService seats,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("VIIDII.Join");

            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("This page sat open too long. Please try again.");
            }

            var form = await http.Request.ReadFormAsync();
            var identifier = form["identifier"].ToString().Trim();
            var name = form["name"].ToString().Trim();

            var target = await classService.GetByCodeAsync(code);
            if (target is null)
            {
                return Results.NotFound();
            }

            if (target.RosterMappingJson is null)
            {
                return Results.Redirect($"/join/{Uri.EscapeDataString(code)}?error=no-list");
            }

            var entry = await rosterService.ResolveAttendeeAsync(target.Id, identifier, name);
            if (entry is null)
            {
                // Deliberately vague. Confirming an identifier exists would let anyone
                // walk the class list.
                return Results.Redirect(
                    $"/join/{Uri.EscapeDataString(code)}?error=not-found&identifier={Uri.EscapeDataString(identifier)}&name={Uri.EscapeDataString(name)}");
            }

            var claimToken = Guid.NewGuid().ToString("N");
            var claimed = await rosterService.ClaimSeatAsync(entry.Id, claimToken);

            if (!claimed)
            {
                // The same roster row is already held from another device. Refusing
                // here stops one person being counted twice, but it also means a
                // student who simply reopened the page is now locked out.
                return Results.Redirect(
                    $"/join/{Uri.EscapeDataString(code)}?error=already-claimed&identifier={Uri.EscapeDataString(identifier)}");
            }

            seats.Issue(http, new StudentSeat(entry.Identifier, entry.DisplayName, target.Id, claimToken));

            logger.LogInformation(
                "{Identifier} claimed a seat in class {ClassCode}", entry.Identifier, target.Code);

            var live = await sessionService.GetLiveSessionsForClassAsync(target.Id);
            var room = live.FirstOrDefault();

            if (room is null)
            {
                // Nothing running yet. Stay on the join link so the student can retry
                // once the lecture starts, rather than dropping them on a dead end.
                return Results.Redirect($"/join/{Uri.EscapeDataString(code)}?waiting=1");
            }

            return Results.Redirect($"/session/{Uri.EscapeDataString(room.SessionId)}");
        });

        // Verify the two fields and hand back a short-lived claim for this session.
        app.MapPost("/api/join/{code}", async (
            string code,
            JoinRequest request,
            ClassService classService,
            RosterService rosterService) =>
        {
            var target = await classService.GetByCodeAsync(code);
            if (target is null)
            {
                return Results.NotFound(new { error = "No class was found with that code." });
            }

            if (target.RosterMappingJson is null)
            {
                return Results.BadRequest(new
                {
                    error = "Your lecturer has not added a class list yet. Try again once they have."
                });
            }

            var entry = await rosterService.ResolveAttendeeAsync(
                target.Id,
                request.Identifier ?? string.Empty,
                request.Name ?? string.Empty);

            if (entry is null)
            {
                // Deliberately vague. Confirming that an identifier exists would let
                // anyone enumerate the class list.
                return Results.NotFound(new
                {
                    error = "Those details were not found on the class list. Check them and try again."
                });
            }

            // Claim the seat so the same class list row cannot be held from two devices.
            var claimToken = Guid.NewGuid().ToString("N");
            var claimed = await rosterService.ClaimSeatAsync(entry.Id, claimToken);

            return Results.Ok(new
            {
                classCode = target.Code,
                className = target.Name,
                identifier = entry.Identifier,
                displayName = entry.DisplayName,
                claimToken,
                alreadyClaimedElsewhere = !claimed
            });
        });
    }

    private sealed class JoinRequest
    {
        public string? Identifier { get; set; }
        public string? Name { get; set; }
    }
}
