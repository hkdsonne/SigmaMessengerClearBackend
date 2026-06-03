using System.ComponentModel.DataAnnotations.Schema;

namespace ChatService.Models;

[Table("chat")]
public class Chat
{
    public Guid chat_id { get; set; } = Guid.NewGuid();
    public string tip { get; set; } = "private";
    public string? nazvanie { get; set; }
    public string? opisanie { get; set; }
    public string? avatar_url { get; set; }
    public Guid? user_id { get; set; }
    public DateTime created_at { get; set; }
    public DateTime updated_at { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Participant> Participants { get; set; } = new List<Participant>();
}