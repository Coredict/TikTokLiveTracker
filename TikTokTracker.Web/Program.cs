using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using TikTokTracker.Web.Components;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();

builder.Services.AddHttpClient("TikTok", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=db;Port=5432;Database=tiktoktracker;Username=postgres;Password=postgres";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<TikTokTracker.Web.Services.AdminSessionService>();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"));

builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IFileSystem, LocalFileSystem>();
builder.Services.AddSingleton<TikTokTrackerService>();
builder.Services.AddSingleton<RecorderClient>();
builder.Services.AddHostedService<TikTokTrackerService>(sp => sp.GetRequiredService<TikTokTrackerService>());
builder.Services.AddSingleton<MidnightResetService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MidnightResetService>());
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();

var app = builder.Build();

// Ensure database tables exist
await app.InitializeDatabaseAsync();


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/recordings/{filename}", async (string filename, RecorderClient recorder) =>
{
    var stream = await recorder.GetDownloadStreamAsync(filename);
    if (stream == null) return Results.NotFound();
    return Results.Stream(stream, "video/mp4", filename);
});

app.MapGet("/api/config/tiktok-session-id", async (ISystemSettingsService settingsService) =>
{
    var sessionId = await settingsService.GetTikTokSessionIdAsync();
    return Results.Ok(new { sessionId });
});

app.Run();
