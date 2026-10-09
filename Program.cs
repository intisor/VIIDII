using VIIDII.Components;
using VIIDII.Data;
using VIIDII.Hubs;
using VIIDII.Models;
using VIIDII.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Web App services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add HTTP context accessor for session management
builder.Services.AddHttpContextAccessor();

// Add DbContext with SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ViidiiDbContext>(options =>
    options.UseSqlServer(connectionString));

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
builder.Services.AddScoped<MessageService>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
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
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });

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
// Seeding is DEVELOPMENT ONLY — seeding demo accounts in production would be a
// critical vulnerability.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ViidiiDbContext>();
    dbContext.Database.Migrate();

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
        return Results.Redirect("/login?error=invalid");
    }

    var principal = ViidiiPrincipalBuilder.Build(user);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = false });

    logger.LogInformation("Successful login for {MatricNo} ({Role})", user.MatricNo, user.Role);

    return Results.Redirect(DashboardPathFor(user.Role));
});

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Map SignalR hub.
// Authorization is enforced INSIDE the hub — a cookie issued to the browser is not
// automatically presented on the websocket handshake for every client.
app.MapHub<SessionHub>("/sessionHub", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static string DashboardPathFor(Role role) => role switch
{
    Role.Admin => "/admin",
    Role.Lecturer => "/lecturer/home",
    Role.Student => "/student/home",
    _ => "/dashboard",
};

/// <summary>
/// Exposed so integration tests can use <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
