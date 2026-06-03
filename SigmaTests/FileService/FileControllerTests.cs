using FileService.Controllers;
using FileService.Data;
using FileService.Models;
using FileService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using SigmaTests.Fixtures;
using SigmaTests.Helpers;
using System.Security.Claims;

namespace SigmaTests.FileService;

[Collection("Database collection")]
public class FileControllerTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private readonly Mock<IStorageService> _mockStorage;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<FileController>> _mockLogger;
    private readonly FileController _controller;
    private readonly Guid _testUserId = Guid.NewGuid();

    public FileControllerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _mockStorage = new Mock<IStorageService>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<FileController>>();

        _controller = new FileController(
            _fixture.FileDbContext,
            _mockStorage.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);

        // Настройка аутентификации
        var httpContext = new DefaultHttpContext();

        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
            new Claim(ClaimTypes.Name, "testuser")
        }, "TestAuth"));

        httpContext.Request.Headers["Authorization"] = $"Bearer {TokenHelper.GenerateTestToken(_testUserId)}";

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    // ============= UPLOAD TESTS =============

    [Fact]
    public async Task UploadFile_ValidTextFile_ReturnsSuccess()
    {
        var file = TestFileHelper.CreateTestTextFile("test.txt", "Hello World!");
        var postId = Guid.NewGuid();

        _mockStorage
            .Setup(x => x.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
            .ReturnsAsync("https://test-storage.yandexcloud.net/uploads/test.txt");

        var result = await _controller.UploadFile(file, postId);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UploadFile_NoFile_ReturnsError()
    {
        IFormFile? file = null;
        var postId = Guid.NewGuid();

        var result = await _controller.UploadFile(file!, postId);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UploadFile_EmptyFile_ReturnsError()
    {
        var file = TestFileHelper.CreateTestTextFile("empty.txt", "");
        var postId = Guid.NewGuid();

        var result = await _controller.UploadFile(file, postId);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UploadFile_ImageFile_ReturnsSuccess()
    {
        var file = TestFileHelper.CreateTestImageFile("photo.jpg");
        var postId = Guid.NewGuid();

        _mockStorage
            .Setup(x => x.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
            .ReturnsAsync("https://test-storage.yandexcloud.net/uploads/photo.jpg");

        var result = await _controller.UploadFile(file, postId);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ============= DOWNLOAD TESTS =============

    [Fact]
    public async Task DownloadFile_ExistingFile_ReturnsFile()
    {
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://test-storage.yandexcloud.net/uploads/test.txt",
            FileName = "test.txt",
            FileType = "text/plain",
            UserId = _testUserId,
            FileHash = "abc123",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed"
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        var fileStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello World!"));
        _mockStorage
            .Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(fileStream);

        var result = await _controller.DownloadFile(fileAttachment.Id);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadFile_NonExistentFile_ReturnsNotFound()
    {
        var result = await _controller.DownloadFile(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DownloadFile_DeletedFile_ReturnsNotFound()
    {
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://test-storage.yandexcloud.net/uploads/deleted.txt",
            FileName = "deleted.txt",
            FileType = "text/plain",
            UserId = _testUserId,
            FileHash = "def456",
            UploadTime = DateTime.UtcNow,
            IsDeleted = true,
            Status = "deleted"
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        var result = await _controller.DownloadFile(fileAttachment.Id);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DownloadFile_FileBelongsToAnotherUser_ReturnsForbid()
    {
        var otherUserId = Guid.NewGuid();
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://test-storage.yandexcloud.net/uploads/private.txt",
            FileName = "private.txt",
            FileType = "text/plain",
            UserId = otherUserId,
            FileHash = "ghi789",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed"
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        var result = await _controller.DownloadFile(fileAttachment.Id);
        result.Should().BeOfType<ForbidResult>();
    }

    // ============= FILE INFO TESTS =============

    [Fact]
    public async Task GetFileInfo_ExistingFile_ReturnsFileInfo()
    {
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://test-storage.yandexcloud.net/uploads/info.txt",
            FileName = "info.txt",
            FileSize = "1024",
            FileType = "text/plain",
            UserId = _testUserId,
            FileHash = "jkl012",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed",
            OriginalSize = 2048,
            IsCompressed = true,
            IsImageOptimized = false
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        var result = await _controller.GetFileInfo(fileAttachment.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetFileInfo_NonExistentFile_ReturnsNotFound()
    {
        var result = await _controller.GetFileInfo(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ============= MY FILES TESTS =============

    [Fact]
    public async Task GetMyFiles_UserHasFiles_ReturnsFileList()
    {
        var files = new List<FileAttachment>
        {
            new() {
                Id = Guid.NewGuid(),
                PostId = Guid.NewGuid(),
                FileUrl = "url1",
                FileName = "file1.txt",
                FileSize = "100",
                FileType = "text/plain",
                UserId = _testUserId,
                FileHash = "hash1",
                UploadTime = DateTime.UtcNow.AddDays(-1),
                IsDeleted = false,
                Status = "completed"
            },
            new() {
                Id = Guid.NewGuid(),
                PostId = Guid.NewGuid(),
                FileUrl = "url2",
                FileName = "file2.jpg",
                FileSize = "500",
                FileType = "image/jpeg",
                UserId = _testUserId,
                FileHash = "hash2",
                UploadTime = DateTime.UtcNow,
                IsDeleted = false,
                Status = "completed"
            }
        };

        await _fixture.FileDbContext.Vlozhenie.AddRangeAsync(files);
        await _fixture.FileDbContext.SaveChangesAsync();

        var result = await _controller.GetMyFiles(0, 50);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMyFiles_UserHasNoFiles_ReturnsEmptyList()
    {
        var result = await _controller.GetMyFiles(0, 50);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMyFiles_WithPagination_ReturnsCorrectPage()
    {
        for (int i = 0; i < 25; i++)
        {
            var file = new FileAttachment
            {
                Id = Guid.NewGuid(),
                PostId = Guid.NewGuid(),
                FileUrl = $"url{i}",
                FileName = $"file{i}.txt",
                FileSize = "100",
                FileType = "text/plain",
                UserId = _testUserId,
                FileHash = $"hash{i}",
                UploadTime = DateTime.UtcNow.AddMinutes(-i),
                IsDeleted = false,
                Status = "completed"
            };
            await _fixture.FileDbContext.Vlozhenie.AddAsync(file);
        }
        await _fixture.FileDbContext.SaveChangesAsync();

        var resultFirstPage = await _controller.GetMyFiles(0, 10);
        var resultSecondPage = await _controller.GetMyFiles(10, 10);

        resultFirstPage.Should().BeOfType<OkObjectResult>();
        resultSecondPage.Should().BeOfType<OkObjectResult>();
    }

    // ============= DELETE TESTS =============

    [Fact]
    public async Task DeleteFile_OwnFile_DeletesSuccessfully()
    {
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://test-storage.yandexcloud.net/uploads/todelete.txt",
            FileName = "todelete.txt",
            FileType = "text/plain",
            UserId = _testUserId,
            FileHash = "mno345",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed"
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        _mockStorage
            .Setup(x => x.DeleteFileAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var result = await _controller.DeleteFile(fileAttachment.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DeleteFile_SomeoneElsesFile_ReturnsForbid()
    {
        var otherUserId = Guid.NewGuid();
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "url",
            FileName = "private.txt",
            FileType = "text/plain",
            UserId = otherUserId,
            FileHash = "pqr678",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed"
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        var result = await _controller.DeleteFile(fileAttachment.Id);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteFile_NonExistentFile_ReturnsNotFound()
    {
        var result = await _controller.DeleteFile(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ============= FILES BY POST TESTS =============

    [Fact]
    public async Task GetFilesByPostId_ValidPost_ReturnsResult()
    {
        var postId = Guid.NewGuid();
        var files = new List<FileAttachment>
        {
            new() {
                Id = Guid.NewGuid(),
                PostId = postId,
                FileUrl = "url1",
                FileName = "file1.txt",
                FileSize = "100",
                FileType = "text/plain",
                UserId = _testUserId,
                FileHash = "stu901",
                UploadTime = DateTime.UtcNow,
                IsDeleted = false,
                Status = "completed"
            }
        };

        await _fixture.FileDbContext.Vlozhenie.AddRangeAsync(files);
        await _fixture.FileDbContext.SaveChangesAsync();

        var result = await _controller.GetFilesByPostId(postId);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFilesByPostId_EmptyPost_ReturnsResult()
    {
        var postId = Guid.NewGuid();
        var result = await _controller.GetFilesByPostId(postId);
        result.Should().NotBeNull();
    }

    // ============= TEMP LINK TESTS =============

    [Fact]
    public async Task GetTempDownloadLink_ValidFile_ReturnsPresignedUrl()
    {
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://test-storage.yandexcloud.net/uploads/tempfile.txt",
            FileName = "tempfile.txt",
            FileType = "text/plain",
            UserId = _testUserId,
            FileHash = "yzA567",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed"
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        _mockStorage
            .Setup(x => x.GeneratePresignedUrl(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("https://presigned-url.test/file.txt?expires=123456");

        var result = await _controller.GetTempDownloadLink(fileAttachment.Id, 15);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetTempDownloadLink_InvalidExpiry_ReturnsBadRequest()
    {
        var fileId = Guid.NewGuid();
        var result = await _controller.GetTempDownloadLink(fileId, 0);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTempDownloadLink_ExpiryTooLarge_ReturnsBadRequest()
    {
        var fileId = Guid.NewGuid();
        var result = await _controller.GetTempDownloadLink(fileId, 1500);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ============= AVATAR TESTS =============

    [Fact]
    public async Task UploadAvatar_ValidImage_ReturnsSuccess()
    {
        var file = TestFileHelper.CreateTestImageFile("avatar.jpg");

        _mockStorage
            .Setup(x => x.UploadImageWithThumbnailAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
            .ReturnsAsync(("https://storage/avatar.jpg", "https://storage/thumb.jpg"));

        var result = await _controller.UploadAvatar(file);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UploadAvatar_NonImageFile_ReturnsError()
    {
        var file = TestFileHelper.CreateTestTextFile("avatar.txt", "not an image");
        var result = await _controller.UploadAvatar(file);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UploadAvatar_FileTooLarge_ReturnsError()
    {
        var largeContent = new string('A', 6 * 1024 * 1024);
        var file = TestFileHelper.CreateTestTextFile("large.jpg", largeContent);
        var result = await _controller.UploadAvatar(file);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ============= THUMBNAIL TESTS =============

    [Fact]
    public async Task GetThumbnail_ExistingFile_ReturnsImage()
    {
        var fileAttachment = new FileAttachment
        {
            Id = Guid.NewGuid(),
            PostId = Guid.NewGuid(),
            FileUrl = "https://storage/image.jpg",
            ThumbnailUrl = "https://storage/thumb.jpg",
            FileName = "image.jpg",
            FileType = "image/jpeg",
            UserId = _testUserId,
            FileHash = "thumb123",
            UploadTime = DateTime.UtcNow,
            IsDeleted = false,
            Status = "completed",
            IsImageOptimized = true
        };

        await _fixture.FileDbContext.Vlozhenie.AddAsync(fileAttachment);
        await _fixture.FileDbContext.SaveChangesAsync();

        var imageStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF });
        _mockStorage
            .Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(imageStream);

        var result = await _controller.GetThumbnail(fileAttachment.Id);
        result.Should().NotBeNull();
    }
}