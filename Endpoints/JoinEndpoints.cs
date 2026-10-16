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

            var studentCount = await classService.GetStudentCountAsync(target.Id);
            var hasList = target.RosterMappingJson is not null || studentCount > 0;

            return Results.Ok(new
            {
                target.Code,
                target.Name,
                target.PresetType,
                prompt = string.IsNullOrWhiteSpace(target.JoinPrompt)
                    ? SpacePreset.GetMeta(target.PresetType).DefaultPrompt
                    : target.JoinPrompt,
                hasList,
                requiresList = hasList
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
            var isTakeover = string.Equals(form["takeover"], "1", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(form["takeover"], "true", StringComparison.OrdinalIgnoreCase);

            var target = await classService.GetByCodeAsync(code);
            if (target is null)
            {
                return Results.NotFound();
            }

            if (target.RosterMappingJson is null && await classService.GetStudentCountAsync(target.Id) == 0)
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

            // Already holding this exact seat on this device. Re-issuing lets a student
            // who refreshed, or whose first response was lost, back in.
            var held = seats.Read(http);
            var alreadyOurs = held is not null
                              && held.ClassId == target.Id
                              && string.Equals(held.ClaimToken, entry.ClaimToken, StringComparison.Ordinal);

            string claimToken;

            if (alreadyOurs)
            {
                claimToken = entry.ClaimToken!;
                logger.LogInformation(
                    "{Identifier} re-entered on the device holding their seat", entry.Identifier);
            }
            else
            {
                claimToken = Guid.NewGuid().ToString("N");

                // Check whether there is an ongoing live session for this class.
                var liveList = await sessionService.GetLiveSessionsForClassAsync(target.Id);
                var noLiveSession = !liveList.Any();

                // If no live session is active, or if takeover is explicitly requested, allow claiming immediately.
                var claimed = await rosterService.ClaimSeatAsync(entry.Id, claimToken, takeOver: isTakeover || noLiveSession);

                if (!claimed)
                {
                    // Held from a different device during an active lecture.
                    // Retain identifier and name so the student can one-click transfer the seat to this device.
                    return Results.Redirect(
                        $"/join/{Uri.EscapeDataString(code)}?error=already-claimed&identifier={Uri.EscapeDataString(identifier)}&name={Uri.EscapeDataString(name)}");
                }
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

            if (target.RosterMappingJson is null && await classService.GetStudentCountAsync(target.Id) == 0)
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
            var claimed = await rosterService.ClaimSeatAsync(entry.Id, claimToken, takeOver: request.Takeover);

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
        public bool Takeover { get; set; }
    }
}
