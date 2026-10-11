using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VIIDII.Models;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Joining a session must never take the circuit down with it.
/// <para>
/// A student holds a seat rather than an account, so their identity is whatever
/// <c>StudentSeatService</c> can read off the connection. That read comes back empty
/// whenever the seat cookie does not ride along on the websocket handshake, which is
/// ordinary rather than exceptional.
/// </para>
/// <para>
/// It used to come back null, flow into SessionService.JoinSession as the participant
/// key, and throw ArgumentNullException from Dictionary.set_Item. The lecturer saw the
/// connection drop.
/// </para>
/// </summary>
public class AnonymousJoinTests
{
    private static WebApplicationFactory<Program> CreateApp() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-anon-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });

    private static async Task<Session> LiveSessionAsync(WebApplicationFactory<Program> app)
    {
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Dr. Amaka Obi",
            matricNo,
            password = "a-good-password"
        });

        var created = await client.PostAsJsonAsync("/api/classes",
            new { code = $"A{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}", name = "Class list" });

        var classId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var session = (await sessions.CreateSession(
            matricNo,
            "Introduction to Computing",
            [User.Departments.Any],
            [User.Levels.Any],
            classId))!;

        await sessions.StartSessionAsync(session.SessionId);

        return session;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task JoiningWithNoIdentityIsRefusedRatherThanThrowing(string? participantId)
    {
        using var app = CreateApp();
        var session = await LiveSessionAsync(app);

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var (joined, error) = await sessions.JoinSession(session.SessionId, participantId!, "conn-1");

        Assert.Null(joined);
        Assert.NotNull(error);
    }

    /// <summary>
    /// A refused join must leave the session exactly as it was, not half-populated with
    /// a null participant.
    /// </summary>
    [Fact]
    public async Task ARefusedJoinLeavesTheSessionUntouched()
    {
        using var app = CreateApp();
        var session = await LiveSessionAsync(app);

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var before = sessions.GetSessionById(session.SessionId);
        var participantsBefore = before!.ParticipantIds.Count;
        var statusesBefore = before.ParticipantStatuses.Count;

        await sessions.JoinSession(session.SessionId, null!, "conn-1");

        var after = sessions.GetSessionById(session.SessionId);

        Assert.Equal(participantsBefore, after!.ParticipantIds.Count);
        Assert.Equal(statusesBefore, after.ParticipantStatuses.Count);
        Assert.DoesNotContain(null!, after.ParticipantIds.Cast<string?>());
    }

    /// <summary>
    /// The real student path still works, so the guard has not become a wall.
    /// </summary>
    [Fact]
    public async Task AStudentWithASeatStillJoins()
    {
        using var app = CreateApp();
        var session = await LiveSessionAsync(app);

        // Unique per test. SessionService._sessions is static and shared across the
        // whole test process, and joining refuses anyone already active in a
        // different session, so a fixed identifier collides with whichever other test
        // used it first.
        var identifier = $"CSC/{Guid.NewGuid():N}";

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var (joined, error) = await sessions.JoinSession(session.SessionId, identifier, "conn-1");

        Assert.Null(error);
        Assert.NotNull(joined);
        Assert.Contains(identifier, joined!.ParticipantIds);
    }
}