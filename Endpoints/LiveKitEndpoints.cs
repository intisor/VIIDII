using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Issues the tokens that let a browser join the LiveKit media room.
/// <para>
/// The browser cannot talk to the media server on its own: it needs a signed token,
/// and only this server holds the key that signs one. So the room is guarded here,
/// using the same identity rules the hub already enforces.
/// </para>
/// <para>
/// The seat cookie is the important case. A student holds no account and no claims,
/// so the cookie is the only thing that says who they are - and it was validated
/// against the roster when they claimed it. It carries the class it was issued for,
/// and a seat is only good for sessions of that class. Without that check a student
/// holding a seat for one class could walk into any other class's lecture by
/// changing the session id in the address.
/// </para>
/// </summary>
public static class LiveKitEndpoints
{
    public static void MapLiveKitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/livekit/token", async (
            string sessionId,
            HttpContext http,
            SessionService sessionService,
            UserService users,
            StudentSeatService seats,
            LiveKitTokenService tokens,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("VIIDII.LiveKit");

            if (!tokens.IsConfigured)
            {
                return Results.Problem("Video is not configured on this server.");
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Results.BadRequest(new { error = "No session was given." });
            }

            var session = await sessionService.GetSessionByIdAsync(sessionId);
            if (session is null)
            {
                return Results.NotFound(new { error = "That session does not exist." });
            }

            if (session.Status == SessionStatus.Ended)
            {
                return Results.BadRequest(new { error = "That session has already ended." });
            }

            string matricNo;
            string displayName;
            var canPublish = false;

            var signedIn = http.User?.FindFirst(ViidiiClaims.MatricNo)?.Value;
            if (!string.IsNullOrWhiteSpace(signedIn))
            {
                // The lecturer. Owning the session is the whole test - being signed
                // in as some other lecturer proves nothing about this room.
                if (!string.Equals(session.LecturerMatricNo, signedIn,
                        StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Refused video token for session {SessionId}: {MatricNo} is not its lecturer.",
                        sessionId, signedIn);
                    return Results.Forbid();
                }

                var user = await users.GetUserByMatricNoAsync(signedIn);
                matricNo = signedIn;
                displayName = user?.Name ?? signedIn;
                canPublish = true;
            }
            else
            {
                var seat = seats.Read(http);
                if (seat is null)
                {
                    return Results.Forbid();
                }

                if (session.ClassId is not int classId || seat.ClassId != classId)
                {
                    logger.LogWarning(
                        "Refused video token for session {SessionId}: seat for class {SeatClass} does not match class {SessionClass}.",
                        sessionId, seat.ClassId, session.ClassId);
                    return Results.Forbid();
                }

                matricNo = seat.MatricNo;
                displayName = seat.DisplayName;
                // A student watches. The token says so, so a publish attempt is
                // refused by the media server rather than merely discouraged here.
            }

            var (token, error) = tokens.Issue(sessionId, matricNo, displayName, canPublish);
            if (token is null)
            {
                logger.LogError("Could not issue a video token for session {SessionId}: {Error}",
                    sessionId, error);
                return Results.Problem(error ?? "Could not issue a video token.");
            }

            logger.LogInformation(
                "Issued video token for session {SessionId}, {MatricNo}, publish={CanPublish}.",
                sessionId, matricNo, canPublish);

            return Results.Ok(new
            {
                token,
                url = tokens.Url,
                room = tokens.RoomFor(sessionId),
                identity = matricNo,
                name = displayName,
                canPublish,
            });
        });
    }
}
