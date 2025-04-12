using FutaMeetWeb.Hubs;
using FutaMeetWeb.Services;

var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddRazorPages();
    builder.Services.AddSingleton<MockApiService>();
    builder.Services.AddSingleton<SessionService>();
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
    	options.IdleTimeout = TimeSpan.FromMinutes(4);
    	options.Cookie.HttpOnly = true;
    	options.Cookie.IsEssential = true;
    });
    builder.Services.AddSignalR();

var app = builder.Build();
    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthorization();
    app.MapStaticAssets();
    app.UseSession();
    app.MapHub<SessionHub>("/SessionHub");
    app.MapRazorPages()
		.WithStaticAssets();
app.Run();
