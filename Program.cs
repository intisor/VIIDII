using VIIDII.Components;
using VIIDII.Data;
using VIIDII.Endpoints;
using VIIDII.Hubs;
using VIIDII.Models;
using VIIDII.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Web App services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add HTTP context accessor for session management
builder.Services.AddHttpContextAccessor();

// Database provider is a deployment decision, chosen from configuration.
// Defaults to SQLite outside production so a fresh clone runs with no DB server.
builder.Services.AddViidiiDatabase(builder.Configuration, builder.Environment);

// Add application services
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<SessionRepository>();
builder.Services.AddScoped<MessageRepository>();
builder.Services.AddScoped<SessionPersistenceService>();
builder.Services.AddScoped<MessagePersistenceService>();

// Register session/message services.
// NOTE: SessionService depends on scoped services (SessionPersistenceService,
// HttpContextAccessor), so it MUST be scoped. Registering it as a singleton leaks
// DbContexts across requests and shares state between users.
builder.Services.AddScoped<SessionService>();

// One clock for the whole app, injectable so tests can move it.
//
// Session timing is business logic: sessions expire, attendance is scored as a
// fraction of session length, and stale sessions are swept. None of that can be
// tested against DateTime.UtcNow without either sleeping or asserting on
// "roughly now". TimeProvider.System is the real clock, so production behaviour is
// unchanged; FakeTimeProvider in tests replaces it wholesale.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StudentSeatService>();

// HttpClient for Blazor components.
//
// Registered rather than built inside a component, because both failure modes are
// silent at build time: an @inject for an unregistered type kills the circuit on the
// first render, and constructing one in a component throws during instantiation.
builder.Services.AddScoped(sp => new HttpClient
{
    // NavigationManager is scoped to the circuit in Blazor Server, so the base address
    // is whatever address the browser is actually on. Components used to build their
    // own client with new Uri("/"), which is relative: HttpClient.BaseAddress requires
    // an absolute URI, so the page threw UriFormatException and never rendered.
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});
builder.Services.AddScoped<MessageService>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RosterService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<ISessionJsInterop, SessionJsInterop>();

// Add hosted service for participant ping
builder.Services.AddHostedService<ParticipantPingService>();
// Add hosted service for session cleanup
builder.Services.AddHostedService<SessionCleanupService>();

// Add distributed memory cache
builder.Services.AddDistributedMemoryCache();

// Add session management
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".VIIDII.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add authentication + authorization.
// Cookie auth is issued by the /auth/login endpoint, NOT from inside a Blazor
// Server circuit — cookies cannot be written once a circuit is established.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".VIIDII.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Always require HTTPS in production. Over plain HTTP a Secure cookie is
        // never sent back by the browser, which silently breaks sign-in during local
        // development and in tests — the symptom is "login succeeds then every
        // authenticated call returns 401".
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";

        // A browser navigating to a page should be redirected to sign in. A script
        // calling the API should get a status code it can act on, not an HTML login
        // page that fails to parse as JSON. Split the two on the request path.
        options.Events.OnRedirectToLogin = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

static bool IsApiRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ViidiiPolicies.LecturerOnly, policy =>
        policy.RequireRole(Role.Lecturer.ToString()));

    options.AddPolicy(ViidiiPolicies.StaffOrAdmin, policy =>
        policy.RequireRole(Role.Lecturer.ToString(), Role.Admin.ToString()));

    options.AddPolicy(ViidiiPolicies.AdminOnly, policy =>
        policy.RequireRole(Role.Admin.ToString()));
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();

