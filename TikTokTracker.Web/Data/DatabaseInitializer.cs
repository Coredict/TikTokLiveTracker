using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Data;

namespace TikTokTracker.Web.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync();

        // 1. Accounts Table
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
        await db.Database.ExecuteSqlRawAsync(accountsSql);

        // 2. Schema Migrations (Accounts)
        await ExecuteSqlIgnoreErrorAsync(db, @"
            DO $$ 
            BEGIN 
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Accounts' AND column_name='CurrentCoins') THEN
                    ALTER TABLE ""Accounts"" RENAME COLUMN ""CurrentCoins"" TO ""CoinsToday"";
                END IF;
            END $$;
        ");

        await ExecuteSqlIgnoreErrorAsync(db, @"ALTER TABLE ""Accounts"" ADD COLUMN IF NOT EXISTS ""AutoRecord"" BOOLEAN NOT NULL DEFAULT FALSE;");
        await ExecuteSqlIgnoreErrorAsync(db, @"ALTER TABLE ""Accounts"" DROP COLUMN IF EXISTS ""ViewerCount"";");

        // 3. Gifts Table
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
        await db.Database.ExecuteSqlRawAsync(giftsSql);
        await ExecuteSqlIgnoreErrorAsync(db, @"ALTER TABLE ""Gifts"" ADD COLUMN IF NOT EXISTS ""SenderUserId"" TEXT NOT NULL DEFAULT '';");
        await ExecuteSqlIgnoreErrorAsync(db, @"ALTER TABLE ""Gifts"" ADD COLUMN IF NOT EXISTS ""StreakId"" TEXT;");

        // 4. GifterSummaries Table
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
        await db.Database.ExecuteSqlRawAsync(summarySql);
        await ExecuteSqlIgnoreErrorAsync(db, @"ALTER TABLE ""GifterSummaries"" ADD COLUMN IF NOT EXISTS ""SenderUserId"" TEXT NOT NULL DEFAULT '';");

        // 5. DailyCoinEarnings Table
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
        await db.Database.ExecuteSqlRawAsync(dailyEarningsSql);

        // 6. Indices and Constraints
        await ExecuteSqlIgnoreErrorAsync(db, @"DROP INDEX IF EXISTS ""IX_GifterSummaries_Account_Sender"";");
        await ExecuteSqlIgnoreErrorAsync(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GifterSummaries_Account_User"" ON ""GifterSummaries"" (""TikTokAccountId"", ""SenderUserId"");");
    }

    private static async Task ExecuteSqlIgnoreErrorAsync(AppDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch
        {
            // Ignore errors for idempotent migration steps
        }
    }
}
