using Microsoft.EntityFrameworkCore;
using UeerService.Data;
using UeerService.DTOs;
using UeerService.Models;

namespace UeerService.Services;

public class UserServiceImpl : IUserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserServiceImpl> _logger;

    public UserServiceImpl(AppDbContext db, ILogger<UserServiceImpl> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserInfo?> GetUserInfoAsync(Guid userId)
    {
        return await _db.UserInfos.AsNoTracking().FirstOrDefaultAsync(u => u.user_id == userId);
    }

    public async Task<bool> UpdateUserInfoAsync(Guid userId, UpdateUserInfoRequest request)
    {
        var user = await _db.UserInfos.FirstOrDefaultAsync(u => u.user_id == userId);
        if (user == null) return false;

        user.full_name = request.full_name;
        user.avatar_url = request.avatar_url;
        user.bio = request.bio;
        user.email = request.email;
        user.updated_at = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<UserSettings?> GetSettingsAsync(Guid userId)
    {
        return await _db.UserSettings.AsNoTracking().FirstOrDefaultAsync(s => s.user_id == userId);
    }

    public async Task<bool> UpdateSettingsAsync(Guid userId, UpdateSettingsRequest request)
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.user_id == userId);
        if (settings == null)
        {
            settings = new UserSettings
            {
                user_id = userId,
                notifications_enabled = request.notifications_enabled ?? true,
                theme = request.theme ?? "light",
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };
            _db.UserSettings.Add(settings);
        }
        else
        {
            settings.notifications_enabled = request.notifications_enabled ?? settings.notifications_enabled;
            settings.theme = request.theme ?? settings.theme;
            settings.updated_at = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<UserInfoResponse?> GetFullUserInfoAsync(Guid userId)
    {
        var userInfo = await GetUserInfoAsync(userId);
        if (userInfo == null) return null;

        var settings = await GetSettingsAsync(userId);
        return new UserInfoResponse
        {
            user_id = userInfo.user_id,
            full_name = userInfo.full_name,
            avatar_url = userInfo.avatar_url,
            bio = userInfo.bio,
            last_activity_at = userInfo.last_activity_at,
            is_active = userInfo.is_active,
            is_blocked = userInfo.is_blocked,
            email = userInfo.email,
            created_at = userInfo.created_at,
            settings = new UserSettingsResponse
            {
                notifications_enabled = settings?.notifications_enabled ?? true,
                theme = settings?.theme ?? "light"
            }
        };
    }

    public async Task<bool> UpdateActivityAsync(Guid userId)
    {
        var user = await _db.UserInfos.FirstOrDefaultAsync(u => u.user_id == userId);
        if (user == null) return false;

        user.last_activity_at = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> BlockUserAsync(Guid userId)
    {
        var user = await _db.UserInfos.FirstOrDefaultAsync(u => u.user_id == userId);
        if (user == null) return false;

        user.is_blocked = true;
        user.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnblockUserAsync(Guid userId)
    {
        var user = await _db.UserInfos.FirstOrDefaultAsync(u => u.user_id == userId);
        if (user == null) return false;

        user.is_blocked = false;
        user.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsUserBlockedAsync(Guid userId)
    {
        var user = await GetUserInfoAsync(userId);
        return user?.is_blocked ?? false;
    }

    public async Task<bool> CreateOrUpdateUserInfoAsync(Guid userId, UpdateUserInfoRequest request)
    {
        var existing = await GetUserInfoAsync(userId);
        if (existing == null)
        {
            var newUser = new UserInfo
            {
                user_id = userId,
                full_name = request.full_name,
                avatar_url = request.avatar_url,
                bio = request.bio ?? "Привет, я в ТЧК!",
                email = request.email,
                last_activity_at = DateTime.UtcNow,
                is_active = true,
                is_blocked = false,
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };
            _db.UserInfos.Add(newUser);
            await _db.SaveChangesAsync();

            await CreateDefaultSettingsAsync(userId);
            return true;
        }
        else
        {
            return await UpdateUserInfoAsync(userId, request);
        }
    }

    private async Task CreateDefaultSettingsAsync(Guid userId)
    {
        var existing = await GetSettingsAsync(userId);
        if (existing != null) return;

        var settings = new UserSettings
        {
            user_id = userId,
            notifications_enabled = true,
            theme = "light",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        _db.UserSettings.Add(settings);
        await _db.SaveChangesAsync();
    }

    public async Task<List<UserSummaryDto>> GetAllUsersAsync()
    {
        return await _db.UserInfos
            .AsNoTracking()
            .Select(u => new UserSummaryDto
            {
                user_id = u.user_id,
                username = u.full_name,
                avatar_url = u.avatar_url,
                bio = u.bio
            })
            .ToListAsync();
    }

    public async Task<UserProfileDto?> GetUserByUsernameAsync(string username)
    {
        var user = await _db.UserInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.full_name == username);
        if (user == null) return null;

        return new UserProfileDto
        {
            username = user.full_name,
            avatar_url = user.avatar_url,
            bio = user.bio,
            last_activity_at = user.last_activity_at
        };
    }
}