using Microsoft.EntityFrameworkCore;
using UeerService.Models;

namespace UeerService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UserInfo> UserInfos { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserInfo>(entity =>
        {
            entity.HasKey(e => e.user_id);
            entity.Property(e => e.user_id).ValueGeneratedNever(); // ID приходит из AuthService
            entity.Property(e => e.full_name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.bio).HasDefaultValue("Привет, я в ТЧК!");
            entity.Property(e => e.last_activity_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.updated_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.user_id);
            entity.Property(e => e.user_id).ValueGeneratedNever();
            entity.Property(e => e.notifications_enabled).HasDefaultValue(true);
            entity.Property(e => e.theme).HasDefaultValue("light");
            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.updated_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}