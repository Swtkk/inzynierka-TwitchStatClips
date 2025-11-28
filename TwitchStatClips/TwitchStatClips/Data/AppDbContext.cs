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
        public DbSet<GetStats> GetStats => Set<GetStats>();
        public DbSet<LatestAvatarPerChannel> LatestAvatarPerChannel => Set<LatestAvatarPerChannel>();
        public DbSet<StreamGamesList> StreamGamesList => Set<StreamGamesList>();
        public DbSet<GetFollowers> GetFollowers => Set<GetFollowers>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<FavoriteClip>()
             .HasIndex(x => new { x.UserId, x.ClipId })
             .IsUnique();

            b.Entity<FavoriteClip>()
             .Property(x => x.CreatedAt)
             .HasDefaultValueSql("SYSUTCDATETIME()");

            b.Entity<GetStats>().HasNoKey();
            b.Entity<LatestAvatarPerChannel>().HasNoKey();
            b.Entity<StreamGamesList>().HasNoKey();
            b.Entity<GetFollowers>().HasNoKey();
        }
    }
}
