using ChatService.DTO;
using ChatService.Models;
using ChatService.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatService.Services;

namespace ChatService.Hubs;

public class ChatHub : Hub
{
    private readonly ChatDbContext _db;
    private readonly IJwtService _jwtService;
    private readonly ILogger<ChatHub> _logger;
    private readonly IMessageEncryption _encryption;

    public ChatHub(ChatDbContext db, IJwtService jwtService, ILogger<ChatHub> logger, IMessageEncryption encryption)
    {
        _db = db;
        _jwtService = jwtService;
        _logger = logger;
        _encryption = encryption;
    }

    private (Guid? userId, string? username) GetUserInfo()
    {
        var context = Context.GetHttpContext();
        if (context == null) return (null, null);

        var accessToken = context.Request.Cookies["access_token"];
        if (string.IsNullOrEmpty(accessToken)) return (null, null);

        var principal = _jwtService.ValidateAccessToken(accessToken);
        if (principal == null) return (null, null);

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            return (null, null);

        var username = principal.FindFirst(ClaimTypes.Name)?.Value;
        return (userId, username);
    }

    public override async Task OnConnectedAsync()
    {
        var (userId, _) = GetUserInfo();
        if (userId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");
            _logger.LogInformation("User {UserId} connected", userId.Value);
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinChat(Guid chatId)
    {
        var (userId, username) = GetUserInfo();
        if (!userId.HasValue) return;

        var isMember = await _db.Participants.AnyAsync(p => p.chat_id == chatId && p.user_id == userId.Value);
        if (!isMember) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");

        // Отправить последние 50 сообщений (расшифрованные)
        var messagesEncrypted = await _db.Messages
            .Where(m => m.chat_id == chatId && !m.is_deleted && !m.is_blocked)
            .OrderByDescending(m => m.created_at)
            .Take(50)
            .Select(m => new
            {
                m.id,
                m.chat_id,
                m.sender_id,
                m.username,
                m.content,          // зашифровано
                m.content_type,
                m.created_at,
                m.is_blocked,
                m.reply_to_id,
                reactions = _db.Reactions
                    .Where(r => r.message_id == m.id)
                    .Select(r => new { r.user_id, r.tip })
                    .ToList()
            })
            .ToListAsync();

        var messages = messagesEncrypted.Select(m => new
        {
            m.id,
            m.chat_id,
            m.sender_id,
            m.username,
            content = _encryption.Decrypt(m.content),
            m.content_type,
            m.created_at,
            m.is_blocked,
            m.reply_to_id,
            m.reactions
        }).ToList();

        await Clients.Caller.SendAsync("ReceiveHistory", messages);

        var systemMessage = new
        {
            id = 0L,
            chat_id = chatId,
            sender_id = (long?)null,
            username = "system",
            content = $"{username ?? $"User {userId}"} присоединился к чату",
            content_type = "system",
            created_at = DateTime.UtcNow,
            is_blocked = false,
            reply_to_id = (long?)null,
            reactions = new List<object>()
        };
        await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", systemMessage);
    }

    public async Task LeaveChat(long chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
    }

    public async Task SendMessage(SendMessageDto dto)
    {
        var (userId, username) = GetUserInfo();
        if (!userId.HasValue) return;

        var isMember = await _db.Participants.AnyAsync(p => p.chat_id == dto.ChatId && p.user_id == userId.Value);
        if (!isMember) return;

        // Шифруем содержимое
        var encryptedContent = _encryption.Encrypt(dto.Content);

        var message = new Message
        {
            chat_id = dto.ChatId,
            sender_id = userId.Value,
            username = username ?? "Unknown",
            reply_to_id = dto.ReplyToId,
            content = encryptedContent,
            content_type = dto.ContentType ?? "text",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow,
            is_deleted = false,
            is_blocked = false
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        // Получаем данные ответного сообщения (расшифровываем)
        string replyToUsername = null;
        string replyContent = null;
        if (dto.ReplyToId.HasValue)
        {
            var replyMsg = await _db.Messages.FindAsync(dto.ReplyToId.Value);
            if (replyMsg != null)
            {
                replyToUsername = replyMsg.username;
                replyContent = _encryption.Decrypt(replyMsg.content);
            }
        }

        var messageDto = new
        {
            message.id,
            message.chat_id,
            message.sender_id,
            username = message.username,
            content = _encryption.Decrypt(message.content),
            message.content_type,
            message.created_at,
            message.is_blocked,
            message.reply_to_id,
            reply_to_username = replyToUsername,
            reply_to_content = replyContent,
            reactions = new List<object>()
        };
        await Clients.Group($"chat_{dto.ChatId}").SendAsync("ReceiveMessage", messageDto);
    }

    public async Task AddReaction(Guid messageId, string tip)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;

        var existingReaction = await _db.Reactions
            .FirstOrDefaultAsync(r => r.message_id == messageId && r.user_id == userId.Value);

        if (existingReaction != null)
        {
            _db.Reactions.Remove(existingReaction);
            await _db.SaveChangesAsync();
        }

        var reaction = new Reaction
        {
            message_id = messageId,
            user_id = userId.Value,
            tip = tip,
            vremya_postavili = DateTime.UtcNow
        };
        _db.Reactions.Add(reaction);
        await _db.SaveChangesAsync();

        var msg = await _db.Messages.FindAsync(messageId);
        if (msg != null)
            await Clients.Group($"chat_{msg.chat_id}").SendAsync("ReactionAdded", new { messageId, userId = userId.Value, tip });
    }

    public async Task RemoveReaction(Guid messageId, string tip)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;

        var reaction = await _db.Reactions.FirstOrDefaultAsync(r => r.message_id == messageId && r.user_id == userId.Value && r.tip == tip);
        if (reaction != null)
        {
            _db.Reactions.Remove(reaction);
            await _db.SaveChangesAsync();
            var msg = await _db.Messages.FindAsync(messageId);
            if (msg != null)
                await Clients.Group($"chat_{msg.chat_id}").SendAsync("ReactionRemoved", new { messageId, userId = userId.Value, tip });
        }
    }

    public async Task MarkRead(Guid chatId, Guid lastReadMessageId)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;

        var participant = await _db.Participants.FirstOrDefaultAsync(p => p.chat_id == chatId && p.user_id == userId.Value);
        if (participant != null && participant.poslednee_prochitannoe < lastReadMessageId)
        {
            participant.poslednee_prochitannoe = lastReadMessageId;
            await _db.SaveChangesAsync();
        }
    }

