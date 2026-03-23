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
    private readonly RecorderClient _recorderClient;
    
    // Cache for UI
    private readonly List<TikTokAccount> _cachedAccounts = new();
    private readonly List<GiftTransaction> _cachedRecentGifts = new();
    private readonly List<GifterSummary> _cachedTopGifters = new();
    private readonly object _cacheLock = new();

    // Tracks active WebSocket gift listeners
    private readonly ConcurrentDictionary<string, TikTokLiveClient> _liveClients = new();
    private DateTime _lastCleanupTime = DateTime.MinValue;
    
    // Batch processing buffer
    private readonly ConcurrentQueue<GiftTransaction> _giftBuffer = new();
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private const int MaxBufferSize = 500;

    public IReadOnlyList<TikTokAccount> CachedAccounts { get { lock(_cacheLock) return _cachedAccounts.ToList(); } }
    public IReadOnlyList<GiftTransaction> CachedRecentGifts { get { lock(_cacheLock) return _cachedRecentGifts.ToList(); } }
    public IReadOnlyList<GifterSummary> CachedTopGifters { get { lock(_cacheLock) return _cachedTopGifters.ToList(); } }

    public TikTokTrackerService(
        IServiceProvider serviceProvider,
        ILogger<TikTokTrackerService> logger,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbFactory,
        RecorderClient recorderClient)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("TikTok");
        _dbFactory = dbFactory;
        _recorderClient = recorderClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial cache population
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
            var accounts = await db.Accounts.ToListAsync(stoppingToken);
            var recentGifts = await db.Gifts.Include(g => g.Account).OrderByDescending(g => g.Timestamp).Take(50).ToListAsync(stoppingToken);
            var topGifters = await db.GifterSummaries.Include(g => g.Account).OrderByDescending(g => g.TotalDiamonds).Take(50).ToListAsync(stoppingToken);
            
            lock (_cacheLock)
            {
                _cachedAccounts.Clear();
                _cachedAccounts.AddRange(accounts);
                _cachedRecentGifts.Clear();
                _cachedRecentGifts.AddRange(recentGifts);
                _cachedTopGifters.Clear();
                _cachedTopGifters.AddRange(topGifters);
            }
            _logger.LogInformation("Initial cache populated: {Acc} accounts, {Gifts} gifts, {Gifters} gifters.", accounts.Count, recentGifts.Count, topGifters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate initial cache.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllAccountsAsync(stoppingToken);

                // Periodic flush of gifts
                if (!_giftBuffer.IsEmpty && (DateTime.UtcNow - _lastFlushTime > TimeSpan.FromMinutes(1)))
                {
                    await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
                    await FlushGiftsAsync(db, stoppingToken);
                }
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
        
        // Only cleanup once per hour to reduce SQL noise
        if (DateTime.UtcNow - _lastCleanupTime > TimeSpan.FromHours(1))
        {
            await CleanupExpiredGiftsAsync(db, cancellationToken);
            _lastCleanupTime = DateTime.UtcNow;
        }

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
                StartGiftTracking(account);
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

            // New: Trigger recording if AutoRecord is enabled and account is live
            if (account.AutoRecord && isLive)
            {
                // Fire and forget recording trigger
                _ = _recorderClient.StartRecordingAsync(account.Username);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Update cache
        lock (_cacheLock)
        {
            _cachedAccounts.Clear();
            _cachedAccounts.AddRange(accounts);
        }
    }

    private async Task CleanupExpiredGiftsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var expiry = DateTime.UtcNow.AddHours(-24);
            var expiredCount = await db.Gifts
                .Where(g => g.Timestamp < expiry)
                .ExecuteDeleteAsync(cancellationToken);

            if (expiredCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} expired gift transactions older than 24h.", expiredCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up expired gifts.");
        }
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

    private void StartGiftTracking(TikTokAccount account)
    {
        var username = account.Username;
        var accountId = account.Id;
        var client = new TikTokLiveClient(uniqueID: username);

        client.OnGift += (sender, e) =>
        {
            if (e != null && e.Gift != null)
            {
                var diamondCost = e.Gift.DiamondCost > 0 ? e.Gift.DiamondCost : 1;
                var currentAmount = e.Amount > 0 ? (int)e.Amount : 1;
                
                RecordGift(accountId, (int)currentAmount, e.Gift.Name, diamondCost, e.Sender.UniqueId, e.Sender.NickName);

                e.OnAmountChanged += (gift, change, newAmount) =>
                {
                    if (change > 0)
                    {
                        RecordGift(accountId, (int)change, e.Gift.Name, diamondCost, e.Sender.UniqueId, e.Sender.NickName);
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

    private void RecordGift(int accountId, int amount, string giftName, int diamondCost, string senderUniqueId, string senderNickname)
    {
        var transaction = new GiftTransaction
        {
            TikTokAccountId = accountId,
            SenderUsername = senderUniqueId,
            SenderNickname = senderNickname,
            GiftName = giftName,
            Amount = amount,
            DiamondCost = diamondCost,
            Timestamp = DateTime.UtcNow
        };

        _giftBuffer.Enqueue(transaction);

        // Update recent gifts cache immediately
        lock (_cacheLock)
        {
            _cachedRecentGifts.Insert(0, transaction);
            if (_cachedRecentGifts.Count > 50) _cachedRecentGifts.RemoveAt(50);
        }

        if (_giftBuffer.Count >= MaxBufferSize)
        {
            _ = Task.Run(async () => {
                await using var db = await _dbFactory.CreateDbContextAsync();
                await FlushGiftsAsync(db, CancellationToken.None);
            });
        }
    }

    private async Task FlushGiftsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (_giftBuffer.IsEmpty) return;

        var giftsToSave = new List<GiftTransaction>();
        while (_giftBuffer.TryDequeue(out var gift))
        {
            giftsToSave.Add(gift);
        }

        if (!giftsToSave.Any()) return;

        try
        {
            // Update individual gift transactions
            await db.Gifts.AddRangeAsync(giftsToSave, cancellationToken);
            
            // Update Account CurrentCoins in batch
            var accountTotals = giftsToSave
                .GroupBy(g => g.TikTokAccountId)
                .Select(g => new { Id = g.Key, Total = g.Sum(x => x.TotalDiamonds) });

            foreach (var total in accountTotals)
            {
                var acc = await db.Accounts.FindAsync(new object[] { total.Id }, cancellationToken);
                if (acc != null) acc.CurrentCoins += total.Total;
            }

            await db.SaveChangesAsync(cancellationToken);

            // Aggregate summaries for Postgres Upsert
            var summaries = giftsToSave
                .GroupBy(g => new { g.TikTokAccountId, g.SenderUsername })
                .Select(group => new
                {
                    AccountId = group.Key.TikTokAccountId,
                    Username = group.Key.SenderUsername,
                    Nickname = group.First().SenderNickname,
                    TotalDiamonds = group.Sum(g => g.TotalDiamonds),
                    TotalGifts = group.Sum(g => g.Amount),
                    LastGift = group.Max(g => g.Timestamp)
                });

            const string upsertSql = @"
                INSERT INTO ""GifterSummaries"" 
                    (""TikTokAccountId"", ""SenderUsername"", ""SenderNickname"", ""TotalDiamonds"", ""TotalGifts"", ""LastGiftTime"")
                VALUES 
                    (@p0, @p1, @p2, @p3, @p4, @p5)
                ON CONFLICT (""TikTokAccountId"", ""SenderUsername"") 
                DO UPDATE SET 
                    ""TotalDiamonds"" = ""GifterSummaries"".""TotalDiamonds"" + EXCLUDED.""TotalDiamonds"",
                    ""TotalGifts"" = ""GifterSummaries"".""TotalGifts"" + EXCLUDED.""TotalGifts"",
                    ""LastGiftTime"" = EXCLUDED.""LastGiftTime"",
                    ""SenderNickname"" = EXCLUDED.""SenderNickname"";";

            foreach (var s in summaries)
            {
                await db.Database.ExecuteSqlRawAsync(upsertSql, 
                    new object[] { s.AccountId, s.Username, s.Nickname, s.TotalDiamonds, s.TotalGifts, s.LastGift }, 
                    cancellationToken);
            }

            _lastFlushTime = DateTime.UtcNow;
            _logger.LogInformation("Batch Flush: {Count} gifts, {SummaryCount} summaries updated.", giftsToSave.Count, summaries.Count());

            // Refresh top gifters cache from DB after flush
            var topGifters = await db.GifterSummaries
                .Include(g => g.Account)
                .OrderByDescending(g => g.TotalDiamonds)
                .Take(50)
                .ToListAsync(cancellationToken);
            
            lock (_cacheLock)
            {
                _cachedTopGifters.Clear();
                _cachedTopGifters.AddRange(topGifters);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch flush.");
        }
    }
}
