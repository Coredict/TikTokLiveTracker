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
            ""CoinsToday"" INTEGER NOT NULL DEFAULT 0,
            ""AutoRecord"" BOOLEAN NOT NULL DEFAULT FALSE
        );
    ";
    db.Database.ExecuteSqlRaw(accountsSql);

    // Migration logic for renaming CurrentCoins to CoinsToday if transitioning from old schema
    try {
        db.Database.ExecuteSqlRaw(@"
            DO $$ 
            BEGIN 
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Accounts' AND column_name='CurrentCoins') THEN
                    ALTER TABLE ""Accounts"" RENAME COLUMN ""CurrentCoins"" TO ""CoinsToday"";
                END IF;
            END $$;
        ");
    } catch { }
    
    // Ensure column exists for already created tables
    try {
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Accounts"" ADD COLUMN IF NOT EXISTS ""AutoRecord"" BOOLEAN NOT NULL DEFAULT FALSE;");
    } catch { /* PostgreSQL 9.6+ supports ADD COLUMN IF NOT EXISTS in some cases, otherwise catch if already present */ }

    // Gifts table
    var giftsSql = @"
        CREATE TABLE IF NOT EXISTS ""Gifts"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""TikTokAccountId"" INTEGER NOT NULL,
            ""SenderUserId"" TEXT NOT NULL DEFAULT '',
            ""SenderUsername"" TEXT NOT NULL,
            ""SenderNickname"" TEXT NOT NULL,
            ""GiftName"" TEXT NOT NULL,
            ""Amount"" INTEGER NOT NULL,
            ""DiamondCost"" INTEGER NOT NULL,
            ""Timestamp"" TIMESTAMP WITH TIME ZONE NOT NULL,
            ""StreakId"" TEXT,
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
            ""SenderUserId"" TEXT NOT NULL DEFAULT '',
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

    // DailyCoinEarnings table
    var dailyEarningsSql = @"
        CREATE TABLE IF NOT EXISTS ""DailyCoinEarnings"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""TikTokAccountId"" INTEGER NOT NULL,
            ""Date"" TIMESTAMP WITH TIME ZONE NOT NULL,
            ""Coins"" INTEGER NOT NULL,
            CONSTRAINT ""FK_DailyCoinEarnings_Accounts_TikTokAccountId"" FOREIGN KEY (""TikTokAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ""IX_DailyCoinEarnings_TikTokAccountId"" ON ""DailyCoinEarnings"" (""TikTokAccountId"");
    ";
    db.Database.ExecuteSqlRaw(dailyEarningsSql);
    
    // Add columns if they don't exist
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Gifts"" ADD COLUMN IF NOT EXISTS ""SenderUserId"" TEXT NOT NULL DEFAULT '';"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Gifts"" ADD COLUMN IF NOT EXISTS ""StreakId"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""GifterSummaries"" ADD COLUMN IF NOT EXISTS ""SenderUserId"" TEXT NOT NULL DEFAULT '';"); } catch { }
    
    // Remove ViewerCount column as it is now in-memory only
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Accounts"" DROP COLUMN IF EXISTS ""ViewerCount"";"); } catch { }

    // Update unique index for summaries to use SenderUserId
    try {
        db.Database.ExecuteSqlRaw(@"DROP INDEX IF EXISTS ""IX_GifterSummaries_Account_Sender"";");
        db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GifterSummaries_Account_User"" ON ""GifterSummaries"" (""TikTokAccountId"", ""SenderUserId"");");
    } catch { }
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
