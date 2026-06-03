using MailKit.Net.Smtp;
using MailKit.Security;
using MailService.Models;
using MimeKit;
using System.Security.Cryptography;

namespace MailService.Services;


public class MailServiceImpl : IMailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MailServiceImpl> _logger;

    public MailServiceImpl(IConfiguration configuration, ILogger<MailServiceImpl> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<EmailResponse> SendVerificationCodeAsync(SendVerificationCodeRequest request)
    {
        try
        {
            var fromName = _configuration["Email:FromName"];
            var fromEmail = _configuration["Email:Smtp:From"];   
            var host = _configuration["Email:Smtp:Host"];
            var portStr = _configuration["Email:Smtp:Port"];
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];

            if (!int.TryParse(portStr, out int port))
            {
                _logger.LogError("Invalid SMTP port: {Port}", portStr);
                return new EmailResponse { success = false, message = "Invalid port" };
            }
            if (string.IsNullOrEmpty(fromEmail))
            {
                _logger.LogError("Sender email is missing");
                return new EmailResponse { success = false, message = "Sender not configured" };
            }

            // Формируем письмо
            var body = $@"Здравствуйте, { request.username}
            !

 Спасибо за регистрацию в ТЧК 💜

Для подтверждения вашей почты используйте код:

━━━━━━━━━━━━━━━  
        { request.code}  
━━━━━━━━━━━━━━━

⏳ Код действителен в течение 10 минут.

Если вы не запрашивали этот код — просто проигнорируйте это письмо.

С уважением,
Команда ТЧК";


            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(new MailboxAddress("", request.email));
            message.Subject = "Ваш код подтверждения - Messenger";
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();

            // Подключаемся
            await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect);

            // Аутентифицируемся
            await client.AuthenticateAsync(username, password);

            // Отправляем
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Verification code sent to {Email}", request.email);

            return new EmailResponse
            {
                success = true,
                message = "Verification code sent successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification code to {Email}", request.email);
            return new EmailResponse
            {
                success = false,
                message = ex.Message
            };
        }
    }
}