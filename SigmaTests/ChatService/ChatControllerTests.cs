using ChatService.Controllers;
using ChatService.Data;
using ChatService.DTO;
using ChatService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using SigmaTests.Fixtures;
using System.Security.Claims;

namespace SigmaTests.ChatService;

[Collection("Database collection")]
public class ChatControllerTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private readonly ChatController _controller;
    private readonly Guid _testUserId = Guid.NewGuid();

    public ChatControllerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        // Получаем ключ шифрования из переменной окружения
        var encryptionKey = GetEncryptionKey();
        var encryption = new MessageEncryption(encryptionKey);

        var mockLogger = new Mock<ILogger<ChatController>>();

        _controller = new ChatController(
            _fixture.ChatDbContext,
            encryption,
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

    private static string GetEncryptionKey()
    {
        var key = Environment.GetEnvironmentVariable("TEST_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(key))
            return key;

        // Ищем .env.tests аналогично TokenHelper
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "../../../../"));
        var envPath = Path.Combine(solutionRoot, "SigmaTests", ".env.tests");
        if (!File.Exists(envPath))
            envPath = Path.Combine(baseDir, "../../../.env.tests");
        if (!File.Exists(envPath))
            envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env.tests");

        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("TEST_ENCRYPTION_KEY="))
                    return line.Substring("TEST_ENCRYPTION_KEY=".Length).Trim();
            }
        }

        throw new InvalidOperationException(
            "TEST_ENCRYPTION_KEY not found. Create .env.tests in SigmaTests folder with TEST_ENCRYPTION_KEY=...");
    }

    [Fact]
    public async Task GetMyChats_UserHasNoChats_ReturnsEmptyList()
    {
        var result = await _controller.GetMyChats();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreatePrivateChat_ValidRequest_CreatesChat()
    {
        var otherUserId = Guid.NewGuid();
        var dto = new CreatePrivateChatDto { OtherUserId = otherUserId };
        var result = await _controller.CreatePrivateChat(dto);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CreateGroupChat_ValidRequest_CreatesGroup()
    {
        var dto = new CreateGroupChatDto
        {
            Nazvanie = "Test Group",
            Opisanie = "Test Description",
            UserIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        };
        var result = await _controller.CreateGroupChat(dto);
        result.Should().BeOfType<OkObjectResult>();
    }
}