
using System.ComponentModel.DataAnnotations.Schema;
namespace ChatService.Models;

[Table("vlozenia")]
public class Attachment
{
   
    public Guid id { get; set; }

    public Guid message_id { get; set; }
    public string file_url { get; set; } = string.Empty;

    public string file_name { get; set; } = string.Empty;
    public long file_size { get; set; }
    public string tip_faila { get; set; } = string.Empty;

    public DateTime vremya_zagruzki { get; set; }

    public Message? Message { get; set; }
}