    public async Task Typing(Guid chatId)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;
        await Clients.Group($"chat_{chatId}").SendAsync("UserTyping", new { chatId, userId = userId.Value });
    }

    public async Task GetParticipants(Guid chatId)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;

        var isMember = await _db.Participants.AnyAsync(p => p.chat_id == chatId && p.user_id == userId.Value);
        if (!isMember) return;

        var participants = await _db.Participants
            .Where(p => p.chat_id == chatId)
            .Select(p => new { p.user_id, p.rol, p.data_vstuplenia })
            .ToListAsync();
        await Clients.Caller.SendAsync("ParticipantsList", participants);
    }

    public async Task ChangeRole(Guid chatId, Guid targetUserId, string newRole)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;

        var currentParticipant = await _db.Participants
            .FirstOrDefaultAsync(p => p.chat_id == chatId && p.user_id == userId.Value);
        if (currentParticipant == null || currentParticipant.rol != "owner") return;

        var targetParticipant = await _db.Participants
            .FirstOrDefaultAsync(p => p.chat_id == chatId && p.user_id == targetUserId);
        if (targetParticipant == null) return;

        if (targetParticipant.rol == "owner" && targetParticipant.user_id != userId.Value) return;

        targetParticipant.rol = newRole;
        await _db.SaveChangesAsync();

        await Clients.Group($"chat_{chatId}").SendAsync("ParticipantRoleChanged", new { chatId, user_id = targetUserId, rol = newRole });
    }

    public async Task DeleteMessage(Guid messageId)
    {
        var (userId, _) = GetUserInfo();
        if (!userId.HasValue) return;

        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.id == messageId);
        if (message == null) return;

        if (message.sender_id != userId.Value) return;

        message.is_deleted = true;
        message.content = "[Сообщение удалено]";
        message.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Clients.Group($"chat_{message.chat_id}").SendAsync("MessageDeleted", new { messageId, chatId = message.chat_id });
    }
}

