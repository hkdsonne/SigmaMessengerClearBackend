using ChatService.DTO;
using ChatService.Data;
using ChatService.Models;
using ChatService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace ChatService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ChatDbContext _db;
    private readonly IMessageEncryption _encryption;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ChatDbContext db, IMessageEncryption encryption, ILogger<ChatController> logger)
    {
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException();
        return userId;
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyChats()
    {
        var userId = GetUserId();

        // Глобальный чат
        var globalChat = await _db.Chats.FirstOrDefaultAsync(c => c.tip == "global");
        if (globalChat != null)
        {
            var isMember = await _db.Participants.AnyAsync(p => p.chat_id == globalChat.chat_id && p.user_id == userId);
            if (!isMember)
            {
                _db.Participants.Add(new Participant
                {
                    chat_id = globalChat.chat_id,
                    user_id = userId,
                    rol = "member",
                    data_vstuplenia = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }

        var chats = await _db.Participants
            .Where(p => p.user_id == userId)
            .Include(p => p.Chat)
            .Select(p => new
            {
                p.chat_id,
                p.Chat.nazvanie,
                p.Chat.tip,
                p.Chat.avatar_url,
                p.Chat.user_id,
                last_message_encrypted = _db.Messages
                    .Where(m => m.chat_id == p.chat_id && !m.is_deleted)
                    .OrderByDescending(m => m.created_at)
                    .Select(m => m.content)
                    .FirstOrDefault(),
                last_message_time = _db.Messages
                    .Where(m => m.chat_id == p.chat_id && !m.is_deleted)
                    .OrderByDescending(m => m.created_at)
                    .Select(m => m.created_at)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = new List<object>();
        foreach (var chat in chats)
        {
            string displayName = chat.nazvanie;

            if (chat.tip == "private")
            {
                // Находим ID собеседника
                var otherParticipant = await _db.Participants
                    .Where(p => p.chat_id == chat.chat_id && p.user_id != userId)
                    .Select(p => p.user_id)
                    .FirstOrDefaultAsync();

                if (otherParticipant != null)
                {
                    // Получаем имя собеседника
                    var otherUserName = await GetUserNameByIdAsync(otherParticipant);
                    if (!string.IsNullOrEmpty(otherUserName))
                    {
                        displayName = otherUserName;

                        // Обновляем название в БД, если оно отличается
                        if (chat.nazvanie != otherUserName)
                        {
                            var chatToUpdate = await _db.Chats.FindAsync(chat.chat_id);
                            if (chatToUpdate != null)
                            {
                                chatToUpdate.nazvanie = otherUserName;
                                await _db.SaveChangesAsync();
                            }
                        }
                    }
                    else
                    {
                        displayName = $"User_{otherParticipant}";
                    }
                }
                else
                {
                    displayName = chat.nazvanie ?? "Чат";
                }
            }

            result.Add(new
            {
                chat.chat_id,
                nazvanie = displayName,
                chat.tip,
                chat.avatar_url,
                last_message = string.IsNullOrEmpty(chat.last_message_encrypted) ? null : _encryption.Decrypt(chat.last_message_encrypted),
                last_message_time = chat.last_message_time
            });
        }

        return Ok(result);
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(Guid chatId, int offset = 0, int limit = 50)
    {
        var userId = GetUserId();
        var isMember = await _db.Participants.AnyAsync(p => p.chat_id == chatId && p.user_id == userId);
        if (!isMember) return Forbid();

        var messagesEncrypted = await _db.Messages
            .Where(m => m.chat_id == chatId && !m.is_deleted && !m.is_blocked)
            .OrderByDescending(m => m.created_at)
            .Skip(offset)
            .Take(limit)
            .Select(m => new
            {
                m.id,
                m.sender_id,
                m.username,
                m.reply_to_id,
                reply_to_username = _db.Messages.Where(r => r.id == m.reply_to_id).Select(r => r.username).FirstOrDefault(),
                reply_to_content_encrypted = _db.Messages.Where(r => r.id == m.reply_to_id).Select(r => r.content).FirstOrDefault(),
                content_encrypted = m.content,
                m.content_type,
                m.created_at,
                m.is_blocked,
                reactions = _db.Reactions
                    .Where(r => r.message_id == m.id)
                    .Select(r => new { r.user_id, r.tip })
                    .ToList()
            })
            .ToListAsync();

        var result = messagesEncrypted.Select(m => new
        {
            m.id,
            m.sender_id,
            m.username,
            m.reply_to_id,
            reply_to_username = m.reply_to_username,
            reply_to_content = string.IsNullOrEmpty(m.reply_to_content_encrypted) ? null : _encryption.Decrypt(m.reply_to_content_encrypted),
            content = _encryption.Decrypt(m.content_encrypted),
            m.content_type,
            m.created_at,
            m.is_blocked,
            m.reactions
        }).ToList();

        return Ok(result);
    }
    [HttpPost("private")]
    public async Task<IActionResult> CreatePrivateChat([FromBody] CreatePrivateChatDto dto)
    {
        var userId = GetUserId();

        var existingChatId = await _db.Participants
            .Where(p => p.user_id == userId)
            .Select(p => p.chat_id)
            .Intersect(_db.Participants.Where(p => p.user_id == dto.OtherUserId).Select(p => p.chat_id))
            .Join(_db.Chats, id => id, c => c.chat_id, (id, c) => new { id, c.tip })
            .Where(x => x.tip == "private")
            .Select(x => x.id)
            .FirstOrDefaultAsync();

        //if (existingChatId != null)
        if (existingChatId != Guid.Empty)
          return Ok(new { chatId = existingChatId });

        // Получаем имя другого пользователя
        var otherUserName = await GetUserNameByIdAsync(dto.OtherUserId);
        if (string.IsNullOrEmpty(otherUserName))
            otherUserName = $"User_{dto.OtherUserId}";

        var chat = new Chat
        {
            tip = "private",
            user_id = userId,
            nazvanie = otherUserName, // Сохраняем имя собеседника
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        _db.Chats.Add(chat);
        await _db.SaveChangesAsync();

        _db.Participants.Add(new Participant { chat_id = chat.chat_id, user_id = userId, rol = "member", data_vstuplenia = DateTime.UtcNow });
        _db.Participants.Add(new Participant { chat_id = chat.chat_id, user_id = dto.OtherUserId, rol = "member", data_vstuplenia = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        return Ok(new { chatId = chat.chat_id });
    }

    // Вспомогательный метод для получения имени пользователя
    private async Task<string?> GetUserNameByIdAsync(Guid userId)
    {
        var userServiceUrl = Environment.GetEnvironmentVariable("USER_SERVICE_URL") ?? "http://localhost:5004";
        using var httpClient = new HttpClient();
        try
        {
            // Получаем access_token из текущего запроса
            var accessToken = Request.Cookies["access_token"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                _logger.LogInformation("Added Authorization header with token");
            }
            else
            {
                _logger.LogWarning("No access_token found in cookies");
            }

            httpClient.Timeout = TimeSpan.FromSeconds(10);
            var response = await httpClient.GetAsync($"{userServiceUrl}/api/User/{userId}");
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation($"UserService response for {userId}: Status={response.StatusCode}, Body={content}");

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var successElem) && successElem.GetBoolean())
                {
                    if (root.TryGetProperty("data", out var dataElem))
                    {
                        if (dataElem.TryGetProperty("full_name", out var nameElem))
                        {
                            var name = nameElem.GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                _logger.LogInformation($"Got name '{name}' for user {userId}");
                                return name;
                            }
                        }
                        if (dataElem.TryGetProperty("username", out var usernameElem))
                        {
                            var username = usernameElem.GetString();
                            if (!string.IsNullOrEmpty(username))
                            {
                                _logger.LogInformation($"Got username '{username}' for user {userId}");
                                return username;
                            }
                        }
                    }
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError($"UserService returned 401 Unauthorized for user {userId}. Check that UserService is running and the token is valid.");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get username for user {UserId}", userId);
            return null;
        }
    }

    [HttpPost("group")]
    public async Task<IActionResult> CreateGroupChat([FromBody] CreateGroupChatDto dto)
    {
        var userId = GetUserId();
        var chat = new Chat
        {
            tip = "group",
            nazvanie = dto.Nazvanie,
            opisanie = dto.Opisanie,
            user_id = userId,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        _db.Chats.Add(chat);
        await _db.SaveChangesAsync();

        _db.Participants.Add(new Participant { chat_id = chat.chat_id, user_id = userId, rol = "owner", data_vstuplenia = DateTime.UtcNow });
        foreach (var uId in dto.UserIds)
        {
            _db.Participants.Add(new Participant { chat_id = chat.chat_id, user_id = uId, rol = "member", data_vstuplenia = DateTime.UtcNow });
        }
        await _db.SaveChangesAsync();

        return Ok(new { chatId = chat.chat_id });
    }

    [HttpGet("{chatId}/participants")]
    public async Task<IActionResult> GetParticipants(Guid chatId)
    {
        var userId = GetUserId();
        var isMember = await _db.Participants.AnyAsync(p => p.chat_id == chatId && p.user_id == userId);
        if (!isMember) return Forbid();

        var participants = await _db.Participants
            .Where(p => p.chat_id == chatId)
            .Select(p => new ParticipantDto
            {
                user_id = p.user_id,
                username = p.username,
                rol = p.rol,
                data_vstuplenia = p.data_vstuplenia
            })
            .ToListAsync();

        return Ok(participants);
    }

    [HttpPut("{chatId}/participants/role")]
    public async Task<IActionResult> UpdateParticipantRole(Guid chatId, [FromBody] UpdateRoleRequest request)
    {
        var currentUserId = GetUserId();
        var currentParticipant = await _db.Participants
            .FirstOrDefaultAsync(p => p.chat_id == chatId && p.user_id == currentUserId);
        if (currentParticipant == null) return Forbid();
        if (currentParticipant.rol != "owner") return StatusCode(403, "Only owner can change roles");

        var targetParticipant = await _db.Participants
            .FirstOrDefaultAsync(p => p.chat_id == chatId && p.user_id == request.user_id);
        if (targetParticipant == null) return NotFound("User not in chat");

        if (targetParticipant.rol == "owner" && targetParticipant.user_id != currentUserId)
            return BadRequest("Cannot change owner role");

        targetParticipant.rol = request.rol;
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(Guid messageId)
    {
        var userId = GetUserId();
        var message = await _db.Messages.FirstOrDefaultAsync(m => m.id == messageId);
        if (message == null) return NotFound();
        if (message.sender_id != userId) return Forbid();

        message.is_deleted = true;
        message.content = "[Сообщение удалено]";
        message.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("test-username/{userId}")]
    public async Task<IActionResult> TestGetUserName(Guid userId)
    {
        var name = await GetUserNameByIdAsync(userId);
        return Ok(new { userId, name });
    }



}

