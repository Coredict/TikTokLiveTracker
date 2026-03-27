namespace TikTokTracker.Web.Services;

public interface ISystemSettingsService
{
    Task<string> GetTikTokSessionIdAsync();
    Task UpdateTikTokSessionIdAsync(string sessionId);
}
