using AuthService.Controllers;
using AuthService.DTOs;
using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace SigmaTests.AuthService;

// Вспомогательный класс для мока кук запроса
public class MockRequestCookieCollection : IRequestCookieCollection
{
    private readonly Dictionary<string, string> _cookies = new();

    public MockRequestCookieCollection(string key, string value)
    {
        _cookies[key] = value;
    }

    public string this[string key] => _cookies.TryGetValue(key, out var value) ? value : null;
    public int Count => _cookies.Count;
    public ICollection<string> Keys => _cookies.Keys;
    public bool ContainsKey(string key) => _cookies.ContainsKey(key);
    public bool TryGetValue(string key, out string value) => _cookies.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockJwtService = new Mock<IJwtService>();
        var mockLogger = new Mock<ILogger<AuthController>>();

        _controller = new AuthController(
            _mockAuthService.Object,
            _mockJwtService.Object,
            mockLogger.Object);

        // Базовый HttpContext для тестов
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task SendVerification_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new RegisterRequest
        {
            email = "test@example.com",
            username = "testuser",
            password = "Password123!",
            device_info = "TestDevice"
        };

        _mockAuthService
            .Setup(x => x.SendVerificationCodeAsync(
                request.email,
                request.username,
                request.password,
                request.device_info))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.SendVerification(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SendVerification_UserExists_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterRequest
        {
            email = "existing@example.com",
            username = "existinguser",
            password = "Password123!",
            device_info = "TestDevice"
        };

        _mockAuthService
            .Setup(x => x.SendVerificationCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.SendVerification(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new LoginRequest
        {
            username = "testuser",
            password = "Password123!",
            device_info = "TestDevice"
        };

        var authResponse = new AuthResponse
        {
            user_id = userId,
            username = "testuser",
            id = "session-hash-456",
            expires_at = DateTime.UtcNow.AddDays(7)
        };

        _mockAuthService
            .Setup(x => x.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync(authResponse);

        _mockJwtService
            .Setup(x => x.GenerateAccessToken(userId, "testuser"))
            .Returns("jwt-token-456");

        // Act
        var result = await _controller.Login(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            username = "wronguser",
            password = "wrongpassword",
            device_info = "TestDevice"
        };

        _mockAuthService
            .Setup(x => x.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Login(request);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task VerifyCode_ValidCode_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new VerifyCodeRequest
        {
            email = "test@example.com",
            code = "123456",
            username = "testuser",
            password = "Password123!",
            device_info = "TestDevice"
        };

        var authResponse = new AuthResponse
        {
            user_id = userId,
            username = "testuser",
            id = "session-hash-123",
            expires_at = DateTime.UtcNow.AddDays(7)
        };

        _mockAuthService
            .Setup(x => x.VerifyCodeAndRegisterAsync(It.IsAny<VerifyCodeRequest>()))
            .ReturnsAsync(authResponse);

        _mockJwtService
            .Setup(x => x.GenerateAccessToken(userId, "testuser"))
            .Returns("jwt-token-123");

        // Act
        var result = await _controller.VerifyCode(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task VerifyCode_InvalidCode_ReturnsBadRequest()
    {
        // Arrange
        var request = new VerifyCodeRequest
        {
            email = "test@example.com",
            code = "wrongcode",
            username = "testuser",
            password = "Password123!",
            device_info = "TestDevice"
        };

        _mockAuthService
            .Setup(x => x.VerifyCodeAndRegisterAsync(It.IsAny<VerifyCodeRequest>()))
            .ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.VerifyCode(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Logout_ValidSession_ReturnsOk()
    {
        // Arrange
        _mockAuthService
            .Setup(x => x.RevokeRefreshTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Logout();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var refreshToken = "valid-refresh-token";
        var authResponse = new AuthResponse
        {
            user_id = userId,
            username = "testuser",
            id = "new-session-hash",
            expires_at = DateTime.UtcNow.AddDays(7)
        };

        _mockAuthService
            .Setup(x => x.RefreshTokenAsync(refreshToken))
            .ReturnsAsync(authResponse);

        _mockJwtService
            .Setup(x => x.GenerateAccessToken(userId, "testuser"))
            .Returns("new-jwt-token");

        // Добавляем refresh token в куки
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Cookies = new MockRequestCookieCollection("refresh_token", refreshToken);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        // Act
        var result = await _controller.Refresh();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }
}