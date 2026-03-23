using System;
using System.ComponentModel.DataAnnotations;

namespace TikTokTracker.Web.Models;

public class GiftTransaction
{
    public int Id { get; set; }
    
    public int TikTokAccountId { get; set; }
    public TikTokAccount? Account { get; set; }

    [Required]
    public string SenderUsername { get; set; } = string.Empty;
    
    [Required]
    public string SenderNickname { get; set; } = string.Empty;

    [Required]
    public string GiftName { get; set; } = string.Empty;

    public int Amount { get; set; }
    public int DiamondCost { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public int TotalDiamonds => Amount * DiamondCost;
}
