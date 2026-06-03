namespace UeerService.DTOs;

public class InitUserRequest
{
    public Guid user_id { get; set; }
    public string username { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
}