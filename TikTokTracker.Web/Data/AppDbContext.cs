using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TikTokAccount> Accounts { get; set; } = null!;
    public DbSet<GiftTransaction> Gifts { get; set; } = null!;
    public DbSet<GifterSummary> GifterSummaries { get; set; } = null!;
    public DbSet<DailyCoinEarning> DailyCoinEarnings { get; set; } = null!;
    public DbSet<SystemSetting> SystemSettings { get; set; } = null!;
}
