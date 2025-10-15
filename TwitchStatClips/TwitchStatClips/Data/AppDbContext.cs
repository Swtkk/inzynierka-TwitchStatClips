using System.Collections.Generic;
using System.Reflection.Emit;
using TwitchStatClips.Models;
using Microsoft.EntityFrameworkCore;

namespace TwitchStatClips.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

        public DbSet<FavoriteClip> Favorites => Set<FavoriteClip>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<FavoriteClip>()
             .HasIndex(x => new { x.UserId, x.ClipId })
             .IsUnique();

            b.Entity<FavoriteClip>()
             .Property(x => x.CreatedAt)
             .HasDefaultValueSql("SYSUTCDATETIME()");
        }
    }
}
