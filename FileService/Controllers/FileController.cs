using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileService.Data;
using FileService.Models;
using FileService.Services;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.IO.Compression;

namespace FileService.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequestSizeLimit(10_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 10_000_000)]
public class FileController : ControllerBase
{
    private const int MAX_FILE_SIZE_MB = 10;
    private const int MAX_USER_FILES = 500;
    private const int MAX_DAILY_UPLOADS = 30;
    private const int MAX_ARCHIVE_EXTRACT_SIZE_MB = 100;

    // ✅ Безопасные текстовые расширения
    private static readonly HashSet<string> _allowedTextExtensions = new()
    {
        ".txt", ".log", ".cfg", ".conf", ".ini", ".md", ".markdown",
        ".csv", ".tsv", ".json", ".xml", ".xsd", ".xslt"
    };

    // ✅ Опасные расширения для текстовых файлов (запрещены)
    private static readonly HashSet<string> _forbiddenTextExtensions = new()
    {
        ".js", ".mjs", ".py", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1",
        ".vbs", ".rb", ".pl", ".php", ".asp", ".aspx", ".jsp", ".cgi"
    };

    // ✅ Опасные расширения для любых файлов (полный запрет)
    private static readonly HashSet<string> _forbiddenExtensions = new()
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".msi", ".app", ".deb", ".rpm",
        ".com", ".scr", ".cpl", ".sys", ".drv", ".ocx", ".ax", ".xpi", ".crx"
    };

    private static readonly Dictionary<string, List<byte[]>> _fileSignatures = new()
    {
        ["image/jpeg"] = new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } },
        ["image/png"] = new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
        ["image/gif"] = new List<byte[]> { new byte[] { 0x47, 0x49, 0x46 } },
        ["image/webp"] = new List<byte[]> { new byte[] { 0x52, 0x49, 0x46, 0x46 } },
        ["image/bmp"] = new List<byte[]> { new byte[] { 0x42, 0x4D } },
        ["application/pdf"] = new List<byte[]> { new byte[] { 0x25, 0x50, 0x44, 0x46 } },
        ["text/plain"] = new List<byte[]>(),
        ["application/json"] = new List<byte[]>(),
        ["application/xml"] = new List<byte[]>(),
        ["application/zip"] = new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        ["application/gzip"] = new List<byte[]> { new byte[] { 0x1F, 0x8B } },
        ["video/mp4"] = new List<byte[]> { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 } },
        ["video/webm"] = new List<byte[]> { new byte[] { 0x1A, 0x45, 0xDF, 0xA3 } },
        ["audio/mpeg"] = new List<byte[]> { new byte[] { 0xFF, 0xFB } },
        ["audio/mp4"] = new List<byte[]> { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 } },
        ["audio/ogg"] = new List<byte[]> { new byte[] { 0x4F, 0x67, 0x67, 0x53 } }
    };

    private readonly FileDbContext _db;
    private readonly IStorageService _storage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FileController> _logger;
    private readonly string _chatServiceIp;

    public FileController(
        FileDbContext db,
        IStorageService storage,
        IHttpClientFactory httpClientFactory,
        ILogger<FileController> logger)
    {
        _db = db;
        _storage = storage;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _chatServiceIp = Environment.GetEnvironmentVariable("CHATS_SERVICE_IP") ?? "http://localhost:5002";
    }

   
    /// Проверяет, нет ли у файла двойного расширения
    private bool HasDoubleExtension(string fileName)
    {
        var parts = fileName.Split('.');
        if (parts.Length <= 2) return false;

        var lastExt = "." + parts.Last().ToLower();
        var secondLastExt = "." + parts[parts.Length - 2].ToLower();

        var dangerousCombinations = new HashSet<string>
        {
            ".exe", ".dll", ".js", ".py", ".sh", ".bat", ".ps1", ".vbs",
            ".php", ".asp", ".aspx", ".jsp", ".html", ".htm"
        };

        return dangerousCombinations.Contains(secondLastExt) || dangerousCombinations.Contains(lastExt);
    }

    /// <summary>
    /// Проверяет расширение файла на безопасность
    /// </summary>
    private bool IsExtensionAllowed(string fileName, string detectedType)
    {
        var extension = Path.GetExtension(fileName).ToLower();

        if (_forbiddenExtensions.Contains(extension))
            return false;

        if (detectedType == "text/plain" || detectedType == "application/json" || detectedType == "application/xml")
        {
            if (_forbiddenTextExtensions.Contains(extension))
                return false;

            return _allowedTextExtensions.Contains(extension);
        }

        return true;
    }

    private string? GetToken()
    {
        var authorization = Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
        {
            return authorization.Substring("Bearer ".Length).Trim();
        }

        return Request.Cookies["access_token"];
    }

    /// <summary>
    /// Извлекает userId (Guid) из JWT токена
    /// </summary>
    private Guid? GetUserIdFromToken()
    {
        try
        {
            var token = GetToken();
            if (string.IsNullOrEmpty(token))
                return null;

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);

            var userIdClaim = jsonToken.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.NameIdentifier ||
                c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" ||
                c.Type == "nameid" || c.Type == "sub" || c.Type == "user_id" || c.Type == "UserId");

            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out Guid userId))
            {
                return userId;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract user ID from token");
            return null;
        }
    }

    private async Task<(bool isValid, string detectedType)> ValidateFileMimeTypeAsync(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var buffer = new byte[256];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            stream.Position = 0;

            var header = buffer.Take(bytesRead).ToArray();

            foreach (var (mimeType, signatures) in _fileSignatures)
            {
                if (signatures.Count == 0)
                {
                    if ((mimeType == "text/plain" || mimeType == "application/json" || mimeType == "application/xml") && IsTextFile(header))
                        return (true, mimeType);
                    continue;
                }

                foreach (var signature in signatures)
                {
                    if (header.Length >= signature.Length &&
                        header.Take(signature.Length).SequenceEqual(signature))
                    {
                        return (true, mimeType);
                    }
                }
            }

            return (false, "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating MIME type");
            return (false, "error");
        }
    }

    private bool IsTextFile(byte[] header)
    {
        var firstBytes = header.Take(100);
        var nonPrintableCount = firstBytes.Count(b => b < 32 && b != 9 && b != 10 && b != 13);
        return nonPrintableCount <= 5;
    }

    private async Task<bool> IsZipBombAsync(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            long totalUncompressedSize = 0;
            int fileCount = 0;

            foreach (var entry in archive.Entries)
            {
                totalUncompressedSize += entry.Length;
                fileCount++;

                if (fileCount > 10000)
                    return true;

                if (totalUncompressedSize > MAX_ARCHIVE_EXTRACT_SIZE_MB * 1024L * 1024L)
                    return true;

                if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ZIP bomb");
            return true;
        }
    }

    /// <summary>
    /// Проверяет, имеет ли пользователь доступ к файлу
    /// </summary>
    private async Task<bool> CanAccessFile(FileAttachment file, Guid userId)
    {
        if (file.UserId == userId)
            return true;

        if (file.PostId == Guid.Empty)
            return true;

        if (file.PostId != Guid.Empty)
        {
            return await IsUserInChat(userId, file.PostId);
        }

        return false;
    }

    /// <summary>
    /// Проверяет, состоит ли пользователь в чате (через ChatService)
    /// </summary>
    private async Task<bool> IsUserInChat(Guid userId, Guid chatId)
    {
        if (chatId == Guid.Empty)
        {
            _logger.LogWarning($"Invalid chatId: {chatId}");
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_chatServiceIp);
            client.Timeout = TimeSpan.FromSeconds(5);

            var token = GetToken();
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            }

            var response = await client.GetAsync($"/api/Chat/{chatId}/participants");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"ChatService returned {response.StatusCode} for chat {chatId}");
                return true;
            }

            var participantsJson = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(participantsJson);

            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var participant in doc.RootElement.EnumerateArray())
                {
                    if (participant.TryGetProperty("user_id", out var userIdProp) &&
                        Guid.TryParse(userIdProp.GetString(), out var participantId) &&
                        participantId == userId)
                    {
                        return true;
                    }
                }
            }
            else if (doc.RootElement.TryGetProperty("participants", out var participants))
            {
                foreach (var participant in participants.EnumerateArray())
                {
                    if (participant.TryGetProperty("user_id", out var userIdProp) &&
                        Guid.TryParse(userIdProp.GetString(), out var participantId) &&
                        participantId == userId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "ChatService unavailable for chat {ChatId}", chatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking chat membership for user {UserId}, chat {ChatId}", userId, chatId);
            return false;
        }
    }

    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ 

    private IActionResult ErrorResponse(string message)
    {
        return Ok(new { success = false, error = message });
    }

    private IActionResult SuccessResponse(Guid fileId, string fileName, string fileSize, string fileType, bool wasCompressed = false)
    {
        return Ok(new
        {
            success = true,
            fileId = fileId,
            fileName = fileName,
            fileSize = fileSize,
            fileType = fileType,
            wasCompressed = wasCompressed,
            message = "Файл успешно загружен"
        });
    }

    // ОСНОВНЫЕ ЭНДПОИНТЫ 

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromForm] Guid postId)
    {
        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        if (file == null || file.Length == 0)
            return ErrorResponse("Файл не выбран");

        if (HasDoubleExtension(file.FileName))
            return ErrorResponse("Недопустимое имя файла");

        var maxSizeBytes = MAX_FILE_SIZE_MB * 1024 * 1024;
        if (file.Length > maxSizeBytes)
            return ErrorResponse($"Файл слишком большой. Максимум {MAX_FILE_SIZE_MB} MB");

        var (isValidMime, detectedType) = await ValidateFileMimeTypeAsync(file);
        if (!isValidMime)
            return ErrorResponse("Недопустимый тип файла");

        if (!IsExtensionAllowed(file.FileName, detectedType))
            return ErrorResponse("Недопустимое расширение файла");

        if (detectedType == "application/zip")
        {
            using var stream = file.OpenReadStream();
            if (await IsZipBombAsync(stream))
                return ErrorResponse("Архив содержит подозрительное содержимое (ZIP-бомба)");
        }

        var userFileCount = await _db.Vlozhenie
            .CountAsync(f => f.UserId == userId.Value && !f.IsDeleted);
        if (userFileCount >= MAX_USER_FILES)
            return ErrorResponse($"Превышен лимит файлов ({MAX_USER_FILES}). Удалите старые файлы.");

        var today = DateTime.UtcNow.Date;
        var todayUploads = await _db.Vlozhenie
            .CountAsync(f => f.UserId == userId.Value && f.UploadTime >= today);
        if (todayUploads >= MAX_DAILY_UPLOADS)
            return ErrorResponse($"Превышен дневной лимит загрузок ({MAX_DAILY_UPLOADS}). Попробуйте завтра.");

        using var md5 = MD5.Create();
        using var streamForHash = file.OpenReadStream();
        var hashBytes = await md5.ComputeHashAsync(streamForHash);
        var fileHash = Convert.ToHexString(hashBytes).ToLower();
        streamForHash.Position = 0;

        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var existingFile = await _db.Vlozhenie
                .FirstOrDefaultAsync(f => f.FileHash == fileHash && !f.IsDeleted);

            if (existingFile != null)
            {
                await transaction.CommitAsync();
                return SuccessResponse(
                    existingFile.Id,
                    existingFile.FileName,
                    existingFile.FileSize,
                    existingFile.FileType
                );
            }

            var extension = Path.GetExtension(file.FileName);
            var storedFileName = $"{Guid.NewGuid()}{extension}";

            var attachment = new FileAttachment
            {
                PostId = postId,
                FileUrl = "",
                ThumbnailUrl = null,
                FileName = file.FileName,
                FileSize = file.Length.ToString(),
                FileType = detectedType,
                UploadTime = DateTime.UtcNow,
                UserId = userId.Value,
                FileHash = fileHash,
                IsDeleted = false,
                Status = "uploading",
                OriginalSize = file.Length,
                IsCompressed = false,
                IsImageOptimized = false
            };

            _db.Vlozhenie.Add(attachment);
            await _db.SaveChangesAsync();

            try
            {
                var fileUrl = await _storage.UploadFileAsync(file, storedFileName);
                bool wasCompressed = fileUrl.EndsWith(".gz");
                bool wasImageOptimized = detectedType.StartsWith("image/") && !wasCompressed;

                attachment.FileUrl = fileUrl;
                attachment.Status = "completed";
                attachment.IsCompressed = wasCompressed;
                attachment.IsImageOptimized = wasImageOptimized;
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return SuccessResponse(
                    attachment.Id,
                    attachment.FileName,
                    attachment.FileSize,
                    attachment.FileType,
                    wasCompressed
                );
            }
            catch (Exception)
            {
                attachment.Status = "failed";
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error uploading file");
            return StatusCode(500, new { success = false, error = "Ошибка при загрузке файла" });
        }
    }

    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile(Guid fileId)
    {
        if (fileId == Guid.Empty)
            return BadRequest(new { message = "Invalid fileId" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var file = await _db.Vlozhenie
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsDeleted == false);

        if (file == null)
            return NotFound(new { message = "Файл не найден" });

        if (!await CanAccessFile(file, userId.Value))
        {
            _logger.LogWarning($"User {userId.Value} tried to access file {fileId} (chat {file.PostId}) without permission");
            return Forbid();
        }

        var fileStream = await _storage.DownloadFileAsync(file.FileUrl);
        if (fileStream == null)
            return NotFound(new { message = "Файл не найден в хранилище" });

        return File(fileStream, file.FileType, file.FileName);
    }

    [HttpGet("info/{fileId}")]
    public async Task<IActionResult> GetFileInfo(Guid fileId)
    {
        if (fileId == Guid.Empty)
            return BadRequest(new { message = "Invalid fileId" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var file = await _db.Vlozhenie
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsDeleted == false);

        if (file == null)
            return NotFound(new { message = "Файл не найден" });

        if (!await CanAccessFile(file, userId.Value))
            return Forbid();

        return Ok(new
        {
            file.Id,
            file.PostId,
            file.FileName,
            file.FileSize,
            file.FileType,
            file.UploadTime,
            file.OriginalSize,
            file.IsCompressed,
            file.IsImageOptimized
        });
    }

    [HttpGet("my-files")]
    public async Task<IActionResult> GetMyFiles([FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        if (offset < 0) offset = 0;
        limit = Math.Clamp(limit, 1, 100);

        var files = await _db.Vlozhenie
            .Where(f => f.UserId == userId.Value && !f.IsDeleted)
            .OrderByDescending(f => f.UploadTime)
            .Skip(offset)
            .Take(limit)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.FileSize,
                f.FileType,
                f.UploadTime,
                f.PostId,
                f.OriginalSize,
                f.IsCompressed,
                f.IsImageOptimized
            })
            .ToListAsync();

        return Ok(files);
    }

    [HttpGet("by-post/{postId}")]
    public async Task<IActionResult> GetFilesByPostId(Guid postId)
    {
        if (postId == Guid.Empty)
            return BadRequest(new { message = "Invalid postId" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var canAccess = await IsUserInChat(userId.Value, postId);
        if (!canAccess)
            return Forbid();

        var files = await _db.Vlozhenie
            .Where(f => f.PostId == postId && !f.IsDeleted)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.FileSize,
                f.FileType,
                f.UploadTime,
                f.OriginalSize,
                f.IsCompressed,
                f.IsImageOptimized
            })
            .ToListAsync();

        return Ok(files);
    }

    [HttpDelete("{fileId}")]
    public async Task<IActionResult> DeleteFile(Guid fileId)
    {
        if (fileId == Guid.Empty)
            return BadRequest(new { message = "Invalid fileId" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var file = await _db.Vlozhenie
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsDeleted == false);

        if (file == null)
            return NotFound(new { message = "Файл не найден" });

        if (file.UserId != userId.Value)
            return Forbid();

        file.IsDeleted = true;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Файл удалён",
            fileId = file.Id,
            fileName = file.FileName
        });
    }

    [HttpGet("temp-link/{fileId}")]
    public async Task<IActionResult> GetTempDownloadLink(Guid fileId, [FromQuery] int expiryMinutes = 15)
    {
        if (fileId == Guid.Empty)
            return BadRequest(new { message = "Invalid fileId" });

        if (expiryMinutes < 1 || expiryMinutes > 1440)
            return BadRequest(new { message = "expiryMinutes must be between 1 and 1440" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var file = await _db.Vlozhenie
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsDeleted == false);

        if (file == null)
            return NotFound(new { message = "Файл не найден" });

        if (!await CanAccessFile(file, userId.Value))
            return Forbid();

        var fileKey = ExtractFileKeyFromUrl(file.FileUrl);
        var presignedUrl = _storage.GeneratePresignedUrl(fileKey, expiryMinutes);

        return Ok(new
        {
            downloadUrl = presignedUrl,
            expiresIn = expiryMinutes,
            fileName = file.FileName,
            message = $"Временная ссылка действительна {expiryMinutes} минут"
        });
    }

    //ЭНДПОИНТЫ ДЛЯ ИЗОБРАЖЕНИЙ 

    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
    {
        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        if (file == null || file.Length == 0)
            return ErrorResponse("Файл не выбран");

        var (isValidMime, detectedType) = await ValidateFileMimeTypeAsync(file);
        if (!isValidMime || !detectedType.StartsWith("image/"))
            return ErrorResponse("Можно загружать только изображения");

        var maxSizeBytes = 5 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
            return ErrorResponse("Аватарка не должна превышать 5 MB");

        using var md5 = MD5.Create();
        using var stream = file.OpenReadStream();
        var hashBytes = await md5.ComputeHashAsync(stream);
        var fileHash = Convert.ToHexString(hashBytes).ToLower();
        stream.Position = 0;

        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var oldAvatars = await _db.Vlozhenie
                .Where(f => f.UserId == userId.Value && f.PostId == Guid.Empty && !f.IsDeleted)
                .ToListAsync();

            foreach (var oldAvatar in oldAvatars)
            {
                oldAvatar.IsDeleted = true;
            }

            var (fileUrl, thumbnailUrl) = await _storage.UploadImageWithThumbnailAsync(
                file,
                $"avatar_{userId.Value}_{Guid.NewGuid()}"
            );

            var attachment = new FileAttachment
            {
                PostId = Guid.Empty,
                FileUrl = fileUrl,
                ThumbnailUrl = thumbnailUrl,
                FileName = file.FileName,
                FileSize = file.Length.ToString(),
                FileType = detectedType,
                UploadTime = DateTime.UtcNow,
                UserId = userId.Value,
                FileHash = $"{fileHash}_avatar_{userId.Value}_{Guid.NewGuid():N}",
                IsDeleted = false,
                Status = "completed",
                IsImageOptimized = true,
                OriginalSize = file.Length,
                IsCompressed = false
            };

            _db.Vlozhenie.Add(attachment);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                success = true,
                avatarUrl = $"/api/File/thumbnail/{attachment.Id}",
                originalUrl = $"/api/File/download/{attachment.Id}",
                fileId = attachment.Id,
                message = "Аватарка успешно обновлена"
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error uploading avatar");
            return StatusCode(500, new { success = false, error = "Ошибка при загрузке аватарки" });
        }
    }

    [HttpGet("avatar/{userId}")]
    public async Task<IActionResult> GetUserAvatar(Guid userId)
    {
        if (userId == Guid.Empty)
            return BadRequest(new { message = "Invalid userId" });

        var currentUserId = GetUserIdFromToken();
        if (!currentUserId.HasValue) return Unauthorized();

        var avatar = await _db.Vlozhenie
            .Where(f => f.UserId == userId && f.PostId == Guid.Empty && !f.IsDeleted)
            .OrderByDescending(f => f.UploadTime)
            .FirstOrDefaultAsync();

        if (avatar == null)
            return NotFound(new { message = "Аватарка не найдена" });

        var thumbnailUrl = !string.IsNullOrEmpty(avatar.ThumbnailUrl)
            ? avatar.ThumbnailUrl
            : avatar.FileUrl;

        var fileStream = await _storage.DownloadFileAsync(thumbnailUrl);

        if (fileStream == null)
            return NotFound(new { message = "Файл аватарки не найден" });

        Response.Headers.Append("Cache-Control", "public, max-age=3600");
        return File(fileStream, "image/png");
    }

    [HttpPost("chat-photo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadChatPhoto([FromForm] IFormFile file, [FromForm] Guid chatId)
    {
        if (chatId == Guid.Empty)
            return ErrorResponse("Invalid chatId");

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        if (file == null || file.Length == 0)
            return ErrorResponse("Файл не выбран");

        var (isValidMime, detectedType) = await ValidateFileMimeTypeAsync(file);
        if (!isValidMime || !detectedType.StartsWith("image/"))
            return ErrorResponse("Можно загружать только изображения");

        if (!IsExtensionAllowed(file.FileName, detectedType))
            return ErrorResponse("Недопустимое расширение файла");

        using var md5 = MD5.Create();
        using var stream = file.OpenReadStream();
        var hashBytes = await md5.ComputeHashAsync(stream);
        var fileHash = Convert.ToHexString(hashBytes).ToLower();
        stream.Position = 0;

        try
        {
            var (fileUrl, thumbnailUrl) = await _storage.UploadImageWithThumbnailAsync(file, $"chat_{chatId}_{Guid.NewGuid()}");

            var attachment = new FileAttachment
            {
                PostId = chatId,
                FileUrl = fileUrl,
                ThumbnailUrl = thumbnailUrl,
                FileName = file.FileName,
                FileSize = file.Length.ToString(),
                FileType = detectedType,
                UploadTime = DateTime.UtcNow,
                UserId = userId.Value,
                FileHash = fileHash,
                IsDeleted = false,
                Status = "completed",
                IsImageOptimized = true
            };

            _db.Vlozhenie.Add(attachment);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                fileId = attachment.Id,
                embedUrl = thumbnailUrl,
                originalUrl = fileUrl,
                message = "Фото успешно загружено"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading chat photo");
            return StatusCode(500, new { success = false, error = "Ошибка при загрузке фото" });
        }
    }

    [HttpGet("thumbnail/{fileId}")]
    public async Task<IActionResult> GetThumbnail(Guid fileId)
    {
        if (fileId == Guid.Empty)
            return BadRequest(new { message = "Invalid fileId" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var file = await _db.Vlozhenie
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted);

        if (file == null) return NotFound(new { message = "Файл не найден" });

        if (!await CanAccessFile(file, userId.Value))
            return Forbid();

        Stream? imageStream = null;
        string contentType = "image/png";

        if (!string.IsNullOrEmpty(file.ThumbnailUrl))
        {
            imageStream = await _storage.DownloadFileAsync(file.ThumbnailUrl);
            contentType = "image/png";
        }
        else
        {
            imageStream = await _storage.DownloadFileAsync(file.FileUrl);
            contentType = file.FileType;
        }

        if (imageStream == null)
            return NotFound(new { message = "Файл не найден в хранилище" });

        return File(imageStream, contentType);
    }

    [HttpGet("embed/{fileId}")]
    public async Task<IActionResult> GetEmbedUrl(Guid fileId)
    {
        if (fileId == Guid.Empty)
            return BadRequest(new { message = "Invalid fileId" });

        var userId = GetUserIdFromToken();
        if (!userId.HasValue) return Unauthorized();

        var file = await _db.Vlozhenie
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted);

        if (file == null) return NotFound();

        if (!await CanAccessFile(file, userId.Value))
        {
            _logger.LogWarning($"User {userId.Value} tried to embed file {fileId} (chat {file.PostId}) without permission");
            return Forbid();
        }

        var embedUrl = !string.IsNullOrEmpty(file.ThumbnailUrl) ? file.ThumbnailUrl : file.FileUrl;

        return Ok(new
        {
            embedUrl = embedUrl,
            fileName = file.FileName,
            fileType = file.FileType,
            message = "Ссылка для встраивания действительна"
        });
    }

    private string ExtractFileKeyFromUrl(string fileUrl)
    {
        var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") ?? "sigmadatastorage";
        var prefix = $"https://{bucketName}.storage.yandexcloud.net/";
        if (fileUrl.StartsWith(prefix))
        {
            return fileUrl.Substring(prefix.Length);
        }
        return string.Empty;
    }
}