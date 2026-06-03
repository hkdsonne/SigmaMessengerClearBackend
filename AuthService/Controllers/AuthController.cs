using AuthService.DTOs;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using System.Security.Claims;

namespace AuthService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthController> _logger;
    private readonly string RefreshToken = "refresh_token";

    public AuthController(IAuthService authService, IJwtService jwtService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _jwtService = jwtService;
        _logger = logger;
    }
    [HttpPost("send-verification")]
    public async Task<IActionResult> SendVerification([FromBody] RegisterRequest request)
    {
        try
        {
            var result = await _authService.SendVerificationCodeAsync(
                request.email,
                request.username,
                request.password,
                request.device_info
            );

            if (!result)
            {
                return BadRequest(ApiResponse<object>.Fail("Username already exists or email sending failed"));
            }

            return Ok(ApiResponse<object>.Ok(null, "Verification code sent to your email"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send verification error");
            return StatusCode(500, ApiResponse<object>.Fail("Internal server error"));
        }
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyCode([FromBody] VerifyCodeRequest request)
    {
        try
        {
            var result = await _authService.VerifyCodeAndRegisterAsync(request);
            if (result == null)
            {
                return BadRequest(ApiResponse<object>.Fail("Неверный код верификации"));
            }

       
            var accessToken = _jwtService.GenerateAccessToken(result.user_id, result.username);
            SetAccessTokenCookie(accessToken);
            SetRefreshTokenCookie(result.id); // result.id – это session_hash

            return Ok(ApiResponse<AuthResponse>.Ok(result, "Успешная регистрация"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка верификации");
            return StatusCode(500, ApiResponse<object>.Fail("Внутренняя ошибка сервера. Попробуйте позже"));
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        if (result == null)
            return Unauthorized(ApiResponse<object>.Fail("Invalid credentials"));

        var accessToken = _jwtService.GenerateAccessToken(result.user_id, result.username);
        SetAccessTokenCookie(accessToken);
        SetRefreshTokenCookie(result.id);   // result.id – это session_hash

        return Ok(ApiResponse<object>.Ok(null, "Login successful"));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies[RefreshToken];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse<object>.Fail("No refresh token"));

        var newTokens = await _authService.RefreshTokenAsync(refreshToken);
        if (newTokens == null)
            return Unauthorized(ApiResponse<object>.Fail("Invalid or expired refresh token"));

        var newAccessToken = _jwtService.GenerateAccessToken(newTokens.user_id, newTokens.username);
        SetAccessTokenCookie(newAccessToken);
        SetRefreshTokenCookie(newTokens.id);  // обновляем refresh 

        return Ok(ApiResponse<object>.Ok(null, "Token refreshed"));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies[RefreshToken];
        if (!string.IsNullOrEmpty(refreshToken))
            await _authService.RevokeRefreshTokenAsync(refreshToken);

        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete(RefreshToken);
        return Ok(ApiResponse<object>.Ok(null, "Logged out"));
    }

    private void SetAccessTokenCookie(string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Path = "/"
        };
        Response.Cookies.Append("access_token", token, cookieOptions);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<object>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ApiResponse<object>))]
    public IActionResult GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(ApiResponse<object>.Fail("Invalid token"));

        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
            return Unauthorized(ApiResponse<object>.Fail("User not found"));

        var data = new { user_id = userId, username };
        return Ok(ApiResponse<object>.Ok(data, "User info retrieved"));
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(7),
            Path = "/"
        };
        Response.Cookies.Append("refresh_token", refreshToken, cookieOptions);
    }
}
