using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using VIIDII.Data;
using VIIDII.Models;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// End-to-end walk through the proof of concept, exercising the real HTTP pipeline
/// and a real database rather than services in isolation.
///
/// The flow is the one a lecturer actually performs:
///   register -> create class -> upload the student list -> student joins
///
/// A 41-file scripted rename ran before these tests existed. This is the check that
/// it did not quietly break the path a user walks.
/// </summary>
public class PocFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PocFlowTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> CreateApp()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-poc-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task Lecturer_Can_Register_CreateClass_UploadList_And_StudentCanJoin()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];

        // ---- 1. Register ----
        var register = await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Dr. Aisha Bello",
            matricNo = $"LEC{suffix}",
            password = "a-good-password"
        });

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var account = await register.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Lecturer", account.GetProperty("role").GetString());

        // ---- 2. Create a class ----
        var classCode = $"CS{suffix.ToUpper()}";

        var createClass = await client.PostAsJsonAsync("/api/classes", new
        {
            code = classCode,
            name = "100 Level Computer Science"
        });

        Assert.Equal(HttpStatusCode.OK, createClass.StatusCode);

        var created = await createClass.Content.ReadFromJsonAsync<JsonElement>();
        var classId = created.GetProperty("id").GetInt32();

        Assert.Equal("100 Level Computer Science", created.GetProperty("name").GetString());

        // ---- 3. Upload the student list ----
        var csv = $"""
        Matric Number,Full Name,Email
        CSC/2021/001,Aisha Bello,a.bello@uni.edu.ng
        CSC/2021/002,Babatunde Cole,b.cole@uni.edu.ng
        CSC/2021/003,Chioma Nwosu,c.nwosu@uni.edu.ng
        """;

        var import = await client.PostAsJsonAsync($"/api/roster/classes/{classId}/import", new
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

        var imported = await import.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, imported.GetProperty("imported").GetInt32());
        Assert.Equal("Matric Number", imported.GetProperty("identifierHeader").GetString());

        // ---- 4. A student looks up the class by code ----
        var lookup = await client.GetAsync($"/api/join/{classCode}");

        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);

        var details = await lookup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Enter your matric number", details.GetProperty("prompt").GetString());
        Assert.True(details.GetProperty("hasList").GetBoolean());

        // ---- 5. The student joins ----
        var join = await client.PostAsJsonAsync($"/api/join/{classCode}", new
        {
            identifier = "CSC/2021/002",
            name = "Babatunde Cole"
        });

        Assert.Equal(HttpStatusCode.OK, join.StatusCode);

        var joined = await join.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Babatunde Cole", joined.GetProperty("displayName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(joined.GetProperty("claimToken").GetString()));

        // ---- 6. A wrong name for a real matric number is refused ----
        var wrongName = await client.PostAsJsonAsync($"/api/join/{classCode}", new
        {
            identifier = "CSC/2021/003",
            name = "Babatunde Cole"
        });

        Assert.Equal(HttpStatusCode.NotFound, wrongName.StatusCode);
    }

    [Fact]
    public async Task JoiningWithAnUnknownCodeReturnsNotFound()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/api/join/NOSUCHCODE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task JoinIsRefusedWhenTheLecturerHasNotUploadedAList()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];

        await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Lecturer Two",
            matricNo = $"LEC{suffix}",
            password = "a-good-password"
        });

        var classCode = $"EM{suffix.ToUpper()}";
        await client.PostAsJsonAsync("/api/classes", new { code = classCode, name = "Empty Class" });

        var join = await client.PostAsJsonAsync($"/api/join/{classCode}", new
        {
            identifier = "ANYTHING",
            name = "Anyone"
        });

        Assert.Equal(HttpStatusCode.BadRequest, join.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsADuplicateNumber()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var payload = new
        {
            name = "Duplicate Test",
            matricNo = $"DUP{suffix}",
            password = "a-good-password"
        };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/account/register", payload)).StatusCode);

        var second = await client.PostAsJsonAsync("/api/account/register", payload);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsAShortPassword()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Short Password",
            matricNo = $"SP{Guid.NewGuid().ToString("N")[..6]}",
            password = "short"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatingAClassWithATakenCodeIsRejected()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Code Clash",
            matricNo = $"LEC{suffix}",
            password = "a-good-password"
        });

        var code = $"DU{suffix.ToUpper()}";

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/classes", new { code, name = "First" })).StatusCode);

        var second = await client.PostAsJsonAsync("/api/classes", new { code, name = "Second" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ClassListEndpointsRequireAuthentication()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var list = await client.GetAsync("/api/classes");
        var create = await client.PostAsJsonAsync("/api/classes", new { code = "NOPE", name = "Nope" });

        var body = await list.Content.ReadAsStringAsync();
        Assert.True(
            list.StatusCode == HttpStatusCode.Unauthorized,
            $"GET /api/classes as anonymous returned {(int)list.StatusCode}. Body: {body}");

        Assert.True(
            create.StatusCode == HttpStatusCode.Unauthorized,
            $"POST /api/classes as anonymous returned {(int)create.StatusCode}.");
    }

    [Fact]
    public async Task ALecturerCannotReadAnotherLecturersClassList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Two fully independent app instances, so the two cookie jars cannot be
        // shared. Two clients from one factory were not proven to be isolated.
        using var appA = CreateApp();
        using var appB = CreateApp();

        using var clientA = appA.CreateClient();
        await clientA.PostAsJsonAsync("/api/account/register", new
        {
            name = "Lecturer A",
            matricNo = $"AA{suffix}",
            password = "a-good-password"
        });

        var created = await clientA.PostAsJsonAsync("/api/classes", new { code = $"PR{suffix.ToUpper()}", name = "Private" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var classId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Sanity check: A can see their own list.
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync($"/api/roster/classes/{classId}")).StatusCode);

        using var clientB = appB.CreateClient();
        await clientB.PostAsJsonAsync("/api/account/register", new
        {
            name = "Lecturer B",
            matricNo = $"BB{suffix}",
            password = "a-good-password"
        });

        // Sanity check: B is signed in.
        var me = await clientB.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var listAsB = await clientB.GetAsync($"/api/roster/classes/{classId}");

        Assert.Equal(HttpStatusCode.Forbidden, listAsB.StatusCode);
    }
}