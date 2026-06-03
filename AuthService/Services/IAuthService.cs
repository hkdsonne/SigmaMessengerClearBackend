using AuthService.DTOs;
using AuthService.Models;

namespace AuthService.Services;

public interface IAuthService
{
    Task<bool> SendVerificationCodeAsync(string email, string username, string password, string deviceInfo);
    Task<AuthResponse?> VerifyCodeAndRegisterAsync(VerifyCodeRequest request);
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<bool> LogoutAsync(string sessionHash);
    Task<bool> ValidateSessionAsync(string sessionHash);
    Task<Session?> GetSessionAsync(string sessionHash);
    Task<AuthResponse?> RefreshTokenAsync(string refreshToken);
    Task<bool> RevokeRefreshTokenAsync(string refreshToken);
}