using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;
using TikTokTracker.Web.Services;
using Xunit;

namespace TikTokTracker.Tests;

public class SystemSettingsServiceTests
{
    private readonly Mock<IDbContextFactory<AppDbContext>> _dbFactoryMock;
    private readonly Mock<ILogger<SystemSettingsService>> _loggerMock;
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public SystemSettingsServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        _dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_dbOptions));

        _loggerMock = new Mock<ILogger<SystemSettingsService>>();
    }

    private SystemSettingsService CreateService(Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();

        return new SystemSettingsService(_dbFactoryMock.Object, config, _loggerMock.Object);
    }

    [Fact]
    public async Task GetTikTokSessionIdAsync_ShouldReturnValueFromDb_WhenPresent()
    {
        // Arrange
        await using (var db = new AppDbContext(_dbOptions))
        {
            db.SystemSettings.Add(new SystemSetting { Key = "TikTokSessionId", Value = "db-session-id-123" });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.GetTikTokSessionIdAsync();

        // Assert
        Assert.Equal("db-session-id-123", result);
    }

    [Fact]
    public async Task GetTikTokSessionIdAsync_ShouldFallBackToConfig_WhenNotInDb()
    {
        // Arrange — no DB entry
        var service = CreateService(new Dictionary<string, string?>
        {
            { "TIKTOK_SESSION_ID", "config-session-id-456" }
        });

        // Act
        var result = await service.GetTikTokSessionIdAsync();

        // Assert
        Assert.Equal("config-session-id-456", result);
    }

    [Fact]
    public async Task GetTikTokSessionIdAsync_ShouldReturnEmptyString_WhenNoDbAndNoConfig()
    {
        var service = CreateService();

        var result = await service.GetTikTokSessionIdAsync();

        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetTikTokSessionIdAsync_ShouldFallBackToConfig_WhenDbValueIsWhitespace()
    {
        // Arrange — DB entry with whitespace value
        await using (var db = new AppDbContext(_dbOptions))
        {
            db.SystemSettings.Add(new SystemSetting { Key = "TikTokSessionId", Value = "   " });
            await db.SaveChangesAsync();
        }

        var service = CreateService(new Dictionary<string, string?>
        {
            { "TIKTOK_SESSION_ID", "config-fallback" }
        });

        // Act
        var result = await service.GetTikTokSessionIdAsync();

        // Assert
        Assert.Equal("config-fallback", result);
    }

    [Fact]
    public async Task UpdateTikTokSessionIdAsync_ShouldInsert_WhenNoExistingEntry()
    {
        var service = CreateService();

        // Act
        await service.UpdateTikTokSessionIdAsync("new-session-id");

        // Assert
        await using var db = new AppDbContext(_dbOptions);
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "TikTokSessionId");
        Assert.NotNull(setting);
        Assert.Equal("new-session-id", setting.Value);
    }

    [Fact]
    public async Task UpdateTikTokSessionIdAsync_ShouldUpdate_WhenEntryExists()
    {
        // Arrange
        await using (var db = new AppDbContext(_dbOptions))
        {
            db.SystemSettings.Add(new SystemSetting { Key = "TikTokSessionId", Value = "old-id" });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        await service.UpdateTikTokSessionIdAsync("updated-id");

        // Assert
        await using var db2 = new AppDbContext(_dbOptions);
        var settings = await db2.SystemSettings.Where(s => s.Key == "TikTokSessionId").ToListAsync();
        Assert.Single(settings);
        Assert.Equal("updated-id", settings[0].Value);
    }
}
