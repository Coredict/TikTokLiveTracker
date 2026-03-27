namespace TikTokTracker.Web.Services;

public interface ISystemClock
{
    DateTime Now { get; }
    DateTime Today => Now.Date;
}
