using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UeerService.Models;

[Table("user_info")]
public class UserInfo
{
    [Key]
    [Column("user_id")]
    public Guid user_id { get; set; }

    [Column("full_name")]
    public string full_name { get; set; } = string.Empty;

    [Column("avatar_url")]
    public string? avatar_url { get; set; }

    [Column("bio")]
    public string? bio { get; set; }

    [Column("last_activity_at")]
    public DateTime last_activity_at { get; set; }

    [Column("is_active")]
    public bool is_active { get; set; }

    [Column("is_blocked")]
    public bool is_blocked { get; set; }

    [Column("email")]
    public string email { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime created_at { get; set; }

    [Column("updated_at")]
    public DateTime updated_at { get; set; }
}