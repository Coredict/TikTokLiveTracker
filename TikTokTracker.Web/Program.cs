using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using TikTokTracker.Web.Components;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

builder.Services.AddSingleton<TikTokTrackerService>();
builder.Services.AddSingleton<RecorderClient>();
builder.Services.AddHostedService<TikTokTrackerService>(sp => sp.GetRequiredService<TikTokTrackerService>());

var app = builder.Build();

// Ensure database tables exist (PostgreSQL version)
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();
    
    // Create tables if they don't exist
    // Using SERIAL for auto-increment in Postgres
    var accountsSql = @"
        CREATE TABLE IF NOT EXISTS ""Accounts"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Username"" TEXT NOT NULL,
            ""ProfileImageUrl"" TEXT,
            ""IsOnline"" BOOLEAN NOT NULL,
            ""ViewerCount"" INTEGER NOT NULL,
            ""CurrentCoins"" INTEGER NOT NULL,
            ""AutoRecord"" BOOLEAN NOT NULL DEFAULT FALSE
        );
    ";
    db.Database.ExecuteSqlRaw(accountsSql);
    
    // Ensure column exists for already created tables
    try {
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Accounts"" ADD COLUMN IF NOT EXISTS ""AutoRecord"" BOOLEAN NOT NULL DEFAULT FALSE;");
    } catch { /* PostgreSQL 9.6+ supports ADD COLUMN IF NOT EXISTS in some cases, otherwise catch if already present */ }

    // Gifts table
    var giftsSql = @"
        CREATE TABLE IF NOT EXISTS ""Gifts"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""TikTokAccountId"" INTEGER NOT NULL,
            ""SenderUsername"" TEXT NOT NULL,
            ""SenderNickname"" TEXT NOT NULL,
            ""GiftName"" TEXT NOT NULL,
            ""Amount"" INTEGER NOT NULL,
            ""DiamondCost"" INTEGER NOT NULL,
            ""Timestamp"" TIMESTAMP WITH TIME ZONE NOT NULL,
            CONSTRAINT ""FK_Gifts_Accounts_TikTokAccountId"" FOREIGN KEY (""TikTokAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ""IX_Gifts_TikTokAccountId"" ON ""Gifts"" (""TikTokAccountId"");
    ";
    db.Database.ExecuteSqlRaw(giftsSql);

    // GifterSummaries table
    var summarySql = @"
        CREATE TABLE IF NOT EXISTS ""GifterSummaries"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""TikTokAccountId"" INTEGER NOT NULL,
            ""SenderUsername"" TEXT NOT NULL,
            ""SenderNickname"" TEXT NOT NULL,
            ""TotalDiamonds"" INTEGER NOT NULL,
            ""TotalGifts"" INTEGER NOT NULL,
            ""LastGiftTime"" TIMESTAMP WITH TIME ZONE NOT NULL,
            CONSTRAINT ""FK_GifterSummaries_Accounts_TikTokAccountId"" FOREIGN KEY (""TikTokAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ""IX_GifterSummaries_TikTokAccountId"" ON ""GifterSummaries"" (""TikTokAccountId"");
    ";
    db.Database.ExecuteSqlRaw(summarySql);
    
    // Add unique index separately as CREATE UNIQUE INDEX IF NOT EXISTS is standard in modern PG
    try {
        db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GifterSummaries_Account_Sender"" ON ""GifterSummaries"" (""TikTokAccountId"", ""SenderUsername"");");
    } catch { /* Index might already exist or PG version varies */ }
}

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

app.Run();
