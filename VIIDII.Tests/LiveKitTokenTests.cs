using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// The token that admits a browser to the media room.
/// <para>
/// This is the join ticket for video, so two things have to be true of it. It must
/// actually verify - a token the media server would reject is worse than no token,
/// because the failure lands on a student's phone mid-lecture. And it must not be
/// obtainable by anyone who has not already proved who they are, because a token is
/// the only thing standing between the public internet and someone's lecture.
/// </para>
/// <para>
/// The signature is checked by re-deriving it from the secret, which is the same
/// operation the media server performs. A test that only decoded the token would
/// pass even if the signing were wrong.
/// </para>
/// <para>
/// The payload is decoded by hand rather than through a JWT library, so these tests
/// assert on exactly the bytes the service emits. A library round-trip can normalise
/// a claim and hide a mistake in what actually goes on the wire.
/// </para>
/// </summary>
public class LiveKitTokenTests
{
    private const string Secret = "test-api-secret-that-is-not-real";
    private const string ApiKey = "APItestkey";

    private static LiveKitTokenService Service(bool configured = true, TimeProvider? clock = null) =>
        new(
            new LiveKitOptions
            {
                Url = configured ? "wss://project-test.livekit.cloud" : null,
                ApiKey = configured ? ApiKey : null,
                ApiSecret = configured ? Secret : null,
            },
            clock ?? TimeProvider.System);

    /// <summary>Re-derives the signature from the secret, exactly as the media server would.</summary>
    private static bool SignatureIsValid(string token, string secret)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));

        var actual = parts[2].Replace('-', '+').Replace('_', '/');
        var padding = actual.Length % 4;
        if (padding != 0)
        {
            actual = actual.PadRight(actual.Length + (4 - padding), '=');
        }

        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(actual));
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        var padding = padded.Length % 4;
        if (padding != 0)
        {
            padded = padded.PadRight(padded.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(padded);
    }

    private static JsonElement Payload(string token) =>
        JsonDocument.Parse(FromBase64Url(token.Split('.')[1])).RootElement.Clone();

    private static JsonElement VideoClaim(string token) => Payload(token).GetProperty("video");

    [Fact]
    public void UnconfiguredServiceRefusesRatherThanIssuingAnUnusableToken()
    {
        var (token, error) = Service(configured: false).Issue("S1", "LEC/1", "Lecturer", true);

        Assert.Null(token);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TokenIsSignedWithTheApiSecret()
    {
        var (token, _) = Service().Issue("S1", "LEC/1", "Lecturer", true);

        Assert.NotNull(token);
        Assert.True(SignatureIsValid(token!, Secret),
            "the media server recomputes this signature, so a token that does not match is dead on arrival");
    }

    [Fact]
    public void TokenIsRejectedWhenTheSecretDiffers()
    {
        var (token, _) = Service().Issue("S1", "LEC/1", "Lecturer", true);

        Assert.False(SignatureIsValid(token!, "a-different-secret"),
            "a token must not verify under any secret but the real one");
    }

    [Fact]
    public void TokenCarriesTheParticipantIdentityAsTheSubject()
    {
        var (token, _) = Service().Issue("S1", "CSC/2021/103", "Ada Okoro", false);

        var payload = Payload(token!);

        Assert.Equal("CSC/2021/103", payload.GetProperty("sub").GetString());
        Assert.Equal(ApiKey, payload.GetProperty("iss").GetString());
    }

    [Fact]
    public void RoomIsScopedToTheSessionSoTokensCannotCrossRooms()
    {
        var (first, _) = Service().Issue("20261013-AAAAA", "LEC/1", "Lecturer", true);
        var (second, _) = Service().Issue("20261013-BBBBB", "LEC/1", "Lecturer", true);

        var firstRoom = VideoClaim(first!).GetProperty("room").GetString();
        var secondRoom = VideoClaim(second!).GetProperty("room").GetString();

        Assert.Equal("session-20261013-AAAAA", firstRoom);
        Assert.Equal("session-20261013-BBBBB", secondRoom);
        Assert.NotEqual(firstRoom, secondRoom);
    }

    [Fact]
    public void LecturerMayPublishAndStudentMayNot()
    {
        var (lecturer, _) = Service().Issue("S1", "LEC/1", "Lecturer", canPublish: true);
        var (student, _) = Service().Issue("S1", "CSC/2021/103", "Ada Okoro", canPublish: false);

        Assert.True(VideoClaim(lecturer!).GetProperty("canPublish").GetBoolean());
        Assert.False(VideoClaim(student!).GetProperty("canPublish").GetBoolean());
    }

    [Fact]
    public void BothRolesMaySubscribe()
    {
        var (lecturer, _) = Service().Issue("S1", "LEC/1", "Lecturer", true);
        var (student, _) = Service().Issue("S1", "CSC/1", "Ada", false);

        Assert.True(VideoClaim(lecturer!).GetProperty("canSubscribe").GetBoolean());
        Assert.True(VideoClaim(student!).GetProperty("canSubscribe").GetBoolean());
    }

    [Fact]
    public void HeaderDeclaresHmacSha256SoTheMediaServerKnowsHowToVerify()
    {
        var (token, _) = Service().Issue("S1", "LEC/1", "Lecturer", true);

        using var header = JsonDocument.Parse(FromBase64Url(token!.Split('.')[0]));

        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
    }

    [Fact]
    public void TokenExpiresRatherThanBeingValidForever()
    {
        var now = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        var (token, _) = Service(clock: new FakeTimeProvider(now)).Issue("S1", "LEC/1", "Lecturer", true);

        var payload = Payload(token!);

        Assert.Equal(now.ToUnixTimeSeconds() + LiveKitTokenService.TokenLifetimeSeconds,
            payload.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void TokenIsNotValidBeforeItIsIssued()
    {
        var now = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        var (token, _) = Service(clock: new FakeTimeProvider(now)).Issue("S1", "LEC/1", "Lecturer", true);

        Assert.Equal(now.ToUnixTimeSeconds(), Payload(token!).GetProperty("nbf").GetInt64());
    }

    [Fact]
    public void EveryTokenIsDistinctSoOneCannotBeReplayedAsAnother()
    {
        var service = Service();

        var (first, _) = service.Issue("S1", "LEC/1", "Lecturer", true);
        var (second, _) = service.Issue("S1", "LEC/1", "Lecturer", true);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void EndpointRefusesWhenVideoIsNotConfigured()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(new LiveKitOptions());
                services.AddSingleton<LiveKitTokenService>();
            }));

        using var response = app.CreateClient()
            .PostAsync("/api/livekit/token?sessionId=anything", null)
            .GetAwaiter()
            .GetResult();

        Assert.True(response.StatusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.ServiceUnavailable,
            $"got {(int)response.StatusCode}");
    }
}
