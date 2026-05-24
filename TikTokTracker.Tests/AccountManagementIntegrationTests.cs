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
/// Integration tests that exercise account management flows through TikTokTrackerService
/// with a real in-memory database, verifying data persistence and cache consistency.
/// </summary>
public class AccountManagementIntegrationTests : IDisposable
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly Mock<IDbContextFactory<AppDbContext>> _dbFactoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<TikTokTrackerService>> _loggerMock;
    private readonly HttpClient _factoryHttpClient;
    private readonly HttpClient _recorderHttpClient;
    private readonly RecorderClient _recorderClient;
    private readonly TikTokTrackerService _service;

    public AccountManagementIntegrationTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        _dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_dbOptions));
        _dbFactoryMock.Setup(f => f.CreateDbContext())
            .Returns(() => new AppDbContext(_dbOptions));

        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<TikTokTrackerService>>();

        _factoryHttpClient = new HttpClient();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_factoryHttpClient);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
        _recorderHttpClient = new HttpClient();
        _recorderClient = new RecorderClient(
            _recorderHttpClient,
            new Mock<ILogger<RecorderClient>>().Object,
            configMock.Object);

        _service = new TikTokTrackerService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            httpClientFactoryMock.Object,
            _dbFactoryMock.Object,
            _recorderClient);
    }

    public void Dispose()
    {
        (_service as IDisposable)?.Dispose();
        _factoryHttpClient.Dispose();
        _recorderHttpClient.Dispose();
    }

    // --- AddAccount ---

    [Fact]
    public async Task AddAccount_ShouldPersistToDatabase()
    {
        var (success, message) = await _service.AddAccountAsync("newuser");

        Assert.True(success);
        Assert.Contains("newuser", message);

        await using var db = new AppDbContext(_dbOptions);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "newuser");
        Assert.NotNull(account);
    }

    [Fact]
    public async Task AddAccount_ShouldUpdateCache()
    {
        await _service.AddAccountAsync("cacheduser");

        var cached = _service.CachedAccounts;
        Assert.Contains(cached, a => a.Username == "cacheduser");
    }

    [Fact]
    public async Task AddAccount_ShouldRejectDuplicateUsername()
    {
        await _service.AddAccountAsync("dupeuser");
        var (success, message) = await _service.AddAccountAsync("dupeuser");

        Assert.False(success);
        Assert.Contains("already", message);
    }

    [Fact]
    public async Task AddAccount_CaseSensitiveDuplicateCheck_ShouldAllowDifferentCase()
    {
        // The production code uses == (case-sensitive). Verify this behavior.
        var (success1, _) = await _service.AddAccountAsync("TestUser");
        var (success2, _) = await _service.AddAccountAsync("testuser");

        // Both should succeed because == is case-sensitive
        Assert.True(success1);
        Assert.True(success2);

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(2, await db.Accounts.CountAsync());
    }

    [Fact]
    public async Task AddAccount_EmptyUsername_ShouldStillSucceed()
    {
        // The service doesn't validate username content — verify it doesn't crash
        var (success, _) = await _service.AddAccountAsync("");
        Assert.True(success);

        await using var db = new AppDbContext(_dbOptions);
        Assert.Single(await db.Accounts.Where(a => a.Username == "").ToListAsync());
    }

    [Fact]
    public async Task AddAccount_WhitespaceUsername_ShouldStillSucceed()
    {
        var (success, _) = await _service.AddAccountAsync("   ");
        Assert.True(success);

        await using var db = new AppDbContext(_dbOptions);
        Assert.Single(await db.Accounts.Where(a => a.Username == "   ").ToListAsync());
    }

    [Fact]
    public async Task AddAccount_MultipleAccounts_ShouldAllBeTracked()
    {
        await _service.AddAccountAsync("user1");
        await _service.AddAccountAsync("user2");
        await _service.AddAccountAsync("user3");

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(3, await db.Accounts.CountAsync());
        Assert.Equal(3, _service.CachedAccounts.Count);
    }

    // --- RemoveAccount ---

    [Fact]
    public async Task RemoveAccount_ShouldDeleteFromDatabase()
    {
        await _service.AddAccountAsync("removeme");
        var account = (await GetAccountFromDbAsync("removeme"))!;

        var result = await _service.RemoveAccountAsync(account.Id);

        Assert.True(result);
        await using var db = new AppDbContext(_dbOptions);
        Assert.Null(await db.Accounts.FindAsync(account.Id));
    }

    [Fact]
    public async Task RemoveAccount_ShouldUpdateCache()
    {
        await _service.AddAccountAsync("removeFromCache");
        var account = (await GetAccountFromDbAsync("removeFromCache"))!;

        await _service.RemoveAccountAsync(account.Id);

        Assert.DoesNotContain(_service.CachedAccounts, a => a.Username == "removeFromCache");
    }

    [Fact]
    public async Task RemoveAccount_ShouldReturnFalse_WhenAccountDoesNotExist()
    {
        var result = await _service.RemoveAccountAsync(99999);
        Assert.False(result);
    }

    // --- UpdateAutoRecord ---

    [Fact]
    public async Task UpdateAutoRecord_ShouldPersistToDatabase()
    {
        await _service.AddAccountAsync("autorecuser");
        var account = (await GetAccountFromDbAsync("autorecuser"))!;
        Assert.False(account.AutoRecord);

        await _service.UpdateAccountAutoRecordAsync(account.Id, true);

        await using var db = new AppDbContext(_dbOptions);
        var updated = await db.Accounts.FindAsync(account.Id);
        Assert.NotNull(updated);
        Assert.True(updated.AutoRecord);
    }

    [Fact]
    public async Task UpdateAutoRecord_ShouldUpdateCache()
    {
        await _service.AddAccountAsync("autorecCached");
        var account = (await GetAccountFromDbAsync("autorecCached"))!;

        await _service.UpdateAccountAutoRecordAsync(account.Id, true);

        var cached = _service.CachedAccounts.FirstOrDefault(a => a.Id == account.Id);
        Assert.NotNull(cached);
        Assert.True(cached.AutoRecord);
    }

    [Fact]
    public async Task UpdateAutoRecord_ToggleOnAndOff_ShouldWork()
    {
        await _service.AddAccountAsync("toggleuser");
        var account = (await GetAccountFromDbAsync("toggleuser"))!;

        await _service.UpdateAccountAutoRecordAsync(account.Id, true);
        Assert.True(_service.CachedAccounts.First(a => a.Id == account.Id).AutoRecord);

        await _service.UpdateAccountAutoRecordAsync(account.Id, false);
        Assert.False(_service.CachedAccounts.First(a => a.Id == account.Id).AutoRecord);
    }

    [Fact]
    public async Task UpdateAutoRecord_ShouldDoNothing_WhenAccountDoesNotExist()
    {
        // Should not throw
        await _service.UpdateAccountAutoRecordAsync(99999, true);
    }

    // --- GetDailyCoinEarnings ---

    [Fact]
    public async Task GetDailyCoinEarnings_ShouldReturnEarningsOrderedByDate()
    {
        // Arrange
        await using (var db = new AppDbContext(_dbOptions))
        {
            var account = new TikTokAccount { Username = "earner" };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            db.DailyCoinEarnings.AddRange(
                new DailyCoinEarning { TikTokAccountId = account.Id, Date = new DateTime(2026, 3, 25), Coins = 100 },
                new DailyCoinEarning { TikTokAccountId = account.Id, Date = new DateTime(2026, 3, 26), Coins = 200 },
                new DailyCoinEarning { TikTokAccountId = account.Id, Date = new DateTime(2026, 3, 27), Coins = 300 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var earnings = await _service.GetDailyCoinEarningsAsync(limit: 30);

        // Assert
        Assert.Equal(3, earnings.Count);
        Assert.Equal(300, earnings[0].Coins); // Most recent first
        Assert.Equal(200, earnings[1].Coins);
        Assert.Equal(100, earnings[2].Coins);
    }

    [Fact]
    public async Task GetDailyCoinEarnings_ShouldRespectLimit()
    {
        await using (var db = new AppDbContext(_dbOptions))
        {
            var account = new TikTokAccount { Username = "limituser" };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            for (int i = 0; i < 10; i++)
            {
                db.DailyCoinEarnings.Add(new DailyCoinEarning
                {
                    TikTokAccountId = account.Id,
                    Date = new DateTime(2026, 3, 1).AddDays(i),
                    Coins = i * 100
                });
            }
            await db.SaveChangesAsync();
        }

        var earnings = await _service.GetDailyCoinEarningsAsync(limit: 3);

        Assert.Equal(3, earnings.Count);
    }

    [Fact]
    public async Task GetDailyCoinEarnings_ShouldReturnAll_WhenLimitIsNull()
    {
        await using (var db = new AppDbContext(_dbOptions))
        {
            var account = new TikTokAccount { Username = "alluser" };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            for (int i = 0; i < 5; i++)
            {
                db.DailyCoinEarnings.Add(new DailyCoinEarning
                {
                    TikTokAccountId = account.Id,
                    Date = new DateTime(2026, 3, 1).AddDays(i),
                    Coins = 50
                });
            }
            await db.SaveChangesAsync();
        }

        var earnings = await _service.GetDailyCoinEarningsAsync(limit: null);

        Assert.Equal(5, earnings.Count);
    }

    // --- RefreshAccountCache ---

    [Fact]
    public async Task RefreshAccountCache_ShouldReloadFromDatabase()
    {
        // Add account via service (populates cache)
        await _service.AddAccountAsync("refreshuser");

        // Modify DB directly (simulating midnight reset)
        await using (var db = new AppDbContext(_dbOptions))
        {
            var account = await db.Accounts.FirstAsync(a => a.Username == "refreshuser");
            account.CoinsToday = 999;
            await db.SaveChangesAsync();
        }

        // Cache should still show old value
        var cachedBefore = _service.CachedAccounts.First(a => a.Username == "refreshuser");
        Assert.Equal(0, cachedBefore.CoinsToday);

        // Force refresh
        _service.RefreshAccountCache();

        // Now cache should reflect DB
        var cachedAfter = _service.CachedAccounts.First(a => a.Username == "refreshuser");
        Assert.Equal(999, cachedAfter.CoinsToday);
    }

    // --- Full flow ---

    [Fact]
    public async Task FullFlow_AddAccountThenRemove_ShouldLeaveCleanState()
    {
        await _service.AddAccountAsync("flowuser");
        Assert.Single(_service.CachedAccounts);

        var account = (await GetAccountFromDbAsync("flowuser"))!;
        await _service.RemoveAccountAsync(account.Id);

        Assert.Empty(_service.CachedAccounts);

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(0, await db.Accounts.CountAsync());
    }

    private async Task<TikTokAccount?> GetAccountFromDbAsync(string username)
    {
        await using var db = new AppDbContext(_dbOptions);
        return await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);
    }
}
