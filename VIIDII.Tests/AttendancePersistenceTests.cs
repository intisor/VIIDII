using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Attendance that only exists in memory is not attendance.
///
/// The existing loop tests assert that a student can join. They do not assert that
/// anything about that join survives the process, which is the part the product is
/// actually selling: a lecturer wants a record they can act on tomorrow.
///
/// These tests join a student who has no account, exactly as the real flow does, then
/// read the database back.
/// </summary>
public class AttendancePersistenceTests
{
    private sealed record Fixture(int ClassId, string ClassCode, string LecturerMatric);

    /// <summary>
    /// One factory per test, because the connection string names the in-memory
    /// database and two factories would otherwise mean two databases. The class has to
    /// be created on the same app the student joins.
    /// </summary>
    private WebApplicationFactory<Program> CreateApp() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-att-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });

    private static HttpClient Browser(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    /// <summary>Registers a lecturer and stands up a class with a three name roster.</summary>
    private static async Task<Fixture> SetUpClassAsync(HttpClient lecturer)
    {
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";
        var classCode = $"A{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";

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

    /// <summary>Creates the session and starts it, so a joining student is late to it.</summary>
    private static async Task<Session> StartStartedSessionAsync(
        WebApplicationFactory<Program> app, Fixture fixture)
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

        var started = await sessionService.StartSessionAsync(session.SessionId);
        Assert.NotNull(started);

        return started;
    }

    private static async Task JoinAsync(WebApplicationFactory<Program> app, Session session, string identifier)
    {
        using var scope = app.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var (joined, error) = await sessionService.JoinSession(session.SessionId, identifier, "conn-1");

        Assert.NotNull(joined);
        Assert.Null(error);
    }

    /// <summary>
    /// JoinSession fires its persistence calls without awaiting them, so there is nothing
    /// to await. Poll briefly rather than sleeping a fixed amount.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task AStudentJoiningAnAlreadyStartedSessionLeavesAnAttendanceRecord()
    {
        using var app = CreateApp();

        string lecturerMatric;
        using (var lecturer = Browser(app))
        {
            lecturerMatric = (await SetUpClassAsync(lecturer)).LecturerMatric;
        }

        var session = await StartStartedSessionAsync(app, new Fixture(1, "unused", lecturerMatric));
        await JoinAsync(app, session, "CSC/2021/002");

        await WaitForAsync(() =>
        {
            using var scope = app.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ViidiiDbContext>()
                .AttendanceLogs.Any(l => l.Student != null && l.Student.MatricNo == "CSC/2021/002");
        });

        using var readScope = app.Services.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<ViidiiDbContext>();

        var logs = await context.AttendanceLogs
            .Include(l => l.Student)
            .Where(l => l.Student != null && l.Student.MatricNo == "CSC/2021/002")
            .ToListAsync();

        Assert.True(
            logs.Count > 0,
            "The student joined a started session, but no attendance row was written for them. "
            + "The join exists only in memory and is gone when the process restarts.");
    }

    /// <summary>
    /// The participant list and the recap are both built from SessionParticipants, so a
    /// join that writes no participant row shows the lecturer an empty room.
    /// </summary>
    [Fact]
    public async Task AStudentWithNoAccountIsRecordedAsASessionParticipant()
    {
        using var app = CreateApp();

        Fixture fixture;
        using (var lecturer = Browser(app))
        {
            fixture = await SetUpClassAsync(lecturer);
        }

        var session = await StartStartedSessionAsync(app, fixture);
        await JoinAsync(app, session, "CSC/2021/002");

        await WaitForAsync(() =>
        {
            using var scope = app.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ViidiiDbContext>()
                .SessionParticipants.Any(p => p.User != null && p.User.MatricNo == "CSC/2021/002");
        });

        using var readScope = app.Services.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<ViidiiDbContext>();

        var stored = await context.SessionParticipants
            .Include(p => p.User)
            .Where(p => p.User != null && p.User.MatricNo == "CSC/2021/002")
            .ToListAsync();

        Assert.True(
            stored.Count > 0,
            "The student is on the roster and nowhere else, so there is no user row to hang "
            + "a participant record off. Nothing was written, which means the lecturer's "
            + "participant list and recap will be empty after a restart.");
    }
}