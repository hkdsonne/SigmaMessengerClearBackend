namespace MailService.Models;


public class SendVerificationCodeRequest
{
    public string email { get; set; } = string.Empty;
    public string code { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
}

public class EmailResponse
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
}