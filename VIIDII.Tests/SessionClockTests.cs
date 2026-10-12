using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Session timing, against a clock the test controls.
///
/// None of this was writable before TimeProvider was injected. Every deadline in
/// SessionService read DateTime.UtcNow, so testing "a session expires after 24 hours"
/// meant either a 24-hour sleep or an assertion that something was "roughly" expired.
/// </summary>
public class SessionClockTests
{
    /// <summary>
    /// The clock starts at the real current instant rather than a fixed date in the past.
    /// <para>
    /// Registration signs the lecturer in with a persistent cookie, and the cookie
    /// container judges expiry against the machine clock. Point this at a past date and
    /// the cookie comes back already expired, so every later call is 401 and the failure
    /// reads as an auth bug rather than a clock bug. Absolute instants are therefore
    /// captured from the clock at the moment they matter, and assertions are made
    /// relative to those, which is what these tests are about anyway.
    /// </para>
    /// </summary>
    private static FakeTimeProvider ClockStartingNow() => new(DateTimeOffset.UtcNow);

    private static WebApplicationFactory<Program> CreateApp(FakeTimeProvider clock) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-clock-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock));
        });

    private static HttpClient Browser(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<(int ClassId, string LecturerMatric, string StudentId)> SetUpClassAsync(
        HttpClient lecturer)
    {
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";

        var register = await lecturer.PostAsJsonAsync("/api/account/register", new
        {
            name = "Dr. Amaka Obi",
            matricNo,
            password = "a-good-password"
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var created = await lecturer.PostAsJsonAsync("/api/classes", new
        {
            code = $"C{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}",
            name = "100 Level Computer Science"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var classId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Unique, and on the list. Both matter:
        //
        // Unique, because SessionService._sessions is static and shared across the
        // whole test process, and joining refuses anyone already active in a different
        // session. A fixed identifier collides with whichever other test used it first,
        // which made this file pass alone and fail in the full run.
        //
        // On the list, because attendance is only written for a student the roster
        // knows: importing creates the user row that LogAttendanceStatusAsync looks up.
        // An identifier that is unique but absent from the class list joins fine and
        // records nothing.
        var studentId = $"CSC/{Guid.NewGuid():N}";

        var csv = $"""
        Matric Number,Full Name
        {studentId},Babatunde Cole
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

        return (classId, matricNo, studentId);
    }

    /// <summary>Registers a lecturer, stands up a class, starts a session, joins a student.</summary>
    private static async Task<(Session Session, string StudentId)> StageLiveSessionAsync(
        WebApplicationFactory<Program> app)
    {
        int classId;
        string lecturerMatric;
        string studentId;
        using (var lecturer = Browser(app))
        {
            (classId, lecturerMatric, studentId) = await SetUpClassAsync(lecturer);
        }

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var session = (await sessions.CreateSession(
            lecturerMatric,
            "Introduction to Computing",
            [User.Departments.Any],
            [User.Levels.Any],
            classId))!;

        await sessions.StartSessionAsync(session.SessionId);

        var (joined, error) = await sessions.JoinSession(session.SessionId, studentId, "conn-1");
        Assert.NotNull(joined);
        Assert.Null(error);

        return (session, studentId);
    }

    /// <summary>
    /// The regression that motivated all of this. Every timestamp in SessionService was
    /// written as DateTime.UtcNow.AddHours(1), but session length was computed from an
    /// unshifted DateTime.UtcNow. The difference came out an hour short, so any session
    /// younger than that returned an empty attendance dictionary and the lecturer saw a
    /// blank recap after an ordinary lecture.
    /// </summary>
    [Fact]
    public async Task ASessionShorterThanAnHourStillProducesAttendance()
    {
        var clock = ClockStartingNow();
        using var app = CreateApp(clock);

        var (session, studentId) = await StageLiveSessionAsync(app);

        // A 20 minute lecture, then read the attendance back.
        clock.Advance(TimeSpan.FromMinutes(20));

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        var scores = await sessions.CalculateAttendanceScoreFromPersistenceAsync(session.SessionId);

        Assert.True(
            scores.ContainsKey(studentId),
            "A 20 minute session produced no attendance at all. Session length is being "
            + "computed an hour short, so anything under an hour reads as zero length.");
    }

    /// <summary>
    /// The lecturer's attendance has to line up with the roster's own timestamps. Those
    /// come from the database CURRENT_TIMESTAMP default and are true UTC; the session
    /// timestamps were being written an hour ahead of them.
    /// </summary>
    [Fact]
    public async Task AttendanceTimestampsAgreeWithTheRosterOnes()
    {
        var clock = ClockStartingNow();
        using var app = CreateApp(clock);

        var (session, studentId) = await StageLiveSessionAsync(app);
        var stampedAt = clock.GetUtcNow().UtcDateTime;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var scope = app.Services.CreateScope();
            if (scope.ServiceProvider.GetRequiredService<ViidiiDbContext>().AttendanceLogs.Any())
            {
                break;
            }

            await Task.Delay(20);
        }

        using var readScope = app.Services.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<ViidiiDbContext>();

        var log = await context.AttendanceLogs
            .Include(l => l.Student)
            .FirstAsync(l => l.Student != null && l.Student.MatricNo == studentId);

        // Timestamps read back from SQLite arrive with Kind=Unspecified, and
        // ToUniversalTime() on an Unspecified value treats it as local time and shifts
        // it. On a machine at UTC+1 that turns a correct timestamp into an apparent
        // one-hour error. These values are already UTC by construction, so compare them
        // directly. Production never calls ToUniversalTime for the same reason.
        Assert.True(
            Math.Abs((log.Timestamp - stampedAt).TotalMinutes) < 5,
            $"Attendance was stamped {log.Timestamp:O}, which is not close to the controlled "
            + $"clock reading {stampedAt:O}. It is offset, not merely noisy.");

        var stored = await context.Sessions.AsNoTracking().FirstAsync(s => s.SessionId == session.SessionId);
        Assert.True(
            Math.Abs((stored.StartTime - stampedAt).TotalMinutes) < 5,
            $"The session start was stamped {stored.StartTime:O}, which is not close to {stampedAt:O}.");

        // And the roster's own clock, which comes from the database default, must agree.
        var entry = await context.RosterEntries.AsNoTracking().FirstAsync();
        Assert.True(
            Math.Abs((entry.ImportedAtUtc - log.Timestamp).TotalMinutes) < 10,
            $"The roster was imported at {entry.ImportedAtUtc:O} but the attendance it produced "
            + $"was stamped {log.Timestamp:O}. Those are supposed to be the same clock.");
    }

    /// <summary>
    /// Expiry is now testable without waiting a day. A session stops being live the
    /// moment its 24 hour window closes, and only then.
    /// </summary>
    [Fact]
    public async Task ASessionIsLiveUntilItsWindowClosesAndNotAfter()
    {
        var clock = ClockStartingNow();
        using var app = CreateApp(clock);

        int classId;
        string lecturerMatric;
        using (var lecturer = Browser(app))
        {
            (classId, lecturerMatric, _) = await SetUpClassAsync(lecturer);
        }

        string sessionId;
        using (var scope = app.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            var session = (await sessions.CreateSession(
                lecturerMatric,
                "Introduction to Computing",
                [User.Departments.Any],
                [User.Levels.Any],
                classId))!;

            sessionId = session.SessionId;

            // Start it. This is the step that made the session invisible to students:
            // CreateSession leaves it Active, starting flips it to Started, and
            // GetLiveSessionsForClassAsync matched on Active alone. The waiting page
            // said "nothing is running" for the whole lecture.
            await sessions.StartSessionAsync(session.SessionId);
        }

        clock.Advance(TimeSpan.FromHours(23));
        using (var scope = app.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            var live = await sessions.GetLiveSessionsForClassAsync(classId);
            Assert.Contains(live, s => s.SessionId == sessionId);
        }

        clock.Advance(TimeSpan.FromHours(2));
        using (var scope = app.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            var live = await sessions.GetLiveSessionsForClassAsync(classId);
            Assert.DoesNotContain(live, s => s.SessionId == sessionId);
        }
    }
}