using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VIIDII.Models;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Creating a session, driven through the real dependency injection container.
///
/// <para>
/// Session creation used to validate the lecturer against a hardcoded list of demo
/// users. Nobody who had signed up appeared in it, so every self-service account was
/// refused and the screen said only "Failed to create session. Please try again."
/// </para>
///
/// <para>
/// It also kept live sessions in a dictionary that belonged to the injected instance,
/// while the service was registered as scoped. Blazor Server builds a scope per
/// circuit, so a session existed only inside the circuit that created it, and every
/// student who connected afterwards was told the session did not exist.
/// </para>
/// </summary>
public class SessionCreationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SessionCreationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> CreateApp()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-session-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });
    }

    private static async Task<string> RegisterLecturerAsync(HttpClient client, string name)
    {
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync("/api/account/register", new
        {
            name,
            matricNo,
            password = "a-good-password"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return matricNo;
    }

    /// <summary>Registers a lecturer and gives them a class, since a session needs one.</summary>
    private static async Task<(string MatricNo, int ClassId)> RegisterWithClassAsync(HttpClient client)
    {
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/account/register", new
            {
                name = "Dr. Amaka Obi",
                matricNo,
                password = "a-good-password"
            })).StatusCode);

        var created = await client.PostAsJsonAsync("/api/classes", new
        {
            code = $"C{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}",
            name = "100 Level Computer Science"
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var classId = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetInt32();

        return (matricNo, classId);
    }
    [Fact]
    public async Task ALecturerWhoSignedUpCanCreateASession()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var (matricNo, classId) = await RegisterWithClassAsync(client);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var session = await sessionService.CreateSession(
            matricNo,
            "Introduction to Computing",
            [User.Departments.Any],
            [User.Levels.Any],
            classId);

        Assert.NotNull(session);
        Assert.Equal("Introduction to Computing", session.Title);
        Assert.Equal(matricNo, session.LecturerMatricNo);
        Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
    }

    [Fact]
    public async Task ANonLecturerCannotCreateASession()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var (matricNo, classId) = await RegisterWithClassAsync(client);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        // A student seat is claimed by identifier, never by account, so there is no
        // student account to try here. An unknown number must still be refused.
        var session = await sessionService.CreateSession(
            "NOBODY123",
            "Introduction to Computing",
            [User.Departments.Any],
            [User.Levels.Any],
            classId);

        Assert.Null(session);
    }

    [Fact]
    public async Task ALecturerIsRecognisedFromTheDatabaseNotAHardcodedList()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var (matricNo, classId) = await RegisterWithClassAsync(client);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        Assert.True(await sessionService.IsLecturerAsync(matricNo));
        Assert.False(await sessionService.IsLecturerAsync("NOBODY123"));
        Assert.False(await sessionService.IsLecturerAsync(""));
    }

    [Fact]
    public async Task ASessionSurvivesMovingToADifferentScope()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var (matricNo, classId) = await RegisterWithClassAsync(client);

        string sessionId;

        // One scope creates the session, exactly as the lecturer's circuit would.
        using (var creatorScope = app.Services.CreateScope())
        {
            var sessionService = creatorScope.ServiceProvider.GetRequiredService<SessionService>();

            var session = await sessionService.CreateSession(
                matricNo,
                "Introduction to Computing",
                [User.Departments.Any],
                [User.Levels.Any],
                classId);

            Assert.NotNull(session);
            sessionId = session.SessionId;
        }

        // A separate scope stands in for a student's connection. With the old
        // per-instance dictionary this found nothing and the student was told the
        // session did not exist.
        using (var readerScope = app.Services.CreateScope())
        {
            var sessionService = readerScope.ServiceProvider.GetRequiredService<SessionService>();

            var found = await sessionService.GetSessionByIdAsync(sessionId);

            Assert.NotNull(found);
            Assert.Equal("Introduction to Computing", found.Title);
        }
    }

    [Fact]
    public async Task AStudentWithNoAccountCanStillJoin()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var (matricNo, classId) = await RegisterWithClassAsync(client);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var session = await sessionService.CreateSession(
            matricNo,
            "Introduction to Computing",
            [User.Departments.Any],
            [User.Levels.Any],
            classId);

        Assert.NotNull(session);

        // Students have no account. They are on the roster and nothing else, so the
        // join must not demand a user record.
        var (joined, error) = await sessionService.JoinSession(session.SessionId, "CSC/2021/001", null);

        Assert.NotNull(joined);
        Assert.Null(error);
    }

    [Fact]
    public async Task CreatingTheSameSessionTwiceReturnsTheFirstOne()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var (matricNo, classId) = await RegisterWithClassAsync(client);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var first = await sessionService.CreateSession(matricNo, "Lecture 1", [User.Departments.Any], [User.Levels.Any], classId);
        var second = await sessionService.CreateSession(matricNo, "Lecture 2", [User.Departments.Any], [User.Levels.Any], classId);

        Assert.NotNull(first);
        Assert.NotNull(second);

        // A lecturer running a class already in progress should not silently end up
        // with two live sessions.
        Assert.Equal(first.SessionId, second.SessionId);
    }
}