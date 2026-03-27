using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Data;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Services;

public class SystemSettingsService : ISystemSettingsService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemSettingsService> _logger;
    private const string TikTokSessionIdKey = "TikTokSessionId";

    public SystemSettingsService(
        IDbContextFactory<AppDbContext> dbFactory,
        IConfiguration configuration,
        ILogger<SystemSettingsService> logger)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetTikTokSessionIdAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var setting = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == TikTokSessionIdKey);

        if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            return setting.Value;
        }

        // Fallback to configuration
        var configValue = _configuration["TIKTOK_SESSION_ID"] ?? "";
        _logger.LogInformation("TikTok Session ID not found in DB, falling back to configuration.");
        return configValue;
    }

    public async Task UpdateTikTokSessionIdAsync(string sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var setting = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == TikTokSessionIdKey);

        if (setting == null)
        {
            setting = new SystemSetting { Key = TikTokSessionIdKey, Value = sessionId };
            db.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = sessionId;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Updated TikTok Session ID in database.");
    }
}
