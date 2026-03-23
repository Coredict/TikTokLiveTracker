using System;
using System.ComponentModel.DataAnnotations;

namespace TikTokTracker.Web.Models;

public class GifterSummary
{
    public int Id { get; set; }

    public int TikTokAccountId { get; set; }
    public TikTokAccount? Account { get; set; }

    [Required]
    public string SenderUserId { get; set; } = string.Empty;

    [Required]
    public string SenderUsername { get; set; } = string.Empty;

    [Required]
    public string SenderNickname { get; set; } = string.Empty;

    public int TotalDiamonds { get; set; }
    public int TotalGifts { get; set; }
    public DateTime LastGiftTime { get; set; } = DateTime.UtcNow;
}
