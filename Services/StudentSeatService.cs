using Microsoft.AspNetCore.DataProtection;
using VIIDII.Models;

namespace VIIDII.Services;

/// <summary>
/// What a student proved when they joined a class.
/// </summary>
/// <param name="MatricNo">The identifier they entered, matched against the roster.</param>
/// <param name="DisplayName">The name the roster holds for them.</param>
/// <param name="ClassId">The class they claimed a seat in. A seat is valid only for sessions of this class.</param>
/// <param name="ClaimToken">The one-time token written against the roster row when the seat was claimed.</param>
public sealed record StudentSeat(
    string MatricNo,
    string DisplayName,
    int ClassId,
    string ClaimToken);

/// <summary>
/// Carries a student's claimed seat between requests without giving them an account.
/// <para>
/// Students have no user row, no password and no login. Once they have matched the
/// class list, something has to stand in for a session cookie so the classroom page
/// and the hub know who they are on later requests. This is that something.
/// </para>
///
/// <para>
/// The payload is protected, not merely encoded. Anyone can read a base64 blob; they
/// cannot forge one without the application's data protection keys. That also stops a
/// student editing their payload to claim a seat in a different class.
/// </para>
///
/// <para>
/// What this proves is that "someone who knew these details was on the list". It does
/// not prove which person that was. That limit is the same one the join endpoints
/// already carry, and it is adequate for participation but not for accreditation.
/// </para>
/// </summary>
public class StudentSeatService
{
    public const string CookieName = ".VIIDII.Seat";

    /// <summary>
    /// Field separator. The ASCII unit separator cannot occur in a name or a
    /// matric number, so the payload cannot be shifted by injecting one.
    /// </summary>
    private const char Separator = (char)31;

    /// <summary>Long enough for a lecture with a tea break, short enough to matter little on a shared device.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly IDataProtector _protector;

    public StudentSeatService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("VIIDII.StudentSeat.v1");
    }

    /// <summary>
    /// Issues the seat cookie. Path is the whole site because the classroom page and
    /// the SignalR handshake are both outside any narrower scope.
    /// </summary>
    public void Issue(HttpContext http, StudentSeat seat)
    {
        var payload = _protector.Protect(string.Join(
            Separator.ToString(),
            seat.MatricNo,
            seat.DisplayName,
            seat.ClassId.ToString(),
            seat.ClaimToken,
            DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds().ToString()));

        http.Response.Cookies.Append(CookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/"
        });
    }

    public void Clear(HttpContext http)
    {
        http.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Path = "/"
        });
    }

    /// <summary>
    /// Reads the seat, or null when there is none or it cannot be trusted. A tampered,
    /// truncated or malformed cookie must never throw: that is the untrusted case, and
    /// it simply means "no seat".
    /// </summary>
    public StudentSeat? Read(HttpContext? http)
    {
        if (http is null)
        {
            return null;
        }

        var raw = http.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string unprotected;
        try
        {
            unprotected = _protector.Unprotect(raw);
        }
        catch
        {
            return null;
        }

        var parts = unprotected.Split(Separator);
        if (parts.Length != 5)
        {
            return null;
        }

        if (!long.TryParse(parts[4], out var expiresUnix)
            || DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        if (!int.TryParse(parts[2], out var classId) || classId <= 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        return new StudentSeat(parts[0], parts[1], classId, parts[3]);
    }

    /// <summary>
    /// Whether a seat may open a particular session.
    /// <para>
    /// A seat is claimed against one class, so it only opens that class's sessions.
    /// Without this check a student who joined CS100 could walk into any session whose
    /// link reached them, which is how attendance gets quietly falsified.
    /// </para>
    /// <para>
    /// Signed in staff do not hold a seat and are handled before this is reached.
    /// </para>
    /// </summary>
    public bool Allows(HttpContext? http, Session session)
    {
        var seat = Read(http);

        return seat is not null
            && session.ClassId is not null
            && session.ClassId == seat.ClassId;
    }
}