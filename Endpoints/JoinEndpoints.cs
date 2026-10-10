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