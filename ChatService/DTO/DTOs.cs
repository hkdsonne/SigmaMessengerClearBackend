namespace ChatService.DTO;
public class CreatePrivateChatDto
{
    public Guid OtherUserId { get; set; }
}

public class CreateGroupChatDto
{
    public string Nazvanie { get; set; } = string.Empty;
    public string? Opisanie { get; set; }
    public List<Guid> UserIds { get; set; } = new();
}

public class ParticipantDto
{
    public Guid user_id { get; set; }
    public string? username { get; set; }
    public string rol { get; set; } = string.Empty;
    public DateTime data_vstuplenia { get; set; }
}

public class UpdateRoleRequest
{
    public Guid user_id { get; set; }
    public string rol { get; set; } = string.Empty;
}


public class UserApiResponse
{
    public bool Success { get; set; }
    public UserData Data { get; set; }
}

public class UserData
{
    public Guid user_id { get; set; }
    public string full_name { get; set; }
    public string username { get; set; }
    public string? avatar_url { get; set; }
    public string? bio { get; set; }
}

public class SendMessageDto
{
    public Guid ChatId { get; set; }
    public Guid? ReplyToId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ContentType { get; set; }
}