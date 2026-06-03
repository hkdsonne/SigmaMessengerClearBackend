namespace AuthService.DTOs;

public class RegisterRequest
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string device_info { get; set; } = "Unknown";
}

public class VerifyCodeRequest
{
    public string email { get; set; } = string.Empty;
    public string code { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    public string device_info { get; set; } = "Unknown";
}

public class LoginRequest
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    public string device_info { get; set; } = "Unknown";
}

public class AuthResponse
{
    public Guid user_id { get; set; }
    public string username { get; set; } = string.Empty;
    public string id { get; set; } = string.Empty;
    public DateTime expires_at { get; set; }
}

public class CurrentUserResponse
{
    public Guid user_id { get; set; }
    public string username { get; set; } = string.Empty;
}