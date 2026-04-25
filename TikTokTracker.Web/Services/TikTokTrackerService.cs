using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Linq;
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
    private readonly Dictionary<string, (string Username, string Nickname)> _userIdCache = new();
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
            var accounts = await db.Accounts.OrderBy(a => a.Username).ToListAsync(stoppingToken);
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

                    // Populate user ID cache from top gifters
                    _userIdCache.Clear();
                    foreach (var g in topGifters)
                    {
                        if (g.SenderUsername != "unknown")
                        {
                            _userIdCache[g.SenderUserId] = (g.SenderUsername, g.SenderNickname);
                        }
                    }
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
                if (!_giftBuffer.IsEmpty && (DateTime.UtcNow - _lastFlushTime > TimeSpan.FromSeconds(15)))
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
        var accounts = await db.Accounts.OrderBy(a => a.Username).ToListAsync(cancellationToken);
        
        // Fetch active recordings from the microservice
        var activeRecordings = await _recorderClient.GetActiveRecordingsAsync();
        
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
                StartGiftTracking(account, cancellationToken);
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

            // Set recording status (not persisted to DB)
            account.IsRecording = activeRecordings.Any(r => r.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase));

            // New: Trigger recording if AutoRecord is enabled and account is live
            if (account.AutoRecord && isLive && !account.IsRecording)
            {
                // Fire and forget recording trigger
                _ = _recorderClient.StartRecordingAsync(account.Username);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        lock (_cacheLock)
        {
            _cachedAccounts.Clear();
            _cachedAccounts.AddRange(accounts);
        }
    }

    public async Task UpdateAccountAutoRecordAsync(int accountId, bool autoRecord)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account != null)
        {
            account.AutoRecord = autoRecord;
            await db.SaveChangesAsync();
            
            // Immediately update the cache to prevent race conditions with UI refresh
            lock (_cacheLock)
            {
                var cached = _cachedAccounts.FirstOrDefault(a => a.Id == accountId);
                if (cached != null)
                {
                    cached.AutoRecord = autoRecord;
                }
            }
            _logger.LogInformation("Updated AutoRecord for Account {Id} to {Status}", accountId, autoRecord);
        }
    }

    public async Task<(bool Success, string Message)> AddAccountAsync(string username)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.Accounts.AnyAsync(a => a.Username == username))
        {
            return (false, $"@{username} is already being tracked.");
        }

        var account = new TikTokAccount { Username = username };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        // Immediately update the cache
        lock (_cacheLock)
        {
            _cachedAccounts.Add(account);
        }

        _logger.LogInformation("Added new account: @{Username}", username);
        return (true, $"@{username} added!");
    }

    public async Task<bool> RemoveAccountAsync(int accountId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account != null)
        {
            db.Accounts.Remove(account);
            await db.SaveChangesAsync();

            // Immediately update the cache
            lock (_cacheLock)
            {
                var cached = _cachedAccounts.FirstOrDefault(a => a.Id == accountId);
                if (cached != null)
                {
                    _cachedAccounts.Remove(cached);
                }
            }

            _logger.LogInformation("Removed account ID: {Id}", accountId);
            return true;
        }
        return false;
    }

    public async Task<List<DailyCoinEarning>> GetDailyCoinEarningsAsync(int? limit = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.DailyCoinEarnings
            .Include(e => e.Account)
            .OrderByDescending(e => e.Date);
            
        if (limit.HasValue)
        {
            return await query.Take(limit.Value).ToListAsync();
        }
        
        return await query.ToListAsync();
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

    private void StartGiftTracking(TikTokAccount account, CancellationToken stoppingToken)
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
                var streakId = Guid.NewGuid().ToString();

                // Record initial gift for both UI and DB
                var uiTransaction = RecordGift(accountId, (int)currentAmount, e.Gift.Name, diamondCost, e.Sender.IdString, e.Sender.UniqueId, e.Sender.NickName, streakId, true);

                e.OnAmountChanged += (gift, change, newAmount) =>
                {
                    if (change > 0)
                    {
                        // Update UI transaction (fix name if resolved, update amount)
                        UpdateUiGift(uiTransaction, (int)newAmount, e.Sender.IdString, e.Sender.UniqueId, e.Sender.NickName);

                        // Record delta for DB accounting (silent, don't add to UI feed)
                        RecordGift(accountId, (int)change, e.Gift.Name, diamondCost, e.Sender.IdString, e.Sender.UniqueId, e.Sender.NickName, streakId, false);
                    }
                };
            }
        };

        // TryAdd is the definitive guard — if another racing call already added this username, bail out
        if (!_liveClients.TryAdd(username, client))
        {
            _logger.LogDebug("Gift tracking already active for @{Username}, skipping duplicate start.", username);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await client.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Gift tracking stopped for @{Username} (app shutdown).", username);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gift tracking WebSocket disconnected for @{Username}", username);
            }
            finally
            {
                _liveClients.TryRemove(username, out _);
            }
        });

        _logger.LogInformation("Started gift tracking WebSocket for @{Username}", username);
    }

    private GiftTransaction RecordGift(int accountId, int amount, string giftName, int diamondCost, string senderUserId, string senderUniqueId, string senderNickname, string? streakId, bool addToCache)
    {
        var transaction = new GiftTransaction
        {
            TikTokAccountId = accountId,
            SenderUserId = senderUserId,
            SenderUsername = senderUniqueId,
            SenderNickname = senderNickname,
            GiftName = giftName,
            Amount = amount,
            DiamondCost = diamondCost,
            Timestamp = DateTime.UtcNow,
            StreakId = streakId
        };

        // Attempt robust mapping if name is unknown
        if (transaction.SenderUsername == "unknown")
        {
            lock (_cacheLock)
            {
                if (_userIdCache.TryGetValue(senderUserId, out var cached))
                {
                    transaction.SenderUsername = cached.Username;
                    transaction.SenderNickname = cached.Nickname;
                }
            }
        }
        else
        {
            // Update cache with known good names
            lock (_cacheLock)
            {
                _userIdCache[senderUserId] = (senderUniqueId, senderNickname);
            }
        }

        _giftBuffer.Enqueue(transaction);

        if (addToCache)
        {
            // Update recent gifts cache immediately
            lock (_cacheLock)
            {
                _cachedRecentGifts.Insert(0, transaction);
                if (_cachedRecentGifts.Count > 50) _cachedRecentGifts.RemoveAt(50);
            }
        }

        if (_giftBuffer.Count >= MaxBufferSize)
        {
            _ = Task.Run(async () => {
                await using var db = await _dbFactory.CreateDbContextAsync();
                await FlushGiftsAsync(db, CancellationToken.None);
            });
        }
        
        return transaction;
    }

    private void UpdateUiGift(GiftTransaction uiTransaction, int newAmount, string senderUserId, string currentUsername, string currentNickname)
    {
        lock (_cacheLock)
        {
            uiTransaction.Amount = newAmount;
            
            // If the name is now known, update it
            if ((uiTransaction.SenderUsername == "unknown" || string.IsNullOrEmpty(uiTransaction.SenderUsername)) && 
                !string.IsNullOrEmpty(currentUsername) && currentUsername != "unknown")
            {
                uiTransaction.SenderUsername = currentUsername;
                uiTransaction.SenderNickname = currentNickname;
                
                // Also update the ID cache for future use
                _userIdCache[senderUserId] = (currentUsername, currentNickname);
            }
        }
    }

    public virtual async Task ManualFlushAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await FlushGiftsAsync(db, ct);
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
                if (acc != null) acc.CoinsToday += total.Total;
            }

            await db.SaveChangesAsync(cancellationToken);

            // Aggregate summaries for Postgres Upsert
            var summaries = giftsToSave
                .GroupBy(g => new { g.TikTokAccountId, g.SenderUserId })
                .Select(group => new
                {
                    AccountId = group.Key.TikTokAccountId,
                    UserId = group.Key.SenderUserId,
                    Username = group.OrderByDescending(x => x.SenderUsername != "unknown" ? 1 : 0).First().SenderUsername,
                    Nickname = group.OrderByDescending(x => x.SenderNickname != "unknown" ? 1 : 0).First().SenderNickname,
                    TotalDiamonds = group.Sum(g => g.TotalDiamonds),
                    TotalGifts = group.Sum(g => g.Amount),
                    LastGift = group.Max(g => g.Timestamp)
                });

            const string upsertSql = @"
                INSERT INTO ""GifterSummaries"" 
                    (""TikTokAccountId"", ""SenderUserId"", ""SenderUsername"", ""SenderNickname"", ""TotalDiamonds"", ""TotalGifts"", ""LastGiftTime"")
                VALUES 
                    (@p0, @p1, @p2, @p3, @p4, @p5, @p6)
                ON CONFLICT (""TikTokAccountId"", ""SenderUserId"") 
                DO UPDATE SET 
                    ""TotalDiamonds"" = ""GifterSummaries"".""TotalDiamonds"" + EXCLUDED.""TotalDiamonds"",
                    ""TotalGifts"" = ""GifterSummaries"".""TotalGifts"" + EXCLUDED.""TotalGifts"",
                    ""LastGiftTime"" = EXCLUDED.""LastGiftTime"",
                    ""SenderUsername"" = CASE WHEN EXCLUDED.""SenderUsername"" != 'unknown' THEN EXCLUDED.""SenderUsername"" ELSE ""GifterSummaries"".""SenderUsername"" END,
                    ""SenderNickname"" = CASE WHEN EXCLUDED.""SenderNickname"" != 'unknown' THEN EXCLUDED.""SenderNickname"" ELSE ""GifterSummaries"".""SenderNickname"" END;";

            foreach (var s in summaries)
            {
                await db.Database.ExecuteSqlRawAsync(upsertSql, 
                    new object[] { s.AccountId, s.UserId, s.Username, s.Nickname, s.TotalDiamonds, s.TotalGifts, s.LastGift }, 
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
