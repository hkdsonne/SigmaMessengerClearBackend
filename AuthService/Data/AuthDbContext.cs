using Microsoft.EntityFrameworkCore;
using AuthService.Models;

namespace AuthService.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<User> users { get; set; }
    public DbSet<Session> sessions { get; set; }
    public DbSet<UserMail> users_mail { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => e.username).IsUnique();
            entity.Property(e => e.username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.password).IsRequired();
            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.updated_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => e.session_hash).IsUnique();
            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(s => s.User)
                  .WithMany(u => u.Sessions)
                  .HasForeignKey(s => s.user_id)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserMail>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => e.user_id);
            entity.HasIndex(e => e.email);

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.user_id)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}