// Add SignalR with Blazor compatibility
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// Apply pending migrations and create database.
//
// Migrations are provider-specific: the checked-in migration is scaffolded against
// PostgreSQL, the production provider. Running it against SQLite in development
// would fail on PostgreSQL type names, so development and tests build the schema
// from the model with EnsureCreated() instead. Production applies migrations.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ViidiiDbContext>();
    var providerName = DatabaseRegistration.ResolveProvider(builder.Configuration, builder.Environment);

    if (providerName == DatabaseProvider.Sqlite && !builder.Environment.IsProduction())
    {
        dbContext.Database.EnsureCreated();
        app.Logger.LogInformation("SQLite schema created from the model (development).");
    }
    else
    {
        dbContext.Database.Migrate();
        app.Logger.LogInformation("Migrations applied ({Provider}).", providerName);
    }

    if (app.Environment.IsDevelopment())
    {
        await DatabaseSeeder.SeedAsync(dbContext);
    }
    else
    {
        app.Logger.LogWarning(
            "Development seeding skipped. No demo accounts were created for this environment.");
    }
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession();
app.Use(async (context, next) =>
{
    try
    {
        if (context.Session != null && !context.Session.Keys.Contains("SessionInit"))
        {
            context.Session.SetString("SessionInit", "1");
        }
    }
    catch (Exception ex)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("VIIDII.Session")
            .LogWarning(ex, "Could not initialize session");
    }
    await next();
});
app.UseAntiforgery();

// Map Blazor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ---------------------------------------------------------------------------
// Authentication endpoints
// Cookie issuance MUST happen outside the Blazor circuit so that Set-Cookie
// reaches the browser.
// ---------------------------------------------------------------------------

app.MapPost("/auth/login", async (
    HttpContext http,
    IAntiforgery antiforgery,
    AuthService authService,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("VIIDII.Auth");

    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException)
    {
        logger.LogWarning("Rejected login with invalid antiforgery token.");
        return Results.BadRequest("Invalid request. Please reload and try again.");
    }

    var form = await http.Request.ReadFormAsync();
    var matricNo = form["matricNo"].ToString().Trim();
    var password = form["password"].ToString();

    var (success, message, user) = await authService.ValidateCredentialsAsync(matricNo, password);

    if (!success || user is null)
    {
        logger.LogInformation("Failed login attempt for matric number {MatricNo}", matricNo);
        // Return to login with a generic error; never reveal whether the user exists.
        // Carry the number back so it is not retyped on a phone.
        return Results.Redirect($"/login?error=invalid&matricNo={Uri.EscapeDataString(matricNo)}");
    }

    var principal = ViidiiPrincipalBuilder.Build(user);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = false });

    logger.LogInformation("Successful login for {MatricNo} ({Role})", user.MatricNo, user.Role);

    return Results.Redirect(SafeReturnUrl(form["returnUrl"].ToString(), user.Role));
});

