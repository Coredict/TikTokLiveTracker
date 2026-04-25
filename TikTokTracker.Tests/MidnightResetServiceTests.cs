using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;
using TikTokTracker.Web.Services;
using Xunit;

namespace TikTokTracker.Tests;

public class MidnightResetServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<MidnightResetService>> _loggerMock;
    private readonly Mock<ISystemClock> _systemClockMock;
    private readonly Mock<IDbContextFactory<AppDbContext>> _dbFactoryMock;
    private readonly DbContextOptions<AppDbContext> _dbContextOptions;

    public MidnightResetServiceTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<MidnightResetService>>();
        _systemClockMock = new Mock<ISystemClock>();

        _dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        _dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_dbContextOptions));

        _serviceProviderMock.Setup(s => s.GetService(typeof(TikTokTrackerService)))
            .Returns((TikTokTrackerService?)null);
    }

    private MidnightResetService CreateService() =>
        new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _dbFactoryMock.Object);

    private static async Task InvokePerformResetAsync(MidnightResetService service, CancellationToken ct = default)
    {
        var method = typeof(MidnightResetService).GetMethod("PerformResetAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        try
        {
            var task = method.Invoke(service, new object[] { ct }) as Task;
            Assert.NotNull(task);
            await task;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    [Fact]
    public async Task PerformResetAsync_ShouldArchiveCoinsAndResetTotals()
    {
        // Arrange
        var today = new DateTime(2026, 3, 27);
        _systemClockMock.Setup(c => c.Now).Returns(today);
        _systemClockMock.Setup(c => c.Today).Returns(today.Date);

        await using (var db = new AppDbContext(_dbContextOptions))
        {
            db.Accounts.Add(new TikTokAccount { Id = 1, Username = "testuser", CoinsToday = 100 });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        await InvokePerformResetAsync(service);

        // Assert
        await using var db = new AppDbContext(_dbContextOptions);
        var account = await db.Accounts.FindAsync(1);
        Assert.NotNull(account);
        Assert.Equal(0, account.CoinsToday);

        var archival = await db.DailyCoinEarnings.FirstOrDefaultAsync(e => e.TikTokAccountId == 1);
        Assert.NotNull(archival);
        Assert.Equal(100, archival.Coins);
        Assert.Equal(today.Date.AddDays(-1), archival.Date);

        var resetSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LastResetDate");
        Assert.NotNull(resetSetting);
        Assert.Equal(today.ToString("yyyy-MM-dd"), resetSetting.Value);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTriggerReset_WhenDayChanges()
    {
        // Arrange
        var now = new DateTime(2026, 3, 27, 0, 0, 1);

        _systemClockMock.Setup(c => c.Now).Returns(now);
        _systemClockMock.Setup(c => c.Today).Returns(now.Date);

        // Seed last reset date as yesterday so a reset is due
        await using (var db = new AppDbContext(_dbContextOptions))
        {
            db.SystemSettings.Add(new SystemSetting { Key = "LastResetDate", Value = "2026-03-26" });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        using var cts = new CancellationTokenSource();

        // Act — run one loop iteration then cancel
        var method = typeof(MidnightResetService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var executeTask = method.Invoke(service, new object[] { cts.Token }) as Task;
        Assert.NotNull(executeTask);

        await Task.Delay(50);
        cts.Cancel();
        await executeTask;

        // Assert — LastResetDate should now be today
        await using var db = new AppDbContext(_dbContextOptions);
        var resetSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LastResetDate");
        Assert.NotNull(resetSetting);
        Assert.Equal(now.ToString("yyyy-MM-dd"), resetSetting.Value);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotTriggerReset_WhenDayIsSame()
    {
        // Arrange
        var now = new DateTime(2026, 3, 27, 10, 0, 0);

        _systemClockMock.Setup(c => c.Now).Returns(now);
        _systemClockMock.Setup(c => c.Today).Returns(now.Date);

        // Seed last reset date as today — no reset should occur
        await using (var db = new AppDbContext(_dbContextOptions))
        {
            db.SystemSettings.Add(new SystemSetting { Key = "LastResetDate", Value = "2026-03-27" });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        using var cts = new CancellationTokenSource();

        // Act
        var method = typeof(MidnightResetService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var executeTask = method.Invoke(service, new object[] { cts.Token }) as Task;
        Assert.NotNull(executeTask);

        await Task.Delay(50);
        cts.Cancel();
        await executeTask;

        // Assert — LastResetDate should still be "2026-03-27" (unchanged)
        await using var db = new AppDbContext(_dbContextOptions);
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LastResetDate");
        Assert.NotNull(setting);
        Assert.Equal("2026-03-27", setting.Value);
    }

    [Fact]
    public async Task PerformResetAsync_ShouldArchiveZeroCoins_WhenNoCoinsEarned()
    {
        // Arrange
        var today = new DateTime(2026, 3, 27);
        _systemClockMock.Setup(c => c.Now).Returns(today);
        _systemClockMock.Setup(c => c.Today).Returns(today.Date);

        await using (var db = new AppDbContext(_dbContextOptions))
        {
            db.Accounts.Add(new TikTokAccount { Id = 3, Username = "zeroUser", CoinsToday = 0 });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        await InvokePerformResetAsync(service);

        // Assert
        await using var db = new AppDbContext(_dbContextOptions);
        var archival = await db.DailyCoinEarnings.FirstOrDefaultAsync(e => e.TikTokAccountId == 3);
        Assert.NotNull(archival);
        Assert.Equal(0, archival.Coins);
        Assert.Equal(today.Date.AddDays(-1), archival.Date);

        var resetSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LastResetDate");
        Assert.NotNull(resetSetting);
        Assert.Equal(today.ToString("yyyy-MM-dd"), resetSetting.Value);
    }

    [Fact]
    public async Task PerformResetAsync_ShouldCallManualFlush_WhenTrackerServiceIsAvailable()
    {
        // Arrange
        var today = new DateTime(2026, 3, 27);
        _systemClockMock.Setup(c => c.Now).Returns(today);
        _systemClockMock.Setup(c => c.Today).Returns(today.Date);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var trackerMock = new Mock<TikTokTrackerService>(null!, null!, httpClientFactoryMock.Object, null!, null!);
        _serviceProviderMock.Setup(s => s.GetService(typeof(TikTokTrackerService)))
            .Returns(trackerMock.Object);

        var service = CreateService();

        // Act
        await InvokePerformResetAsync(service);

        // Assert
        trackerMock.Verify(t => t.ManualFlushAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PerformResetAsync_ShouldContinue_WhenManualFlushFails()
    {
        // Arrange
        var today = new DateTime(2026, 3, 27);
        _systemClockMock.Setup(c => c.Now).Returns(today);
        _systemClockMock.Setup(c => c.Today).Returns(today.Date);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var trackerMock = new Mock<TikTokTrackerService>(null!, null!, httpClientFactoryMock.Object, null!, null!);
        trackerMock.Setup(t => t.ManualFlushAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Flush failed"));

        _serviceProviderMock.Setup(s => s.GetService(typeof(TikTokTrackerService)))
            .Returns(trackerMock.Object);

        await using (var db = new AppDbContext(_dbContextOptions))
        {
            db.Accounts.Add(new TikTokAccount { Id = 2, Username = "user2", CoinsToday = 50 });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        await InvokePerformResetAsync(service);

        // Assert — reset should still complete even if flush failed
        await using var db = new AppDbContext(_dbContextOptions);
        var account = await db.Accounts.FindAsync(2);
        Assert.NotNull(account);
        Assert.Equal(0, account.CoinsToday);
        Assert.True(await db.DailyCoinEarnings.AnyAsync(e => e.TikTokAccountId == 2));

        var resetSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LastResetDate");
        Assert.NotNull(resetSetting);
        Assert.Equal(today.ToString("yyyy-MM-dd"), resetSetting.Value);
    }
}
