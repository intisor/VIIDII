using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// The sign in and create account screens, driven as a browser drives them.
/// <para>
/// Both pages are server rendered forms posting to /auth/login and /auth/register.
/// They are not interactive Blazor components, because the authentication cookie
/// has to be set in the response to the form post itself. These tests fetch the
/// antiforgery token from the rendered HTML and post it back, which is the only
/// way these two endpoints can be exercised at all.
/// </para>
/// </summary>
public class AuthFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthFlowTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> CreateApp()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-auth-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });
    }

    private static async Task<HttpClient> OpenFormAsync(WebApplicationFactory<Program> app, string path)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var page = await client.GetAsync(path);
        page.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>
    /// Antiforgery requires the cookie set when the page was rendered to be
    /// presented alongside the hidden field on the post. Both travel with the
    /// client's cookie container, so scraping the field is sufficient.
    /// </summary>
    private static async Task<string> ReadAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");

        Assert.True(match.Success, $"No antiforgery token found on {path}.");

        return match.Groups["token"].Value;
    }

    [Fact]
    public async Task SignInPageRendersAUsableForm()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"matricNo\"", html);
        Assert.Contains("name=\"password\"", html);
        Assert.Contains("action=\"/auth/login\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task CreateAccountPageRendersAUsableForm()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/signup");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"name\"", html);
        Assert.Contains("name=\"matricNo\"", html);
        Assert.Contains("name=\"password\"", html);
        Assert.Contains("action=\"/auth/register\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task BothAuthScreensAreReachableWithoutSigningIn()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        // Both must answer directly. A redirect to the login page from the login
        // page is an infinite loop.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/signup")).StatusCode);
    }

    [Fact]
    public async Task TheAuthStylesheetIsServed()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/css/auth.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/css", response.Content.Headers.ContentType?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task TheShellLinksTheAuthStylesheet()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var html = await client.GetStringAsync("/login");

        Assert.Contains("css/auth.css", html);
    }

    [Fact]
    public async Task RegisteringSignsTheLecturerInAndLandsThemOnTheirClasses()
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var token = await ReadAntiforgeryTokenAsync(client, "/signup");
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("name", "Dr. Aisha Bello"),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/lecturer/home", response.Headers.Location?.ToString());

        // The whole point: the cookie is issued, so the account is usable at once.
        var me = await client.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var body = await me.Content.ReadAsStringAsync();
        Assert.Contains("Lecturer", body);
        Assert.Contains(matricNo, body);
    }

    [Fact]
    public async Task RegisteringTwiceReturnsToTheFormWithAnError()
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var matricNo = $"DUP{Guid.NewGuid().ToString("N")[..8]}";
        var token = await ReadAntiforgeryTokenAsync(client, "/signup");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["name"] = "First Signup",
            ["matricNo"] = matricNo,
            ["password"] = "a-good-password",
        };

        var first = await client.PostAsync("/auth/register", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var token2 = await ReadAntiforgeryTokenAsync(client, "/signup");
        form["__RequestVerificationToken"] = token2;

        var second = await client.PostAsync("/auth/register", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains("/signup?error=duplicate", second.Headers.Location?.ToString() ?? string.Empty);
    }

    [Theory]
    [InlineData("", "VALIDNUM", "a-good-password", "/signup?error=name")]
    [InlineData("Real Name", "AB", "a-good-password", "/signup?error=matric")]
    [InlineData("Real Name", "VALIDNUM", "short", "/signup?error=password")]
    public async Task BadInputReturnsToTheFormWithTheRightError(
        string name, string matricNo, string password, string expected)
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var token = await ReadAntiforgeryTokenAsync(client, "/signup");

        var response = await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("name", name),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", password),
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(expected, response.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task RegisteringWithoutAnAntiforgeryTokenIsRejected()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var response = await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("name", "No Token"),
            new KeyValuePair<string, string>("matricNo", $"NT{Guid.NewGuid().ToString("N")[..6]}"),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ASuccessfulSignInLandsTheLecturerOnTheirClasses()
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var token = await ReadAntiforgeryTokenAsync(client, "/signup");
        var matricNo = $"SI{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("name", "Dr. Tunde Adeyemi"),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        await client.PostAsync("/auth/logout", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        var signInToken = await ReadAntiforgeryTokenAsync(client, "/login");

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", signInToken),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/lecturer/home", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AWrongPasswordReturnsToTheFormWithoutSayingWhetherTheAccountExists()
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/login");

        var token = await ReadAntiforgeryTokenAsync(client, "/login");

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("matricNo", $"NOPE{Guid.NewGuid().ToString("N")[..6]}"),
            new KeyValuePair<string, string>("password", "wrong-password"),
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? string.Empty;

        Assert.Contains("/login?error=invalid", location);
        Assert.DoesNotContain("exist", location, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("evil.example")]
    [InlineData("")]
    public async Task AnOffSiteReturnUrlIsIgnored(string returnUrl)
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var token = await ReadAntiforgeryTokenAsync(client, "/signup");
        var matricNo = $"OR{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("name", "Redirect Tester"),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        await client.PostAsync("/auth/logout", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        var signInToken = await ReadAntiforgeryTokenAsync(client, "/login");

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", signInToken),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
            new KeyValuePair<string, string>("returnUrl", returnUrl),
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Anything not a plain local path must fall back to the lecturer's home.
        Assert.Equal("/lecturer/home", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ALocalReturnUrlIsHonoured()
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var token = await ReadAntiforgeryTokenAsync(client, "/signup");
        var matricNo = $"LR{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("name", "Return Url Tester"),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        await client.PostAsync("/auth/logout", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        var signInToken = await ReadAntiforgeryTokenAsync(client, "/login");

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", signInToken),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
            new KeyValuePair<string, string>("returnUrl", "/create-session"),
        }));

        Assert.Equal("/create-session", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task SigningOutClearsTheSessionAndReturnsToTheHomePage()
    {
        using var app = CreateApp();
        using var client = await OpenFormAsync(app, "/signup");

        var token = await ReadAntiforgeryTokenAsync(client, "/signup");
        var matricNo = $"SO{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsync("/auth/register", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("name", "Sign Out Tester"),
            new KeyValuePair<string, string>("matricNo", matricNo),
            new KeyValuePair<string, string>("password", "a-good-password"),
        }));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);

        var logout = await client.PostAsync("/auth/logout", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", logout.Headers.Location?.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task TheNavOffersSignInAndCreateAccountToVisitors()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("href=\"/login\"", html);
        Assert.Contains("href=\"/signup\"", html);
    }

    [Theory]
    [InlineData("/login", "Sign in - VIIDII")]
    [InlineData("/signup", "Create an account - VIIDII")]
    [InlineData("/about", "About VIIDII")]
    public async Task EachAuthScreenShowsItsOwnTitle(string path, string expected)
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var html = await client.GetStringAsync(path);

        // A hardcoded <title> above <HeadOutlet /> used to win over every page's
        // <PageTitle>, so a lecturer with several tabs open saw "VIIDII" on all
        // of them and could not tell which was the sign in screen.
        var titles = Regex.Matches(html, "<title>(?<title>.*?)</title>");

        Assert.True(titles.Count > 0, $"No <title> rendered on {path}.");
        Assert.Equal(expected, titles[0].Groups["title"].Value);
    }

    [Fact]
    public async Task NoPageIsLeftWithoutATitle()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        // Pages rendered with prerender:false send no title from the server, so a
        // fallback after <HeadOutlet /> has to exist or the tab shows a bare URL.
        foreach (var path in new[] { "/", "/login", "/signup", "/about" })
        {
            var html = await client.GetStringAsync(path);
            var titles = Regex.Matches(html, "<title>(?<title>.*?)</title>");

            Assert.True(titles.Count > 0, $"No <title> at all on {path}.");
            Assert.False(
                string.IsNullOrWhiteSpace(titles[0].Groups["title"].Value),
                $"Empty <title> on {path}.");
        }
    }

    [Fact]
    public async Task TheErrorBannerIsHiddenOnTheAuthScreens()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var css = await client.GetStringAsync("/css/site.css");

        // The banner sits in the markup of every page. Without a display:none
        // default it showed "An unhandled error has occurred" to every visitor
        // arriving on a perfectly working page.
        Assert.Contains("#blazor-error-ui", css);
        Assert.Matches(@"#blazor-error-ui\s*\{[^}]*display:\s*none", css);
    }

    [Fact]
    public async Task SignInAndSignUpPages_RenderGoogleButton()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();

        var loginHtml = await client.GetStringAsync("/login");
        Assert.Contains("href=\"/auth/google\"", loginHtml);
        Assert.Contains("Continue with Google", loginHtml);

        var signupHtml = await client.GetStringAsync("/signup");
        Assert.Contains("href=\"/auth/google\"", signupHtml);
        Assert.Contains("Sign up with Google", signupHtml);
    }

    [Fact]
    public async Task GoogleAuthEndpoint_RedirectsGracefully_WhenUnconfigured()
    {
        using var app = CreateApp();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.GetAsync("/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/login?error=google_unconfigured", resp.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GoogleAuthEndpoint_TriggersChallenge_WhenConfigured()
    {
        using var app = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-google-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseSetting("Authentication:Google:ClientId", "mock-google-client-id");
            builder.UseSetting("Authentication:Google:ClientSecret", "mock-google-client-secret");
            builder.UseEnvironment("Development");
        });

        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.GetAsync("/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.Contains("accounts.google.com", resp.Headers.Location.OriginalString);
        Assert.Contains("client_id=mock-google-client-id", resp.Headers.Location.OriginalString);
    }
}