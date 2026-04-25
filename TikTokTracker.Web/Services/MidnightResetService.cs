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

        // No record yet — assume today so we don't trigger a reset on first run
        return _systemClock.Today;
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

        // Flush any buffered gifts before archiving so today's totals are accurate
        try
        {
            var tracker = _serviceProvider.GetService<TikTokTrackerService>();
            if (tracker != null)
            {
                _logger.LogInformation("Triggering manual flush of gift buffer before reset.");
                await tracker.ManualFlushAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger manual flush before midnight reset. Some coins might be misattributed to the next day.");
        }

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
    }
}
