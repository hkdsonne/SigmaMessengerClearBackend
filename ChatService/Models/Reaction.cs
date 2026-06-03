// Models/Reaction.cs
using System.ComponentModel.DataAnnotations.Schema;
namespace ChatService.Models;

[Table("m_reaction")]
public class Reaction
{
    public Guid id { get; set; }

    public Guid message_id { get; set; }
    public Guid user_id { get; set; }

    public string tip { get; set; } = string.Empty;

    public DateTime vremya_postavili { get; set; }

    public Message? Message { get; set; }
}