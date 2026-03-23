using Microsoft.EntityFrameworkCore;
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

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<TikTokTracker.Web.Services.AdminSessionService>();

builder.Services.AddHostedService<TikTokTrackerService>();

var app = builder.Build();

// Ensure DB is created on startup
using (var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
{
    db.Database.EnsureCreated();
    
    // Manually create Gifts table if it doesn't exist (since EnsureCreated won't add it to an existing DB)
    var sql = @"
        CREATE TABLE IF NOT EXISTS ""Gifts"" (
            ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
            ""TikTokAccountId"" INTEGER NOT NULL,
            ""SenderUsername"" TEXT NOT NULL,
            ""SenderNickname"" TEXT NOT NULL,
            ""GiftName"" TEXT NOT NULL,
            ""Amount"" INTEGER NOT NULL,
            ""DiamondCost"" INTEGER NOT NULL,
            ""Timestamp"" TEXT NOT NULL,
            CONSTRAINT ""FK_Gifts_Accounts_TikTokAccountId"" FOREIGN KEY (""TikTokAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ""IX_Gifts_TikTokAccountId"" ON ""Gifts"" (""TikTokAccountId"");
    ";
    db.Database.ExecuteSqlRaw(sql);

    // Manually create GifterSummaries table
    var summarySql = @"
        CREATE TABLE IF NOT EXISTS ""GifterSummaries"" (
            ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
            ""TikTokAccountId"" INTEGER NOT NULL,
            ""SenderUsername"" TEXT NOT NULL,
            ""SenderNickname"" TEXT NOT NULL,
            ""TotalDiamonds"" INTEGER NOT NULL,
            ""TotalGifts"" INTEGER NOT NULL,
            ""LastGiftTime"" TEXT NOT NULL,
            CONSTRAINT ""FK_GifterSummaries_Accounts_TikTokAccountId"" FOREIGN KEY (""TikTokAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ""IX_GifterSummaries_TikTokAccountId"" ON ""GifterSummaries"" (""TikTokAccountId"");
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GifterSummaries_Account_Sender"" ON ""GifterSummaries"" (""TikTokAccountId"", ""SenderUsername"");
    ";
    db.Database.ExecuteSqlRaw(summarySql);
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

app.Run();
