using FutaMeetWeb.Hubs;
using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddRazorPages();
    builder.Services.AddSingleton<MockApiService>();
    builder.Services.AddSingleton<SessionService>();
	builder.Services.AddSingleton<MessageService>();
	builder.Services.AddScoped<PasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddHostedService<ParticipantPingService>();	
	builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
        options.Cookie.Name = ".FutaMeet.Session";
        options.IdleTimeout = TimeSpan.FromMinutes(20);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = true;
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    });

var app = builder.Build();

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthorization();
    app.MapStaticAssets();
    app.UseSession();
    app.MapHub<SessionHub>("/sessionHub", options =>
	{
		options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
	});
    app.MapRazorPages()
		.WithStaticAssets();
app.Run();
