using MailService.Controllers;
using MailService.Models;
using MailService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;

namespace SigmaTests.MailService;

public class MailControllerTests
{
    private readonly Mock<IMailService> _mockMailService;
    private readonly Mock<ILogger<EmailController>> _mockLogger;
    private readonly EmailController _controller;

    public MailControllerTests()
    {
        _mockMailService = new Mock<IMailService>();
        _mockLogger = new Mock<ILogger<EmailController>>();
        _controller = new EmailController(_mockMailService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SendVerificationCode_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new SendVerificationCodeRequest
        {
            email = "test@example.com",
            code = "123456",
            username = "testuser"
        };

        var expectedResponse = new EmailResponse
        {
            success = true,
            message = "Verification code sent successfully"
        };

        _mockMailService
            .Setup(x => x.SendVerificationCodeAsync(It.IsAny<SendVerificationCodeRequest>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.SendVerificationCode(request);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task SendVerificationCode_MailServiceFails_ReturnsBadRequest()
    {
        // Arrange
        var request = new SendVerificationCodeRequest
        {
            email = "test@example.com",
            code = "123456",
            username = "testuser"
        };

        var expectedResponse = new EmailResponse
        {
            success = false,
            message = "Failed to send email"
        };

        _mockMailService
            .Setup(x => x.SendVerificationCodeAsync(It.IsAny<SendVerificationCodeRequest>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.SendVerificationCode(request);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SendVerificationCode_ExceptionThrown_ReturnsInternalServerError()
    {
        // Arrange
        var request = new SendVerificationCodeRequest
        {
            email = "test@example.com",
            code = "123456",
            username = "testuser"
        };

        _mockMailService
            .Setup(x => x.SendVerificationCodeAsync(It.IsAny<SendVerificationCodeRequest>()))
            .ThrowsAsync(new Exception("SMTP connection failed"));

        // Act
        var result = await _controller.SendVerificationCode(request);

        // Assert
        var statusCodeResult = result as ObjectResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task SendVerificationCode_EmptyEmail_StillProcessesRequest()
    {
        // Arrange
        var request = new SendVerificationCodeRequest
        {
            email = "",
            code = "123456",
            username = "testuser"
        };

        var expectedResponse = new EmailResponse
        {
            success = false,
            message = "Invalid email"
        };

        _mockMailService
            .Setup(x => x.SendVerificationCodeAsync(It.IsAny<SendVerificationCodeRequest>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.SendVerificationCode(request);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(400);
    }
}