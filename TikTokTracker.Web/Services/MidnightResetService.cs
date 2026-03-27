using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Services;

public class MidnightResetService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MidnightResetService> _logger;

    public MidnightResetService(IServiceProvider serviceProvider, ILogger<MidnightResetService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private readonly string _lastResetFilePath = Path.Combine(AppContext.BaseDirectory, "last_reset.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Midnight Reset Service is starting.");

        // Check for missed reset on startup
        try
        {
            await CheckForMissedResetAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for missed reset on startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;

            _logger.LogInformation("Next reset scheduled in {Delay} at {Time}", delay, nextMidnight);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Perform reset
            await PerformResetAsync(stoppingToken);
        }
    }

    private async Task CheckForMissedResetAsync(CancellationToken cancellationToken)
    {
        var lastResetDate = await GetLastResetDateAsync();
        var today = DateTime.Now.Date;

        if (today > lastResetDate)
        {
            _logger.LogInformation("Midnight reset was missed (Last reset: {LastReset}, Today: {Today}). Performing catch-up reset.", lastResetDate, today);
            
            // For catch-up, we archive as "yesterday" relative to the current time
            // since we don't know exactly when the coins were earned if multiple days were missed.
            await PerformResetAsync(cancellationToken);
        }
    }

    private async Task<DateTime> GetLastResetDateAsync()
    {
        if (File.Exists(_lastResetFilePath))
        {
            var content = await File.ReadAllTextAsync(_lastResetFilePath);
            if (DateTime.TryParse(content, out var lastReset))
            {
                return lastReset.Date;
            }
        }
        
        // If file doesn't exist, assume today so we don't trigger a massive reset on first run
        // unless there are coins, but PerformResetAsync handles accountability.
        return DateTime.Now.Date;
    }

    private async Task SaveLastResetDateAsync(DateTime date)
    {
        await File.WriteAllTextAsync(_lastResetFilePath, date.ToString("yyyy-MM-dd"));
    }

    private async Task PerformResetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting midnight reset and coin archival.");
        
        using var scope = _serviceProvider.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var archivalDate = DateTime.Now.Date.AddDays(-1);
        var accountsWithCoins = await db.Accounts.Where(a => a.CoinsToday > 0).ToListAsync(cancellationToken);

        if (accountsWithCoins.Any())
        {
            foreach (var account in accountsWithCoins)
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
                _logger.LogInformation("Successfully archived coins and reset daily totals for {Count} accounts.", accountsWithCoins.Count);
                
                // Save reset date after successful DB update
                await SaveLastResetDateAsync(DateTime.Now.Date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save changes during midnight reset.");
            }
        }
        else
        {
            _logger.LogInformation("No accounts had coins to archive today.");
            // Even if no coins, mark the day as reset so we don't keep checking on startup
            await SaveLastResetDateAsync(DateTime.Now.Date);
        }
    }
}
