using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileService.Models;

[Table("vlozhenie")]
public class FileAttachment
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }  

    [Column("post_id")]
    public Guid PostId { get; set; }

    [Column("file_url")]
    public string FileUrl { get; set; } = string.Empty;

    [Column("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [Column("file_name")]
    public string FileName { get; set; } = string.Empty;

    [Column("file_size")]
    public string FileSize { get; set; } = string.Empty;

    [Column("file_type")]
    public string FileType { get; set; } = string.Empty;

    [Column("upload_time")]
    public DateTime UploadTime { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("file_hash")]
    public string FileHash { get; set; } = string.Empty;

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("status")]
    public string Status { get; set; } = "completed";

    [Column("original_size")]
    public long? OriginalSize { get; set; }

    [Column("is_compressed")]
    public bool IsCompressed { get; set; }

    [Column("image_optimized")]
    public bool IsImageOptimized { get; set; }
}