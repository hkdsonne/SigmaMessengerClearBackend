using Microsoft.EntityFrameworkCore;
using FileService.Models;

namespace FileService.Data;

public class FileDbContext : DbContext
{
    public FileDbContext(DbContextOptions<FileDbContext> options) : base(options) { }

    public DbSet<FileAttachment> Vlozhenie { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileAttachment>(entity =>
        {
            entity.ToTable("vlozhenie");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.UploadTime).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.PostId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.FileHash).IsUnique();
            entity.HasIndex(e => e.Status);
        });
    }
}