app.MapPost("/auth/register", async (
    HttpContext http,
    IAntiforgery antiforgery,
    ViidiiDbContext context,
    PasswordHasher<User> hasher,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("VIIDII.Auth");

    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException)
    {
        logger.LogWarning("Rejected registration with invalid antiforgery token.");
        return Results.BadRequest("Invalid request. Please reload and try again.");
    }

    var form = await http.Request.ReadFormAsync();
    var name = form["name"].ToString().Trim();
    var matricNo = form["matricNo"].ToString().Trim();
    var password = form["password"].ToString();

    // Same rules as the JSON endpoint in AccountEndpoints so the two cannot drift
    // apart. On failure we return to this page with a code, keeping whatever was
    // already typed.
    if (name.Length is < 2 or > 100)
    {
        return Results.Redirect("/signup?error=name");
    }

    if (matricNo.Length is < 3 or > 20)
    {
        return Results.Redirect($"/signup?error=matric&name={Uri.EscapeDataString(name)}");
    }

    if (password.Length < 8)
    {
        return Results.Redirect(
            $"/signup?error=password&name={Uri.EscapeDataString(name)}&matricNo={Uri.EscapeDataString(matricNo)}");
    }

    // Lecturer-first: every self-service account is a lecturer. Students join from
    // a class link and never register.
    if (await context.Users.AnyAsync(u => u.MatricNo == matricNo))
    {
        logger.LogInformation("Rejected registration for already registered {MatricNo}", matricNo);
        return Results.Redirect($"/signup?error=duplicate&name={Uri.EscapeDataString(name)}");
    }

    var user = new User
    {
        Name = name,
        MatricNo = matricNo,
        PasswordHash = hasher.HashPassword(null!, password),
        Role = Role.Lecturer,
        CreatedAt = DateTime.UtcNow
    };

    context.Users.Add(user);
    await context.SaveChangesAsync();

    var principal = ViidiiPrincipalBuilder.Build(user);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = false });

    logger.LogInformation("Registered lecturer {MatricNo}", user.MatricNo);

    return Results.Redirect(DashboardPathFor(user.Role));
});

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// Map SignalR hub.
// Authorization is enforced INSIDE the hub — a cookie issued to the browser is not
// automatically presented on the websocket handshake for every client.
app.MapHub<SessionHub>("/sessionHub", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapRosterEndpoints();
app.MapClassEndpoints();
app.MapPublicClassEndpoints();
app.MapAccountEndpoints();
app.MapJoinEndpoints();

// ---------------------------------------------------------------------------
// ICE / TURN configuration for the browser.
//
// Credentials are issued per-session rather than hardcoded, so switching from
// public relays to a self-hosted coturn is a configuration change only.
// Uses the TURN REST API shared-secret scheme, which is the supported way to
// hand out time-limited TURN credentials.
// ---------------------------------------------------------------------------

app.MapGet("/api/config/ice", (IConfiguration config, IWebHostEnvironment env) =>
{
    var section = config.GetSection("TurnServers");
    var servers = section.GetSection("Servers").Get<List<TurnServerOptions>>()
                  ?? new List<TurnServerOptions>();

    var usingPublicFallback = servers.Count == 0;
    if (usingPublicFallback && !env.IsDevelopment())
    {
        // Loud, actionable warning — do not fail the request. Lecturers can still run
        // sessions on a good network; they will just fail on poor ones.
        app.Logger.LogError(
            "No TurnServers configured in a non-Development environment. " +
            "WebRTC relay is unavailable and sessions will fail on restrictive NATs. " +
            "Configure TurnServers (see docs/TURN_AND_MEDIA.md).");
    }

    var result = new List<IceServerDto>();
    var turnSecret = section["SharedSecret"];

    foreach (var server in servers)
    {
        if (server.Urls.Count == 0)
        {
            continue;
        }

        if (!string.IsNullOrWhiteSpace(server.Username) && !string.IsNullOrWhiteSpace(server.Credential))
        {
            result.Add(new IceServerDto { Urls = server.Urls, Username = server.Username, Credential = server.Credential });
            continue;
        }

        // Time-limited HMAC credentials via the TURN REST API (RFC draft "turn-rest").
        if (!string.IsNullOrWhiteSpace(turnSecret) && !string.IsNullOrWhiteSpace(server.Url))
        {
            var ttl = TimeSpan.FromHours(server.CredentialTtlHours <= 0 ? 12 : server.CredentialTtlHours);
            var (username, credential) = TurnCredentialGenerator.Generate(turnSecret, ttl);
            result.Add(new IceServerDto
            {
                Urls = server.Urls,
                Username = username,
                Credential = credential
            });
        }
    }

    return Results.Ok(new
    {
        iceServers = result,
        usingPublicFallback,
        message = usingPublicFallback
            ? "Public relay servers in use. Rate limited; will fail under real load."
            : null
    });
});

app.Run();

static string DashboardPathFor(Role role) => role switch
{
    Role.Admin => "/admin",
    Role.Lecturer => "/lecturer/home",
    Role.Student => "/student/home",
    _ => "/dashboard",
};

/// <summary>
/// Where to send someone after signing in.
/// <para>
/// The form carries a returnUrl so a lecturer who was bounced off a protected page
/// lands back on it. That value arrives from the browser, so it is treated as
/// untrusted: only a single-slash local path is honoured. A protocol relative
/// "//host" target is rejected too, because it would turn the sign in form into an
/// open redirect that sends the lecturer's session to another host.
/// </para>
/// </summary>
static string SafeReturnUrl(string? candidate, Role role)
{
    var fallback = DashboardPathFor(role);

    if (string.IsNullOrWhiteSpace(candidate) || !candidate.StartsWith('/'))
    {
        return fallback;
    }

    var second = candidate.Length > 1 ? candidate[1] : ' ';

    if (second is '/' or (char)92)
    {
        return fallback;
    }

    return candidate;
}

/// <summary>
/// Exposed so integration tests can use <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
