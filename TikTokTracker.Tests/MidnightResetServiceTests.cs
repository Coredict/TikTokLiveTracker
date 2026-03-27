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
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly Mock<IDbContextFactory<AppDbContext>> _dbFactoryMock;
    private readonly DbContextOptions<AppDbContext> _dbContextOptions;

    public MidnightResetServiceTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<MidnightResetService>>();
        _systemClockMock = new Mock<ISystemClock>();
        _fileSystemMock = new Mock<IFileSystem>();
        _dbFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();

        _dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_dbContextOptions));

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        _serviceProviderMock.Setup(s => s.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);
        _serviceProviderMock.Setup(s => s.GetService(typeof(IDbContextFactory<AppDbContext>)))
            .Returns(_dbFactoryMock.Object);
        _serviceProviderMock.Setup(s => s.GetService(typeof(TikTokTrackerService)))
            .Returns((TikTokTrackerService?)null);
    }

    [Fact]
    public async Task PerformResetAsync_ShouldArchiveCoinsAndResetTotals()
    {
        // Arrange
        var today = new DateTime(2026, 3, 27);
        _systemClockMock.Setup(c => c.Now).Returns(today);
        _systemClockMock.Setup(c => c.Today).Returns(today.Date);

        using (var db = new AppDbContext(_dbContextOptions))
        {
            var account = new TikTokAccount { Id = 1, Username = "testuser", CoinsToday = 100 };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
        }

        var service = new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _fileSystemMock.Object);

        // Act
        var method = typeof(MidnightResetService).GetMethod("PerformResetAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        try
        {
            var task = method.Invoke(service, new object[] { CancellationToken.None }) as Task;
            Assert.NotNull(task);
            await task;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }

        // Assert
        using (var db = new AppDbContext(_dbContextOptions))
        {
            var account = await db.Accounts.FindAsync(1);
            Assert.NotNull(account);
            Assert.Equal(0, account.CoinsToday);
            var archival = await db.DailyCoinEarnings.FirstOrDefaultAsync(e => e.TikTokAccountId == 1);
            Assert.NotNull(archival);
            Assert.Equal(100, archival.Coins);
            Assert.Equal(today.Date.AddDays(-1), archival.Date);
        }
        
        _fileSystemMock.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), today.ToString("yyyy-MM-dd")), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTriggerReset_WhenDayChanges()
    {
        // Arrange
        var lastReset = new DateTime(2026, 3, 26);
        var now = new DateTime(2026, 3, 27, 0, 0, 1);
        
        _systemClockMock.Setup(c => c.Now).Returns(now);
        _systemClockMock.Setup(c => c.Today).Returns(now.Date);
        
        // Mock file system to say last reset was yesterday
        _fileSystemMock.Setup(f => f.Exists(It.IsAny<string>())).Returns(true);
        _fileSystemMock.Setup(f => f.ReadAllTextAsync(It.IsAny<string>())).ReturnsAsync("2026-03-26");

        var service = new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _fileSystemMock.Object);

        using var cts = new CancellationTokenSource();
        
        // Act
        // We run ExecuteAsync but cancel it immediately after it starts
        var method = typeof(MidnightResetService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var executeTask = method.Invoke(service, new object[] { cts.Token }) as Task;
        Assert.NotNull(executeTask);

        await Task.Delay(50); // Give it a bit of time to run one loop
        cts.Cancel();
        await executeTask;

        // Assert
        // Verify that PerformResetAsync logic was triggered (e.g. by checking file write)
        _fileSystemMock.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), now.ToString("yyyy-MM-dd")), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotTriggerReset_WhenDayIsSame()
    {
        // Arrange
        var now = new DateTime(2026, 3, 27, 10, 0, 0);
        
        _systemClockMock.Setup(c => c.Now).Returns(now);
        _systemClockMock.Setup(c => c.Today).Returns(now.Date);
        
        // Mock file system to say last reset was TODAY
        _fileSystemMock.Setup(f => f.Exists(It.IsAny<string>())).Returns(true);
        _fileSystemMock.Setup(f => f.ReadAllTextAsync(It.IsAny<string>())).ReturnsAsync("2026-03-27");

        var service = new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _fileSystemMock.Object);

        using var cts = new CancellationTokenSource();
        
        // Act
        var method = typeof(MidnightResetService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var executeTask = method.Invoke(service, new object[] { cts.Token }) as Task;
        Assert.NotNull(executeTask);

        await Task.Delay(50);
        cts.Cancel();
        await executeTask;

        // Assert
        // Verify that PerformResetAsync logic was NOT triggered (no file write for today)
        _fileSystemMock.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PerformResetAsync_ShouldArchiveZeroCoins_WhenNoCoinsEarned()
    {
        // Arrange
        var today = new DateTime(2026, 3, 27);
        _systemClockMock.Setup(c => c.Now).Returns(today);
        _systemClockMock.Setup(c => c.Today).Returns(today.Date);

        using (var db = new AppDbContext(_dbContextOptions))
        {
            // Add an account with 0 coins
            db.Accounts.Add(new TikTokAccount { Id = 3, Username = "zeroUser", CoinsToday = 0 });
            await db.SaveChangesAsync();
        }

        var service = new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _fileSystemMock.Object);

        // Act
        var method = typeof(MidnightResetService).GetMethod("PerformResetAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = method.Invoke(service, new object[] { CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task;

        // Assert
        using (var db = new AppDbContext(_dbContextOptions))
        {
            var archival = await db.DailyCoinEarnings.FirstOrDefaultAsync(e => e.TikTokAccountId == 3);
            Assert.NotNull(archival);
            Assert.Equal(0, archival.Coins);
            Assert.Equal(today.Date.AddDays(-1), archival.Date);
        }
        
        // Should still update the last reset file
        _fileSystemMock.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), today.ToString("yyyy-MM-dd")), Times.Once);
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

        var service = new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _fileSystemMock.Object);

        // Act
        var method = typeof(MidnightResetService).GetMethod("PerformResetAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = method.Invoke(service, new object[] { CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task;

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

        using (var db = new AppDbContext(_dbContextOptions))
        {
            db.Accounts.Add(new TikTokAccount { Id = 2, Username = "user2", CoinsToday = 50 });
            await db.SaveChangesAsync();
        }

        var service = new MidnightResetService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _systemClockMock.Object,
            _fileSystemMock.Object);

        // Act
        var method = typeof(MidnightResetService).GetMethod("PerformResetAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = method.Invoke(service, new object[] { CancellationToken.None }) as Task;
        Assert.NotNull(task);
        await task;

        // Assert
        // Reset should still complete even if flush failed
        using (var db = new AppDbContext(_dbContextOptions))
        {
            var account = await db.Accounts.FindAsync(2);
            Assert.NotNull(account);
            Assert.Equal(0, account.CoinsToday);
            Assert.True(await db.DailyCoinEarnings.AnyAsync(e => e.TikTokAccountId == 2));
        }
        _fileSystemMock.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), today.ToString("yyyy-MM-dd")), Times.Once);
    }
}
