namespace TikTokTracker.Web.Models;

public class TikTokAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public int CurrentCoins { get; set; }
    public int ViewerCount { get; set; }
    public string? ProfileImageUrl { get; set; }
    
    public string StreamUrl => $"https://www.tiktok.com/@{Username}/live";
}
