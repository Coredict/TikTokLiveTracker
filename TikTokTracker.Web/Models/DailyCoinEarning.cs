using System;

namespace TikTokTracker.Web.Models;

public class DailyCoinEarning
{
    public int Id { get; set; }
    public int TikTokAccountId { get; set; }
    public TikTokAccount? Account { get; set; }
    public DateTime Date { get; set; }
    public int Coins { get; set; }
}
