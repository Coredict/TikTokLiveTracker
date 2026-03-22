using Microsoft.EntityFrameworkCore;
using TikTokTracker.Web.Models;

namespace TikTokTracker.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TikTokAccount> Accounts { get; set; } = null!;
}
