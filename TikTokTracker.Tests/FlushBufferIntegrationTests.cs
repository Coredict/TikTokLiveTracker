using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;
using TikTokTracker.Web.Services;
using Xunit;

namespace TikTokTracker.Tests;

/// <summary>
/// Integration tests for the gift buffer flush pipeline (ManualFlushAsync, FlushAndHoldLockAsync).
/// Uses reflection to access the private _giftBuffer to enqueue test data.
///
/// Note: The Postgres upsert SQL (GifterSummaries) will throw with InMemoryDatabase
/// and is swallowed by the catch block — those summaries won't update. These tests
/// focus on: gift persistence, CoinsToday accumulation, lock semantics, and cache sync.
/// </summary>
public class FlushBufferIntegrationTests : IDisposable
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly Mock<IDbContextFactory<AppDbContext>> _dbFactoryMock;
    private readonly TikTokTrackerService _service;
    private readonly HttpClient _factoryHttpClient;
    private readonly HttpClient _recorderHttpClient;

    public FlushBufferIntegrationTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        _dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_dbOptions));
        _dbFactoryMock.Setup(f => f.CreateDbContext())
            .Returns(() => new AppDbContext(_dbOptions));

        _factoryHttpClient = new HttpClient();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_factoryHttpClient);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
        _recorderHttpClient = new HttpClient();
        var recorderClient = new RecorderClient(
            _recorderHttpClient,
            new Mock<ILogger<RecorderClient>>().Object,
            configMock.Object);

        _service = new TikTokTrackerService(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<TikTokTrackerService>>().Object,
            httpClientFactoryMock.Object,
            _dbFactoryMock.Object,
            recorderClient);
    }

    public void Dispose()
    {
        (_service as IDisposable)?.Dispose();
        _factoryHttpClient.Dispose();
        _recorderHttpClient.Dispose();
    }

    /// <summary>
    /// Gets the private _giftBuffer via reflection so we can enqueue test data.
    /// </summary>
    private ConcurrentQueue<GiftTransaction> GetGiftBuffer()
    {
        var field = typeof(TikTokTrackerService)
            .GetField("_giftBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var buffer = field.GetValue(_service) as ConcurrentQueue<GiftTransaction>;
        Assert.NotNull(buffer);
        return buffer;
    }

    private async Task SeedAccountAsync(int id, string username, int coinsToday = 0)
    {
        await using var db = new AppDbContext(_dbOptions);
        db.Accounts.Add(new TikTokAccount { Id = id, Username = username, CoinsToday = coinsToday });
        await db.SaveChangesAsync();

        // Also populate the cache so FlushGiftsInternalAsync can sync it
        _service.RefreshAccountCache();
    }

    // --- ManualFlushAsync ---

    [Fact]
    public async Task ManualFlush_ShouldPersistGiftsToDatabase()
    {
        await SeedAccountAsync(1, "streamer1");

        var buffer = GetGiftBuffer();
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1,
            SenderUserId = "sender1",
            SenderUsername = "gifter1",
            SenderNickname = "Gifter One",
            GiftName = "Rose",
            Amount = 3,
            DiamondCost = 1,
            Timestamp = DateTime.UtcNow
        });

        await _service.ManualFlushAsync();

        await using var db = new AppDbContext(_dbOptions);
        var gifts = await db.Gifts.ToListAsync();
        Assert.Single(gifts);
        Assert.Equal("Rose", gifts[0].GiftName);
        Assert.Equal(3, gifts[0].Amount);
        Assert.Equal(1, gifts[0].DiamondCost);
        Assert.Equal("gifter1", gifts[0].SenderUsername);
    }

    [Fact]
    public async Task ManualFlush_ShouldAccumulateCoinsToday()
    {
        await SeedAccountAsync(1, "coinstreamer", coinsToday: 100);

        var buffer = GetGiftBuffer();
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1,
            SenderUserId = "s1",
            SenderUsername = "user1",
            SenderNickname = "User One",
            GiftName = "Star",
            Amount = 5,
            DiamondCost = 10, // TotalDiamonds = 50
            Timestamp = DateTime.UtcNow
        });

        await _service.ManualFlushAsync();

        await using var db = new AppDbContext(_dbOptions);
        var account = await db.Accounts.FindAsync(1);
        Assert.NotNull(account);
        Assert.Equal(150, account.CoinsToday); // 100 + 50
    }

    [Fact]
    public async Task ManualFlush_MultipleGifts_ShouldBatchAccumulateCoins()
    {
        await SeedAccountAsync(1, "batchstreamer");

        var buffer = GetGiftBuffer();
        // Gift 1: 2 * 5 = 10 diamonds
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s1", SenderUsername = "u1",
            SenderNickname = "n1", GiftName = "Rose", Amount = 2, DiamondCost = 5,
            Timestamp = DateTime.UtcNow
        });
        // Gift 2: 3 * 10 = 30 diamonds
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s2", SenderUsername = "u2",
            SenderNickname = "n2", GiftName = "Star", Amount = 3, DiamondCost = 10,
            Timestamp = DateTime.UtcNow
        });

        await _service.ManualFlushAsync();

        await using var db = new AppDbContext(_dbOptions);
        var account = await db.Accounts.FindAsync(1);
        Assert.NotNull(account);
        Assert.Equal(40, account.CoinsToday); // 10 + 30
        Assert.Equal(2, await db.Gifts.CountAsync());
    }

    [Fact]
    public async Task ManualFlush_MultipleAccounts_ShouldAccumulateSeparately()
    {
        await SeedAccountAsync(1, "streamerA", coinsToday: 10);
        await SeedAccountAsync(2, "streamerB", coinsToday: 20);

        var buffer = GetGiftBuffer();
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s1", SenderUsername = "u1",
            SenderNickname = "n1", GiftName = "Rose", Amount = 1, DiamondCost = 100,
            Timestamp = DateTime.UtcNow
        });
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 2, SenderUserId = "s2", SenderUsername = "u2",
            SenderNickname = "n2", GiftName = "Star", Amount = 1, DiamondCost = 200,
            Timestamp = DateTime.UtcNow
        });

        await _service.ManualFlushAsync();

        await using var db = new AppDbContext(_dbOptions);
        var accountA = await db.Accounts.FindAsync(1);
        var accountB = await db.Accounts.FindAsync(2);
        Assert.Equal(110, accountA!.CoinsToday); // 10 + 100
        Assert.Equal(220, accountB!.CoinsToday); // 20 + 200
    }

    [Fact]
    public async Task ManualFlush_EmptyBuffer_ShouldDoNothing()
    {
        await SeedAccountAsync(1, "emptystreamer", coinsToday: 50);

        // Don't enqueue anything
        await _service.ManualFlushAsync();

        await using var db = new AppDbContext(_dbOptions);
        var account = await db.Accounts.FindAsync(1);
        Assert.Equal(50, account!.CoinsToday); // Unchanged
        Assert.Equal(0, await db.Gifts.CountAsync());
    }

    [Fact]
    public async Task ManualFlush_ShouldPersistCoinsToDatabaseEvenWhenUpsertFails()
    {
        // The Postgres upsert SQL (GifterSummaries) throws with InMemoryDatabase,
        // but the gifts and CoinsToday are committed via SaveChangesAsync before the
        // SQL call. The cache sync at the end of FlushGiftsInternalAsync won't run
        // because it's in the same try block after the failing SQL — but the DB
        // state is correct. This test verifies that the important data is persisted
        // even when the downstream upsert fails.
        await SeedAccountAsync(1, "cachecoins");

        var buffer = GetGiftBuffer();
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s1", SenderUsername = "u1",
            SenderNickname = "n1", GiftName = "Rose", Amount = 4, DiamondCost = 25,
            Timestamp = DateTime.UtcNow
        });

        await _service.ManualFlushAsync();

        // DB should have the correct CoinsToday
        await using var db = new AppDbContext(_dbOptions);
        var account = await db.Accounts.FindAsync(1);
        Assert.NotNull(account);
        Assert.Equal(100, account.CoinsToday); // 4 * 25

        // Gift transaction should be persisted
        Assert.Single(await db.Gifts.ToListAsync());
    }

    // --- FlushAndHoldLockAsync ---

    [Fact]
    public async Task FlushAndHoldLock_ShouldReturnDisposable_ThatReleasesLock()
    {
        await SeedAccountAsync(1, "lockstreamer");

        var buffer = GetGiftBuffer();
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s1", SenderUsername = "u1",
            SenderNickname = "n1", GiftName = "Rose", Amount = 1, DiamondCost = 1,
            Timestamp = DateTime.UtcNow
        });

        var lockHandle = await _service.FlushAndHoldLockAsync();
        Assert.NotNull(lockHandle);

        // While lock is held, a second ManualFlush should block.
        // We verify by trying with a short timeout — it should NOT complete immediately.
        var flushTask = Task.Run(() => _service.ManualFlushAsync());
        var completedInTime = await Task.WhenAny(flushTask, Task.Delay(200)) == flushTask;
        Assert.False(completedInTime, "ManualFlush should be blocked while lock is held");

        // Release the lock
        lockHandle.Dispose();

        // Now the blocked flush should complete
        await Task.WhenAny(flushTask, Task.Delay(2000));
        Assert.True(flushTask.IsCompleted, "ManualFlush should complete after lock is released");
    }

    [Fact]
    public async Task FlushAndHoldLock_ShouldFlushGiftsBeforeHoldingLock()
    {
        await SeedAccountAsync(1, "flushlockstreamer");

        var buffer = GetGiftBuffer();
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s1", SenderUsername = "u1",
            SenderNickname = "n1", GiftName = "Rose", Amount = 2, DiamondCost = 5,
            Timestamp = DateTime.UtcNow
        });

        using var lockHandle = await _service.FlushAndHoldLockAsync();

        // Gifts should be flushed even though lock is held
        await using var db = new AppDbContext(_dbOptions);
        Assert.Single(await db.Gifts.ToListAsync());
        Assert.Equal(10, (await db.Accounts.FindAsync(1))!.CoinsToday);
    }

    // --- Sequential flush correctness ---

    [Fact]
    public async Task ManualFlush_CalledTwice_ShouldAccumulateCoinsCorrectly()
    {
        await SeedAccountAsync(1, "doubleflush");

        var buffer = GetGiftBuffer();

        // First batch
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s1", SenderUsername = "u1",
            SenderNickname = "n1", GiftName = "Rose", Amount = 1, DiamondCost = 10,
            Timestamp = DateTime.UtcNow
        });
        await _service.ManualFlushAsync();

        // Second batch
        buffer.Enqueue(new GiftTransaction
        {
            TikTokAccountId = 1, SenderUserId = "s2", SenderUsername = "u2",
            SenderNickname = "n2", GiftName = "Star", Amount = 1, DiamondCost = 20,
            Timestamp = DateTime.UtcNow
        });
        await _service.ManualFlushAsync();

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(30, (await db.Accounts.FindAsync(1))!.CoinsToday); // 10 + 20
        Assert.Equal(2, await db.Gifts.CountAsync());
    }
}
