using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Guards a bug that made the API look authenticated when it was not.
///
/// `group.MapGet("")` registers the route as "/api/classes/" with a trailing slash.
/// A request to "/api/classes" was answered with a 302 redirect. Any HTTP client that
/// follows redirects — including the test client and every browser — then landed on
/// the Blazor shell, which returns 200 and the app document.
///
/// The symptom was an unauthenticated caller receiving a 200 HTML page instead of a
/// 401. Nothing leaked, but every auth check on that route was untested, because the
/// test only ever saw the final 200.
///
/// These assert that API paths reach the API: never HTML, never a redirect.
/// </summary>
public class ApiRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiRouteTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> CreateApp()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-routes-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });
    }

    /// <summary>Client that does not follow redirects, so a 302 stays visible.</summary>
    private static HttpClient CreateNonRedirectingClient(WebApplicationFactory<Program> app)
    {
        var options = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        };

        return app.CreateClient(options);
    }

    [Theory]
    [InlineData("/api/classes")]
    [InlineData("/api/classes/")]
    public async Task ClassCollectionPaths_AreNotRedirectsAndNotHtml(string path)
    {
        using var app = CreateApp();
        using var client = CreateNonRedirectingClient(app);

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        var location = response.Headers.Location?.ToString() ?? "(none)";

        Assert.True(
            response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.MovedPermanently),
            $"{path} returned {(int)response.StatusCode} redirecting to {location}.");
        Assert.False(
            body.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase),
            $"{path} returned the Blazor shell instead of reaching the API.");
    }

    [Fact]
    public async Task ClassCollection_ReturnsUnauthorizedForAnonymousCallers()
    {
        using var app = CreateApp();
        using var client = CreateNonRedirectingClient(app);

        var list = await client.GetAsync("/api/classes");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/classes/9999999")]
    [InlineData("/api/join/NOSUCHCODE")]
    public async Task UnknownApiPaths_ReturnJsonNotHtml(string path)
    {
        using var app = CreateApp();
        using var client = CreateNonRedirectingClient(app);

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.False(
            body.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase),
            $"{path} returned HTML instead of an API response.");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized,
            $"{path} returned {(int)response.StatusCode}, expected 404 or 401.");
    }

    [Fact]
    public async Task ApiPaths_AreNotShadowedByTheBlazorShell()
    {
        using var app = CreateApp();
        using var client = CreateNonRedirectingClient(app);

        // An application page legitimately returns HTML. This confirms the shell works,
        // which is what makes the API checks above meaningful.
        var page = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var body = await page.Content.ReadAsStringAsync();
        Assert.True(
            body.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase));
    }
}