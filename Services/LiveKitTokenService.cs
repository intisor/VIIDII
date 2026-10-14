using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VIIDII.Services;

/// <summary>
/// Settings for the LiveKit media server, bound from the "LiveKit" config section.
/// Values live outside the repository; see scripts for where they are read from.
/// </summary>
public class LiveKitOptions
{
    /// <summary>WebSocket endpoint of the media server, e.g. "wss://project-xxx.livekit.cloud".</summary>
    public string? Url { get; set; }

    /// <summary>Public API key. Safe to hand to the browser.</summary>
    public string? ApiKey { get; set; }

    /// <summary>API secret. Signs join tokens. Must never reach the browser or the repository.</summary>
    public string? ApiSecret { get; set; }

    /// <summary>Prefix applied to the VIIDII session id to form the media room name.</summary>
    public string RoomPrefix { get; set; } = "session-";
}

/// <summary>
/// Issues the short-lived tokens a participant needs to join a LiveKit room.
/// <para>
/// A LiveKit access token is a plain HS256 JWT: the client is handed one, the media
/// server verifies it, and nothing else grants entry. That is the whole reason the
/// token has to be minted here rather than in the browser.
/// </para>
/// <para>
/// Written by hand rather than via the community .NET server SDK. The SDK is
/// "community" support only, and this is a standard signed JWT - which means the
/// implementation is small enough to read end to end and to test directly, which
/// matters more here than convenience. The authority check is still entirely
/// server-side: this service is only ever called after the caller has been
/// verified against the session owner or the class roster.
/// </para>
/// </summary>
public class LiveKitTokenService
{
    /// <summary>How long an issued token stays valid. A lecture is short; the token is not a session.</summary>
    public const int TokenLifetimeSeconds = 60 * 60;

    private readonly LiveKitOptions _options;
    private readonly TimeProvider _timeProvider;

    public LiveKitTokenService(LiveKitOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>True when all three required values are present, so the app can tell "not set up" from "broken".</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Url)
        && !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.ApiSecret);

    /// <summary>The media server address the browser connects to.</summary>
    public string Url => _options.Url ?? string.Empty;

    /// <summary>The media room name for a VIIDII session.</summary>
    public string RoomFor(string sessionId) => _options.RoomPrefix + sessionId;

    /// <summary>
    /// Mints a token granting <paramref name="identity"/> access to one room.
    /// </summary>
    /// <param name="canPublish">
    /// Whether the holder may send media. True for the lecturer, false for students:
    /// a student joining a lecture is a viewer, and saying so in the token means the
    /// media server refuses a publish attempt rather than the client politely asking
    /// for permission it was never given.
    /// </param>
    public (string? Token, string? Error) Issue(
        string sessionId,
        string identity,
        string displayName,
        bool canPublish)
    {
        if (!IsConfigured)
        {
            return (null, "Video is not configured on this server.");
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        var video = new Dictionary<string, object>
        {
            ["room"] = RoomFor(sessionId),
            ["roomJoin"] = true,
            ["canPublish"] = canPublish,
            ["canSubscribe"] = true,
            // The lecturer may need to create the room before anyone else arrives.
            ["roomCreate"] = canPublish,
        };

        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT",
        };

        var payload = new Dictionary<string, object>
        {
            ["iss"] = _options.ApiKey!,
            ["sub"] = identity,
            ["name"] = displayName,
            ["video"] = video,
            ["nbf"] = now,
            ["exp"] = now + TokenLifetimeSeconds,
            ["jti"] = Guid.NewGuid().ToString("N"),
        };

        var signingInput =
            Base64Url(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));

        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.ApiSecret!),
            Encoding.ASCII.GetBytes(signingInput));

        return (signingInput + "." + Base64Url(signature), null);
    }

    /// <summary>Standard base64url: no padding, and the two URL-safe substitutions.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
