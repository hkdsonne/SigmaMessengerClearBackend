
using System.ComponentModel.DataAnnotations.Schema;
namespace ChatService.Models;

[Table("message")]
public class Message
{
    public Guid id { get; set; } = Guid.NewGuid();
    public Guid chat_id { get; set; }
    public Guid sender_id { get; set; }
    public string username { get; set; }
    public Guid? reply_to_id { get; set; }
    public string content { get; set; } = string.Empty;
    public string content_type { get; set; } = "text";
    public bool is_deleted { get; set; }
    public bool is_blocked { get; set; }
    public DateTime created_at { get; set; }
    public DateTime updated_at { get; set; }

    public Chat? Chat { get; set; }
    public Message? ReplyTo { get; set; }
    public ICollection<Message> Replies { get; set; } = new List<Message>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();
}