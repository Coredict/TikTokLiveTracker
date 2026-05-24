using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Services;

public class MidnightResetService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MidnightResetService> _logger;
    private readonly ISystemClock _systemClock;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string LastResetDateKey = "LastResetDate";

    public MidnightResetService(
        IServiceProvider serviceProvider,
        ILogger<MidnightResetService> logger,
        ISystemClock systemClock,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _systemClock = systemClock;
        _dbFactory = dbFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Midnight Reset Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _systemClock.Now;
                var lastResetDate = await GetLastResetDateAsync();

                if (now.Date > lastResetDate)
                {
                    _logger.LogInformation("Day change detected (Current: {Now}, Last Reset: {LastReset}). Performing reset.", now.Date, lastResetDate);
                    await PerformResetAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Midnight Reset loop.");
            }

            try
            {
                // Sleep until just after the next midnight rather than polling every second.
                // A small 5-second buffer prevents waking up a hair early due to timer drift.
                var now = _systemClock.Now;
                var nextMidnight = now.Date.AddDays(1);
                var delay = nextMidnight - now + TimeSpan.FromSeconds(5);
                _logger.LogDebug("Next midnight reset check in {Delay:hh\\:mm\\:ss}.", delay);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<DateTime> GetLastResetDateAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == LastResetDateKey);

        if (setting != null && DateTime.TryParse(setting.Value, out var lastReset))
        {
            return lastReset.Date;
        }

        // First run — seed with today's date so the NEXT midnight triggers a reset.
        // Without persisting, the fallback would return a moving "today" that always
        // equals now.Date, making the reset condition permanently false.
        var today = _systemClock.Today;
        db.SystemSettings.Add(new SystemSetting { Key = LastResetDateKey, Value = today.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync();
        _logger.LogInformation("First run detected — seeded LastResetDate as {Date}.", today.ToString("yyyy-MM-dd"));
        return today;
    }

    private async Task SaveLastResetDateAsync(DateTime date)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == LastResetDateKey);

        if (setting == null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = LastResetDateKey, Value = date.ToString("yyyy-MM-dd") });
        }
        else
        {
            setting.Value = date.ToString("yyyy-MM-dd");
        }

        await db.SaveChangesAsync();
    }

    private async Task PerformResetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting midnight reset and coin archival.");

        IDisposable? flushHold = null;
        var tracker = _serviceProvider.GetService<TikTokTrackerService>();

        // Flush buffered gifts AND hold the lock so no concurrent flush can race
        // with the CoinsToday = 0 write below.
        try
        {
            if (tracker != null)
            {
                _logger.LogInformation("Flushing gift buffer and acquiring flush lock before reset.");
                flushHold = await tracker.FlushAndHoldLockAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush/lock before midnight reset. Proceeding without lock — some coins might be misattributed.");
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var archivalDate = _systemClock.Today.AddDays(-1);
            var allAccounts = await db.Accounts.ToListAsync(cancellationToken);

            if (allAccounts.Any())
            {
                foreach (var account in allAccounts)
                {
                    _logger.LogInformation("Archiving {Coins} coins for @{Username}", account.CoinsToday, account.Username);

                    db.DailyCoinEarnings.Add(new DailyCoinEarning
                    {
                        TikTokAccountId = account.Id,
                        Date = archivalDate,
                        Coins = account.CoinsToday
                    });

                    account.CoinsToday = 0;
                }

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Successfully archived coins and reset daily totals for {Count} accounts.", allAccounts.Count);

                    await SaveLastResetDateAsync(_systemClock.Today);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save changes during midnight reset.");
                }
            }
            else
            {
                _logger.LogInformation("No accounts found to archive.");
                await SaveLastResetDateAsync(_systemClock.Today);
            }

            // Signal the tracker to refresh its cached accounts immediately
            tracker?.RefreshAccountCache();
        }
        finally
        {
            flushHold?.Dispose(); // Release the flush lock
        }
    }
}
