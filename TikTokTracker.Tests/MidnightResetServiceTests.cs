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
            .Returns((TikTokTrackerService)null);
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
        try
        {
            await (Task)method.Invoke(service, new object[] { CancellationToken.None });
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException;
        }

        // Assert
        using (var db = new AppDbContext(_dbContextOptions))
        {
            var account = await db.Accounts.FindAsync(1);
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
        var executeTask = typeof(MidnightResetService)
            .GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(service, new object[] { cts.Token }) as Task;

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
        var executeTask = typeof(MidnightResetService)
            .GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(service, new object[] { cts.Token }) as Task;

        await Task.Delay(50);
        cts.Cancel();
        await executeTask;

        // Assert
        // Verify that PerformResetAsync logic was NOT triggered (no file write for today)
        _fileSystemMock.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
