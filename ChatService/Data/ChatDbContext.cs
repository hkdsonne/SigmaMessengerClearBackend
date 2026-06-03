using ChatService.Models;
using Microsoft.EntityFrameworkCore;
namespace ChatService.Data;
public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    public DbSet<Chat> Chats { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Participant> Participants { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
    public DbSet<Reaction> Reactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Chat
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.chat_id);
            entity.HasIndex(e => e.tip);
            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.updated_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Message
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => e.chat_id);
            entity.HasIndex(e => e.sender_id);
            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.updated_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.is_blocked).HasDefaultValue(false);

            entity.HasOne(m => m.Chat)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(m => m.chat_id)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.ReplyTo)
                  .WithMany(m => m.Replies)
                  .HasForeignKey(m => m.reply_to_id)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Participant (uchastnik)
        modelBuilder.Entity<Participant>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => new { e.chat_id, e.user_id }).IsUnique();
            entity.Property(e => e.data_vstuplenia).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.rol).HasDefaultValue("member");

            entity.HasOne(p => p.Chat)
                  .WithMany(c => c.Participants)
                  .HasForeignKey(p => p.chat_id)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Attachment (vlozenia)
        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.vremya_zagruzki).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(a => a.Message)
                  .WithMany(m => m.Attachments)
                  .HasForeignKey(a => a.message_id)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Reaction (m_reaction)
        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => new { e.message_id, e.user_id, e.tip }).IsUnique();
            entity.Property(e => e.vremya_postavili).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(r => r.Message)
                  .WithMany(m => m.Reactions)
                  .HasForeignKey(r => r.message_id)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}