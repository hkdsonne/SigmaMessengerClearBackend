using MailService.Models;

namespace MailService.Services;



public interface IMailService
{
    Task<EmailResponse> SendVerificationCodeAsync(SendVerificationCodeRequest request);
}
