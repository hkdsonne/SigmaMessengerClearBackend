using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UeerService.DTOs;
using UeerService.Services;
using Shared.Models;

namespace UeerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // все методы требуют аутентификации
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<UserController> _logger;

        public UserController(IUserService userService, ILogger<UserController> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        // Вспомогательный метод получения текущего userId из JWT
        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                _logger.LogWarning("Не удалось получить userId из токена");
                return null;
            }
            return userId;
        }

        // Эндпоинты для текущего пользователя

        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized(ApiResponse<object>.Fail("Not authenticated"));

            var userIdUlong = userId.Value;
            var userInfo = await _userService.GetFullUserInfoAsync(userIdUlong);

            if (userInfo == null)
            {
                // Берём имя пользователя из токена (ClaimTypes.Name)
                var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "Пользователь";
                await _userService.CreateOrUpdateUserInfoAsync(userIdUlong, new UpdateUserInfoRequest
                {
                    full_name = username,
                    avatar_url = null,
                    bio = null
                });
                userInfo = await _userService.GetFullUserInfoAsync(userIdUlong);
            }

            return Ok(ApiResponse<UserInfoResponse>.Ok(userInfo));
        }

        [HttpPut("me/info")]
        public async Task<IActionResult> UpdateMyInfo([FromBody] UpdateUserInfoRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized(ApiResponse<object>.Fail("Not authenticated"));

            var userIdUlong = userId.Value;

            var isBlocked = await _userService.IsUserBlockedAsync(userIdUlong);
            if (isBlocked)
                return StatusCode(403, ApiResponse<object>.Fail("User is blocked"));

            var result = await _userService.CreateOrUpdateUserInfoAsync(userIdUlong, request);
            if (!result)
                return BadRequest(ApiResponse<object>.Fail("Failed to update user info"));

            await _userService.UpdateActivityAsync(userIdUlong);
            return Ok(ApiResponse<object>.Ok(null, "User info updated successfully"));
        }

        [HttpGet("me/settings")]
        public async Task<IActionResult> GetMySettings()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized(ApiResponse<object>.Fail("Not authenticated"));

            var userIdUlong = userId.Value;
            var settings = await _userService.GetSettingsAsync(userIdUlong);
            if (settings == null)
            {
                // Создаём настройки по умолчанию
                await _userService.UpdateSettingsAsync(userIdUlong, new UpdateSettingsRequest
                {
                    notifications_enabled = true,
                    theme = "light"
                });
                // Повторно получаем настройки после создания
                settings = await _userService.GetSettingsAsync(userIdUlong);
                if (settings == null)
                {
                    _logger.LogError("Failed to create default settings for user {UserId}", userIdUlong);
                    return StatusCode(500, ApiResponse<object>.Fail("Failed to initialize settings"));
                }
            }

            return Ok(ApiResponse<UserSettingsResponse>.Ok(new UserSettingsResponse
            {
                notifications_enabled = settings.notifications_enabled,
                theme = settings.theme
            }));
        }

        [HttpPut("me/settings")]
        public async Task<IActionResult> UpdateMySettings([FromBody] UpdateSettingsRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized(ApiResponse<object>.Fail("Not authenticated"));

            var userIdUlong = userId.Value;

            var isBlocked = await _userService.IsUserBlockedAsync(userIdUlong);
            if (isBlocked)
                return StatusCode(403, ApiResponse<object>.Fail("User is blocked"));

            var result = await _userService.UpdateSettingsAsync(userIdUlong, request);
            if (!result)
                return BadRequest(ApiResponse<object>.Fail("Failed to update settings"));

            return Ok(ApiResponse<object>.Ok(null, "Settings updated successfully"));
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var users = await _userService.GetAllUsersAsync();
                return Ok(ApiResponse<List<UserSummaryDto>>.Ok(users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users list");
                return StatusCode(500, ApiResponse<object>.Fail("Internal server error"));
            }
        }

        [HttpGet("by-username/{username}")]
        public async Task<IActionResult> GetUserByUsername(string username)
        {
            try
            {
                var user = await _userService.GetUserByUsernameAsync(username);
                if (user == null)
                    return NotFound(ApiResponse<object>.Fail("User not found"));

                return Ok(ApiResponse<UserProfileDto>.Ok(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by username {Username}", username);
                return StatusCode(500, ApiResponse<object>.Fail("Internal server error"));
            }
        }

        [HttpPost("internal/init")]
        [AllowAnonymous] // Для вызова из AuthService, можно позже защитить внутренним ключом
        public async Task<IActionResult> InitUser([FromBody] InitUserRequest request)
        {
            try
            {
                var userIdUlong = request.user_id;
                var existing = await _userService.GetUserInfoAsync(userIdUlong);
                if (existing != null)
                    return Ok(new { success = true, message = "User already exists" });

                await _userService.CreateOrUpdateUserInfoAsync(userIdUlong, new UpdateUserInfoRequest
                {
                    full_name = request.username,
                    avatar_url = null,
                    bio = null
                });
                return Ok(new { success = true, message = "User initialized" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing user {UserId}", request.user_id);
                return StatusCode(500, new { success = false, message = "Internal error" });
            }
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUser(Guid userId)
        {
            try
            {
                var isBlocked = await _userService.IsUserBlockedAsync(userId);
                if (isBlocked)
                {
                    return Ok(ApiResponse<object>.Ok(new { is_blocked = true, message = "User is blocked" }));
                }

                var userInfo = await _userService.GetFullUserInfoAsync(userId);
                if (userInfo == null)
                    return NotFound(ApiResponse<object>.Fail("User not found"));

                return Ok(ApiResponse<UserInfoResponse>.Ok(userInfo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user {UserId}", userId);
                return StatusCode(500, ApiResponse<object>.Fail("Internal server error"));
            }
        }

    }
}