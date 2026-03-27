using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Services;

public class MidnightResetService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MidnightResetService> _logger;
    private readonly ISystemClock _systemClock;
    private readonly IFileSystem _fileSystem;

    public MidnightResetService(
        IServiceProvider serviceProvider, 
        ILogger<MidnightResetService> logger,
        ISystemClock systemClock,
        IFileSystem fileSystem)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _systemClock = systemClock;
        _fileSystem = fileSystem;
    }

    private readonly string _lastResetFilePath = Path.Combine(AppContext.BaseDirectory, "last_reset.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Midnight Reset Service is starting with 1-second polling.");

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
                _logger.LogError(ex, "Error in Midnight Reset polling loop.");
            }

            try
            {
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<DateTime> GetLastResetDateAsync()
    {
        if (_fileSystem.Exists(_lastResetFilePath))
        {
            var content = await _fileSystem.ReadAllTextAsync(_lastResetFilePath);
            if (DateTime.TryParse(content, out var lastReset))
            {
                return lastReset.Date;
            }
        }
        
        // If file doesn't exist, assume today so we don't trigger a massive reset on first run
        // unless there are coins, but PerformResetAsync handles accountability.
        return _systemClock.Today;
    }

    private async Task SaveLastResetDateAsync(DateTime date)
    {
        await _fileSystem.WriteAllTextAsync(_lastResetFilePath, date.ToString("yyyy-MM-dd"));
    }

    private async Task PerformResetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting midnight reset and coin archival.");

        // Sync: Trigger a flush in TikTokTrackerService to ensure all buffered gifts are persisted
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
        
        using var scope = _serviceProvider.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

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
                
                // Save reset date after successful DB update
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
            // Even if no accounts, mark the day as reset so we don't keep checking on startup
            await SaveLastResetDateAsync(_systemClock.Today);
        }
    }
}
