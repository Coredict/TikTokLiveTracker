using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using TikTokLiveSharp.Client;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Services;

public class TikTokTrackerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TikTokTrackerService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    
    // Tracks active WebSocket gift listeners
    private readonly ConcurrentDictionary<string, TikTokLiveClient> _liveClients = new();

    public TikTokTrackerService(
        IServiceProvider serviceProvider,
        ILogger<TikTokTrackerService> logger,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("TikTok");
        _dbFactory = dbFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllAccountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App is shutting down gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TikTok polling loop.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollAllAccountsAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Accounts.ToListAsync(cancellationToken);

        // Clean up WebSocket clients for accounts no longer in DB
        var knownUsernames = accounts.Select(a => a.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var user in _liveClients.Keys.Where(k => !knownUsernames.Contains(k)).ToList())
        {
            if (_liveClients.TryRemove(user, out var c)) try { _ = c.Stop(); } catch { }
        }

        foreach (var account in accounts)
        {
            var (isLive, viewerCount) = await CheckIsLiveAsync(account.Username);

            if (isLive && !_liveClients.ContainsKey(account.Username))
            {
                // Account just went live – connect WebSocket to track gifts
                StartGiftTracking(account.Username);
            }
            else if (!isLive && _liveClients.TryRemove(account.Username, out var client))
            {
                try { _ = client.Stop(); } catch { }
            }

            // Update online status and viewer count in DB
            if (account.IsOnline != isLive || account.ViewerCount != viewerCount)
            {
                account.IsOnline = isLive;
                account.ViewerCount = isLive ? viewerCount : 0;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches TikTok's embedded page JSON (SIGI_STATE) and extracts live status and viewer count.
    /// </summary>
    private async Task<(bool IsLive, int ViewerCount)> CheckIsLiveAsync(string username)
    {
        try
        {
            var url = $"https://www.tiktok.com/@{username}/live";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (false, 0);

            var html = await response.Content.ReadAsStringAsync();

            // TikTok embeds a JSON state object in the page — find liveRoom.status and liveRoom.liveRoomStats.userCount
            var sigiMatch = System.Text.RegularExpressions.Regex.Match(
                html,
                @"<script id=""SIGI_STATE""[^>]*>(.*?)</script>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (sigiMatch.Success)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(sigiMatch.Groups[1].Value);
                if (doc.RootElement.TryGetProperty("LiveRoom", out var liveRoomSection))
                {
                    foreach (var prop in liveRoomSection.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            prop.Value.TryGetProperty("liveRoom", out var liveRoom) &&
                            liveRoom.TryGetProperty("status", out var statusEl))
                        {
                            var status = statusEl.GetInt32();
                            bool isLive = status == 2;
                            int viewerCount = 0;
                            
                            if (isLive && liveRoom.TryGetProperty("liveRoomStats", out var statsEl) && statsEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                if (statsEl.TryGetProperty("userCount", out var userCountEl))
                                {
                                    viewerCount = userCountEl.GetInt32();
                                }
                            }

                            _logger.LogInformation("@{Username}: liveRoom.status={Status} (live={IsLive}, viewers={ViewerCount})", username, status, isLive, viewerCount);
                            return (isLive, viewerCount);
                        }
                    }
                }
            }

            // Fallback: check for "liveRoom":{"status":2 pattern in raw HTML
            bool fallback = html.Contains(@"""liveRoom"":{") && System.Text.RegularExpressions.Regex.IsMatch(html, @"""status""\s*:\s*2");
            _logger.LogInformation("@{Username}: fallback live check = {IsLive}", username, fallback);
            return (fallback, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check live status for @{Username}", username);
            return (false, 0);
        }
    }

    private void StartGiftTracking(string username)
    {
        var client = new TikTokLiveClient(uniqueID: username);

        client.OnGift += (sender, e) =>
        {
            if (e != null && e.Gift != null)
            {
                var diamondCost = e.Gift.DiamondCost > 0 ? e.Gift.DiamondCost : 1;
                var currentAmount = e.Amount > 0 ? (int)e.Amount : 1;
                
                RecordGift(username, (int)currentAmount, e.Gift.Name, diamondCost, e.Sender.UniqueId, e.Sender.NickName);

                e.OnAmountChanged += (gift, change, newAmount) =>
                {
                    if (change > 0)
                    {
                        RecordGift(username, (int)change, e.Gift.Name, diamondCost, e.Sender.UniqueId, e.Sender.NickName);
                    }
                };
            }
        };

        _liveClients.TryAdd(username, client);

        Task.Run(async () =>
        {
            try
            {
                await client.RunAsync(new CancellationToken());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gift tracking WebSocket disconnected for @{Username}", username);
                _liveClients.TryRemove(username, out _);
            }
        });

        _logger.LogInformation("Started gift tracking WebSocket for @{Username}", username);
    }

    private void RecordGift(string targetUsername, int amount, string giftName, int diamondCost, string senderUniqueId, string senderNickname)
    {
        Task.Run(async () =>
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == targetUsername);
                if (account != null)
                {
                    // Update total coins
                    account.CurrentCoins += (amount * diamondCost);

                    // Record transaction
                    var transaction = new GiftTransaction
                    {
                        TikTokAccountId = account.Id,
                        SenderUsername = senderUniqueId,
                        SenderNickname = senderNickname,
                        GiftName = giftName,
                        Amount = amount,
                        DiamondCost = diamondCost,
                        Timestamp = DateTime.UtcNow
                    };
                    db.Gifts.Add(transaction);

                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record gift for {Username} from {Sender}", targetUsername, senderUniqueId);
            }
        });
    }
}
