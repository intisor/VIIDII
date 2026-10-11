using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// The loop a lecturer and a student actually walk.
/// <para>
/// Lecturer: sign up, create a class, upload the list, create a session for that class.
/// Student: open the class link, enter the two details, land in the room.
/// </para>
///
/// <para>
/// Students have no account. They hold a seat claimed against the roster, so every step
/// here runs without a sign in on the student's side.
/// </para>
///
/// <para>
/// The classroom renders over a circuit with prerender disabled, so its HTTP response is
/// only a shell. These tests therefore assert the things that exist server side: the
/// redirect, the seat itself, the roster row, and the rules that decide which seat opens
/// which room.
/// </para>
/// </summary>
public class LecturerStudentLoopTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LecturerStudentLoopTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> CreateApp()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-loop-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });
    }

    private sealed record Fixture(int ClassId, string ClassCode, string LecturerMatric);

    /// <summary>
    /// Redirects are not followed, so each step can be asserted on its own. Cookies are
    /// left on: the lecturer needs the sign in cookie and every form needs the
    /// antiforgery one. The seat is read off the response headers instead, which is the
    /// only place it is visible, because the client's cookie container keeps it private.
    /// </summary>
    private static HttpClient Browser(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static string? SeatCookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        var seat = values.FirstOrDefault(v => v.StartsWith(StudentSeatService.CookieName, StringComparison.Ordinal));

        // Just the name=value pair, without the attributes that follow it.
        return seat?.Split(';')[0];
    }

    private static bool IssuedSeat(HttpResponseMessage response) => SeatCookieValue(response) is not null;

    /// <summary>Decodes the seat a response issued, the same way the app would.</summary>
    private static StudentSeat? DecodeSeat(WebApplicationFactory<Program> app, HttpResponseMessage response)
        => ReadSeatCookie(app, SeatCookieValue(response));

    private static StudentSeat? ReadSeatCookie(WebApplicationFactory<Program> app, string? cookiePair)
    {
        using var scope = app.Services.CreateScope();
        var seatService = scope.ServiceProvider.GetRequiredService<StudentSeatService>();

        return seatService.Read(AsContext(cookiePair));
    }

    private static bool Allows(WebApplicationFactory<Program> app, HttpResponseMessage response, Session session)
    {
        using var scope = app.Services.CreateScope();
        var seatService = scope.ServiceProvider.GetRequiredService<StudentSeatService>();

        return seatService.Allows(AsContext(SeatCookieValue(response)), session);
    }

    private static DefaultHttpContext AsContext(string? cookiePair)
    {
        var context = new DefaultHttpContext();

        context.Request.Headers.Cookie = cookiePair ?? string.Empty;

        return context;
    }

    private static async Task<string> ReadAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");

        Assert.True(match.Success, $"No antiforgery token found on {path}.");

        return match.Groups["token"].Value;
    }

    /// <summary>Registers a lecturer and stands up a class with a three name roster.</summary>
    private static async Task<Fixture> SetUpClassAsync(HttpClient lecturer)
    {
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";
        var classCode = $"C{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";

        var register = await lecturer.PostAsJsonAsync("/api/account/register", new
        {
            name = "Dr. Amaka Obi",
            matricNo,
            password = "a-good-password"
        });

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var created = await lecturer.PostAsJsonAsync("/api/classes", new
        {
            code = classCode,
            name = "100 Level Computer Science"
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var classId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var csv = """
        Matric Number,Full Name
        CSC/2021/001,Ada Nwosu
        CSC/2021/002,Babatunde Cole
        CSC/2021/003,Chioma Okeke
        """;

        var import = await lecturer.PostAsJsonAsync($"/api/roster/classes/{classId}/import", new
        {
            content = csv,
            mapping = new
            {
                identifierHeader = "Matric Number",
                nameHeader = "Full Name",
                identifierPrompt = "Enter your matric number"
            }
        });

        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        return new Fixture(classId, classCode, matricNo);
    }

    private static async Task<Session> StartSessionAsync(WebApplicationFactory<Program> app, Fixture fixture)
    {
        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var session = await sessionService.CreateSession(
            fixture.LecturerMatric,
            "Introduction to Computing",
            [User.Departments.Any],
            [User.Levels.Any],
            fixture.ClassId);

        Assert.NotNull(session);

        return session;
    }

    private static async Task<HttpResponseMessage> PostJoinAsync(
        HttpClient student, string classCode, string identifier, string name)
    {
        var token = await ReadAntiforgeryTokenAsync(student, $"/join/{classCode}");

        return await student.PostAsync($"/join/claim/{classCode}", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("identifier", identifier),
            new KeyValuePair<string, string>("name", name),
        }));
    }

    [Fact]
    public async Task AStudentWalksTheWholePathFromClassLinkToRoom()
    {
        using var app = CreateApp();

        string classCode;

        using (var lecturer = Browser(app))
        {
            var fixture = await SetUpClassAsync(lecturer);
            classCode = fixture.ClassCode;

            await StartSessionAsync(app, fixture);
        }

        // A different browser entirely, with no account and no sign in.
        using var student = Browser(app);

        var page = await student.GetAsync($"/join/{classCode}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("100 Level Computer Science", html);
        Assert.Contains("Enter your matric number", html);

        var join = await PostJoinAsync(student, classCode, "CSC/2021/002", "Babatunde Cole");

        // Straight into the room, with no sign in at any point.
        Assert.Equal(HttpStatusCode.Redirect, join.StatusCode);
        Assert.StartsWith("/session/", join.Headers.Location?.ToString() ?? string.Empty);

        Assert.True(IssuedSeat(join), "The student should be issued a seat when they join.");


        var seat = DecodeSeat(app, join);

        Assert.NotNull(seat);
        Assert.Equal("CSC/2021/002", seat.MatricNo);
        Assert.Equal("Babatunde Cole", seat.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(seat.ClaimToken));
    }

    [Fact]
    public async Task TheSeatOpensTheRoomForTheClassItWasClaimedIn()
    {
        using var app = CreateApp();

        Fixture fixture;
        Session session;

        using (var lecturer = Browser(app))
        {
            fixture = await SetUpClassAsync(lecturer);
            session = await StartSessionAsync(app, fixture);
        }

        using var student = Browser(app);
        var join = await PostJoinAsync(student, fixture.ClassCode, "CSC/2021/002", "Babatunde Cole");

        Assert.Equal(HttpStatusCode.Redirect, join.StatusCode);
        Assert.Equal($"/session/{session.SessionId}", join.Headers.Location!.ToString());




        Assert.True(Allows(app, join, session));

        var room = await student.GetAsync($"/session/{session.SessionId}");
        Assert.Equal(HttpStatusCode.OK, room.StatusCode);
    }

    [Fact]
    public async Task AStudentWithTheWrongNameIsSentBackToTheForm()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);

        using var student = Browser(app);
        var join = await PostJoinAsync(student, fixture.ClassCode, "CSC/2021/003", "Babatunde Cole");

        var location = join.Headers.Location?.ToString() ?? string.Empty;

        Assert.Equal(HttpStatusCode.Redirect, join.StatusCode);
        Assert.Contains($"/join/{fixture.ClassCode}", location);
        Assert.Contains("error=not-found", location);

        // The details they typed come back, so somebody on a phone does not retype them.
        Assert.Contains("CSC%2F2021%2F003", location);

        Assert.False(IssuedSeat(join), "A refused student must not be issued a seat.");
    }

    [Fact]
    public async Task AStudentWhoIsNotOnTheListIsTurnedAway()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);

        using var student = Browser(app);
        var join = await PostJoinAsync(student, fixture.ClassCode, "NOT/ON/LIST", "Somebody Else");

        Assert.Contains("error=not-found", join.Headers.Location?.ToString() ?? string.Empty);
        Assert.False(IssuedSeat(join));
    }

    [Fact]
    public async Task AStudentWhoJoinsBeforeTheSessionStartsIsToldToWait()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);

        // No session started yet.
        using var student = Browser(app);
        var join = await PostJoinAsync(student, fixture.ClassCode, "CSC/2021/001", "Ada Nwosu");

        Assert.Equal(HttpStatusCode.Redirect, join.StatusCode);
        Assert.Contains("waiting=1", join.Headers.Location?.ToString() ?? string.Empty);

        // The seat is still issued, so the refresh that follows can get straight in.
        Assert.True(IssuedSeat(join));


        var waiting = await student.GetAsync(join.Headers.Location!.ToString());
        var html = await waiting.Content.ReadAsStringAsync();

        Assert.Contains("You are on the list", html);
        Assert.Contains("Ada Nwosu", html);
    }

    [Fact]
    public async Task AnUnknownClassCodeShowsAPlainDeadEnd()
    {
        using var app = CreateApp();
        using var student = Browser(app);

        var page = await student.GetAsync("/join/NOSUCH");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("did not work", html);
    }

    [Fact]
    public async Task SomebodyWithNoSeatHoldsNothing()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);
        var session = await StartSessionAsync(app, fixture);

        // Somebody who never went through the join link.
        using var stranger = Browser(app);
        var response = await stranger.GetAsync($"/session/{session.SessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(ReadSeatCookie(app, null));
        Assert.False(Allows(app, response, session));
    }

    [Fact]
    public async Task ASeatDoesNotOpenASessionForADifferentClass()
    {
        using var app = CreateApp();

        using var lecturer = Browser(app);
        var first = await SetUpClassAsync(lecturer);
        var session = await StartSessionAsync(app, first);

        // A second class, and a seat in it.
        using var lecturerTwo = Browser(app);
        var second = await SetUpClassAsync(lecturerTwo);

        using var student = Browser(app);
        var join = await PostJoinAsync(student, second.ClassCode, "CSC/2021/002", "Babatunde Cole");

        Assert.True(IssuedSeat(join));

        var seat = DecodeSeat(app, join);
        Assert.NotNull(seat);
        Assert.Equal(second.ClassId, seat.ClassId);
        Assert.NotEqual(first.ClassId, seat.ClassId);

        // The seat is real, it just does not open this room.
        Assert.False(
            Allows(app, join, session),
            "A seat in one class must not open another class's session.");
    }

    [Fact]
    public async Task ASessionCannotBeCreatedForSomebodyElsesClass()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);

        var otherMatric = $"OTH{Guid.NewGuid().ToString("N")[..8]}";
        using var other = Browser(app);

        Assert.Equal(
            HttpStatusCode.OK,
            (await other.PostAsJsonAsync("/api/account/register", new
            {
                name = "Dr. Someone Else",
                matricNo = otherMatric,
                password = "a-good-password"
            })).StatusCode);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var session = await sessionService.CreateSession(
            otherMatric, "Not my class", [User.Departments.Any], [User.Levels.Any], fixture.ClassId);

        Assert.Null(session);
    }

    [Fact]
    public async Task ASessionNeedsAClass()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        // A session with no class has no audience: no student could ever be routed to it.
        Assert.Null(await sessionService.CreateSession(
            fixture.LecturerMatric, "Orphan", [User.Departments.Any], [User.Levels.Any]));

        Assert.NotNull(await sessionService.CreateSession(
            fixture.LecturerMatric, "Attached", [User.Departments.Any], [User.Levels.Any], fixture.ClassId));
    }

    [Fact]
    public async Task AStudentCanOnlyFindASessionForTheirOwnClass()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);
        await StartSessionAsync(app, fixture);

        using (var lecturerTwo = Browser(app))
        {
            var other = await SetUpClassAsync(lecturerTwo);

            using var scope = app.Services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

            Assert.NotEmpty(await sessionService.GetLiveSessionsForClassAsync(fixture.ClassId));
            Assert.Empty(await sessionService.GetLiveSessionsForClassAsync(other.ClassId));
        }
    }

    [Theory]
    [InlineData("forged-payload-not-signed")]
    [InlineData("")]
    [InlineData("YWJjZGVmZ2g")]
    public async Task ATamperedSeatCookieIsRefused(string forged)
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);
        var session = await StartSessionAsync(app, fixture);

        using var scope = app.Services.CreateScope();
        var seatService = scope.ServiceProvider.GetRequiredService<StudentSeatService>();

        // A student editing their own seat to claim a different class. The payload is
        // protected, so all of these must be treated as no seat at all.
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{StudentSeatService.CookieName}={forged}";

        Assert.Null(seatService.Read(context));
        Assert.False(seatService.Allows(context, session));
    }

    [Fact]
    public async Task TheStudentIsRecordedOnTheRosterRow()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);
        await StartSessionAsync(app, fixture);

        using var student = Browser(app);
        await PostJoinAsync(student, fixture.ClassCode, "CSC/2021/002", "Babatunde Cole");

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ViidiiDbContext>();

        var entry = await context.RosterEntries.SingleAsync(e => e.Identifier == "CSC/2021/002");

        Assert.True(entry.HasClaimed);
        Assert.NotNull(entry.ClaimToken);
        Assert.NotNull(entry.ClaimedAtUtc);
    }

    [Fact]
    public async Task AStudentWithNoAccountCanStillTakePart()
    {
        using var app = CreateApp();
        using var lecturer = Browser(app);

        var fixture = await SetUpClassAsync(lecturer);
        var session = await StartSessionAsync(app, fixture);

        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        // Students are on the roster and nowhere else, so the join must not demand a
        // user record.
        var (joined, error) = await sessionService.JoinSession(session.SessionId, "CSC/2021/002", null);

        Assert.NotNull(joined);
        Assert.Null(error);
        Assert.Contains("CSC/2021/002", joined.ParticipantIds);
    }
}