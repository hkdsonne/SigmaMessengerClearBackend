namespace UeerService.DTOs;

public class UserSummaryDto
{
    public Guid user_id { get; set; }
    public string username { get; set; } = string.Empty;
    public string? avatar_url { get; set; }
    public string? bio { get; set; }
}