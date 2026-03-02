using VIIDII.Components;
using VIIDII.Hubs;
using VIIDII.Models;
using VIIDII.Services;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire shared service defaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// ===== TESTING MODE CONFIGURATION =====
// Extended timeouts for development/debugging
// For production, reduce these values to production-appropriate settings:
//   - ClientTimeoutInterval: 30-60 seconds
//   - KeepAliveInterval: 10-15 seconds
//   - DisconnectedCircuitRetentionPeriod: 3 minutes
//   - JSInteropDefaultCallTimeout: 1 minute
// ======================================

// Add Blazor Web App services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Blazor Server Circuit options for testing (increased timeouts)
builder.Services.AddServerSideBlazor(options =>
{
    // Extended timeout for testing/debugging (default is 3 minutes)
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    
    // Allow longer JS interop calls (default is 1 minute)
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(5);
    
    // Maximum number of disconnected circuits to retain (default is 100)
    options.DisconnectedCircuitMaxRetained = 100;
});

// Add HTTP context accessor for session management
builder.Services.AddHttpContextAccessor();

// Add application services
builder.Services.AddSingleton<MockApiService>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>(); // CHANGED: Scoped per Blazor circuit (not Singleton)
builder.Services.AddScoped<ISessionJsInterop, SessionJsInterop>();

// Add hosted service for participant ping
builder.Services.AddHostedService<ParticipantPingService>();

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

// Add SignalR with Blazor compatibility (extended timeouts for testing)
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    
    // Increased from 60s to 5 minutes for testing mode
    // This is how long the server waits for a message from the client before timing out
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    
    // Keep-alive ping interval (client expects a message within ClientTimeoutInterval)
    // Should be less than ClientTimeoutInterval (typically 1/2 to 1/3)
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    
    // Maximum message size (default is 32KB, increase if needed for large payloads)
    options.MaximumReceiveMessageSize = 128 * 1024; // 128KB
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAntiforgery();

// Map Aspire health check endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Map Blazor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hub
app.MapHub<SessionHub>("/sessionHub", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
});

app.Run();
