using System.Security.Cryptography;
using System.Text;

namespace VIIDII.Services;

/// <summary>
/// Options for a self-hosted TURN server, bound from the "TurnServers" config section.
/// </summary>
public class TurnServerOptions
{
    /// <summary>Primary TURN URL, e.g. "turn:turn.example.com:443". Used as the shared-secret issuer.</summary>
    public string? Url { get; set; }

    /// <summary>All URLs the browser should try, including a turns: entry for TLS.</summary>
    public List<string> Urls { get; set; } = new();

    /// <summary>Static long-term credential. When set, no HMAC credentials are issued.</summary>
    public string? Username { get; set; }

    /// <summary>Static long-term credential. When set, no HMAC credentials are issued.</summary>
    public string? Credential { get; set; }

    /// <summary>How long an issued HMAC credential remains valid.</summary>
    public int CredentialTtlHours { get; set; } = 12;
}

/// <summary>
/// Response shape consumed by wwwroot/js/sessionInterop.js.
/// </summary>
public class IceServerDto
{
    public List<string> Urls { get; set; } = new();
    public string? Username { get; set; }
    public string? Credential { get; set; }
}

/// <summary>
/// Generates time-limited TURN credentials using the TURN REST API scheme
/// (ephemeral username = expiry timestamp, credential = base64(HMAC-SHA1)).
///
/// This is the mechanism coturn expects when configured with
/// use-auth-secret / static-auth-secret. Issuing per-session credentials avoids
/// shipping a long-lived username/password pair to every browser.
/// </summary>
public static class TurnCredentialGenerator
{
    public static (string Username, string Credential) Generate(string sharedSecret, TimeSpan ttl)
    {
        // Username is the UNIX expiry time. cotturn parses it and rejects expired values.
        var expiry = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var username = expiry.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(username));
        var credential = Convert.ToBase64String(signature);

        return (username, credential);
    }
}