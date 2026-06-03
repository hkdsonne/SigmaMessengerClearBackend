namespace UeerService.DTOs;

public class UserProfileDto
{
    public string username { get; set; } = string.Empty;
    public string? avatar_url { get; set; }
    public string? bio { get; set; }
    public DateTime last_activity_at { get; set; }
}