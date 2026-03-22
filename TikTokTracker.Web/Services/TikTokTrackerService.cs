using Microsoft.EntityFrameworkCore;
using TikTokLiveSharp;
using TikTokLiveSharp.Client;
using TikTokLiveSharp.Events;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;
using System.Collections.Concurrent;

namespace TikTokTracker.Web.Services;

public class TikTokTrackerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TikTokTrackerService> _logger;
    private readonly ConcurrentDictionary<string, TikTokLiveClient> _clients = new();

    public TikTokTrackerService(IServiceProvider serviceProvider, ILogger<TikTokTrackerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAccountsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing TikTok accounts.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task SyncAccountsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var accounts = await db.Accounts.ToListAsync(cancellationToken);
        var accountUsernames = accounts.Select(a => a.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove clients that are no longer in the DB
        var toRemove = _clients.Keys.Where(k => !accountUsernames.Contains(k)).ToList();
        foreach (var user in toRemove)
        {
            if (_clients.TryRemove(user, out var client))
            {
                try { client.Stop(); } catch { }
            }
        }

        // Add clients that are new in the DB and try starting them
        foreach (var account in accounts)
        {
            if (!_clients.ContainsKey(account.Username))
            {
                StartTracking(account.Username);
            }
        }
    }

    private void StartTracking(string username)
    {
        var client = new TikTokLiveClient(username);

        client.OnConnected += (sender, e) => UpdateAccountStatus(username, isOnline: true);
        client.OnDisconnected += (sender, e) => UpdateAccountStatus(username, isOnline: false);
        client.OnGiftRecieved += (sender, e) =>
        {
            if (e != null)
            {
                var repeat = e.repeatCount > 0 ? e.repeatCount : 1;
                AddCoins(username, repeat);
            }
        };

        _clients.TryAdd(username, client);

        Task.Run(() =>
        {
            try
            {
                client.Run(new CancellationToken());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not start tracking for {username}. They might be offline.");
                UpdateAccountStatus(username, isOnline: false);
                _clients.TryRemove(username, out _);
            }
        });
    }

    private void UpdateAccountStatus(string username, bool isOnline)
    {
        Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);
                if (account != null)
                {
                    account.IsOnline = isOnline;
                    if (!isOnline)
                    {
                        // Optional: Reset views/coins when offline? Or keep it for historical stat?
                        // Let's keep it for now.
                        account.ViewerCount = 0;
                    }
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for {username}", username);
            }
        });
    }

    private void AddCoins(string username, int coinsToAdd)
    {
        Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);
                if (account != null)
                {
                    account.CurrentCoins += coinsToAdd;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add coins for {username}", username);
            }
        });
    }
}
