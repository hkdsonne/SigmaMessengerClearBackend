using MailService.Models;
using MailService.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace MailService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailController : ControllerBase
{
    private readonly IMailService _emailService;
    private readonly ILogger<EmailController> _logger;

    public EmailController(IMailService emailService, ILogger<EmailController> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("send-verification")]
    public async Task<IActionResult> SendVerificationCode([FromBody] SendVerificationCodeRequest request)
    {
        try
        {
            var result = await _emailService.SendVerificationCodeAsync(request);

            if (result.success)
            {
                return Ok(ApiResponse<object>.Ok(null, result.message));
            }

            return BadRequest(ApiResponse<object>.Fail(result.message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending verification code");
            return StatusCode(500, ApiResponse<object>.Fail("Internal server error"));
        }
    }
}