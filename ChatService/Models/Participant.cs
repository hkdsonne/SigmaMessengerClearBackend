// Models/Participant.cs
using System.ComponentModel.DataAnnotations.Schema;
namespace ChatService.Models;

[Table("uchastnik")]
public class Participant
{
    public Guid id { get; set; } = Guid.NewGuid();
    public Guid chat_id { get; set; }
    public Guid user_id { get; set; }
    public string username { get; set; } = String.Empty;
    public string rol { get; set; } = "member";
    public DateTime data_vstuplenia { get; set; }
    public Guid poslednee_prochitannoe { get; set; }

    public Chat? Chat { get; set; }
}