using VIIDII.Components;
using VIIDII.Hubs;
using VIIDII.Models;
using VIIDII.Services;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Web App services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add HTTP context accessor for session management
builder.Services.AddHttpContextAccessor();

// Add application services
builder.Services.AddSingleton<MockApiService>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddSingleton<AuthService>();
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

// Add SignalR with Blazor compatibility
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
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

// Map Blazor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hub
app.MapHub<SessionHub>("/sessionHub", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
});

app.Run();
