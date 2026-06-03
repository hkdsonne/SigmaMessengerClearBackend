using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using System.Security.Claims;
using UeerService.Controllers;
using UeerService.DTOs;
using UeerService.Services;

namespace SigmaTests.UserService;

public class UserControllerTests
{
    private readonly Mock<IUserService> _mockUserService;
    private readonly UserController _controller;
    private readonly Guid _testUserId = Guid.NewGuid();

    public UserControllerTests()
    {
        _mockUserService = new Mock<IUserService>();
        var mockLogger = new Mock<ILogger<UserController>>();

        _controller = new UserController(
            _mockUserService.Object,
            mockLogger.Object);

        // Setup authenticated user
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
            new Claim(ClaimTypes.Name, "testuser")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public async Task GetMyProfile_UserExists_ReturnsUserInfo()
    {
        // Arrange
        var userInfo = new UserInfoResponse
        {
            user_id = _testUserId,
            full_name = "testuser",
            email = "test@example.com",
            is_active = true,
            is_blocked = false
        };

        _mockUserService
            .Setup(x => x.GetFullUserInfoAsync(_testUserId))
            .ReturnsAsync(userInfo);

        // Act
        var result = await _controller.GetMyProfile();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }
}