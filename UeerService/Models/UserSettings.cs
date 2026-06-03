using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UeerService.Models;

[Table("user_settings")]
public class UserSettings
{
    [Key]
    [Column("user_id")]
    public Guid user_id { get; set; }

    [Column("notifications_enabled")]
    public bool notifications_enabled { get; set; }

    [Column("theme")]
    public string theme { get; set; } = "light";

    [Column("created_at")]
    public DateTime created_at { get; set; }

    [Column("updated_at")]
    public DateTime updated_at { get; set; }
}