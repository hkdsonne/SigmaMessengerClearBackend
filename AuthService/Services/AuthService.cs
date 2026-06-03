using AuthService.Data;
using AuthService.DTOs;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuthService.Services;

public class AuthServiceImpl : IAuthService
{
    private readonly AuthDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthServiceImpl> _logger;
    private readonly HttpClient _httpClient;
    private readonly VerificationCache _verificationCache;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthServiceImpl(
        AuthDbContext context,
        IConfiguration configuration,
        ILogger<AuthServiceImpl> logger,
        IHttpClientFactory httpClientFactory,
        VerificationCache verificationCache
        )
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClientFactory = httpClientFactory;
        _verificationCache = verificationCache;
    }

    public async Task<bool> SendVerificationCodeAsync(string email, string username, string password, string deviceInfo)
    {
        try
        {

            // 1. Проверяем, не существует ли уже пользователь
            var existingUser = await _context.users
                .FirstOrDefaultAsync(u => u.username == username);

            if (existingUser != null)
            {
                _logger.LogWarning("Username {Username} already exists", username);
                return false;
            }

            // 2. Проверяем, не зарегистрирован ли уже email
            var existingEmail = await _context.users_mail
                .FirstOrDefaultAsync(m => m.email == email);
            if (existingEmail != null)
            {
                _logger.LogWarning("Email {Email} already registered", email);
                return false;
            }

            // 3. Генерируем 6-значный код
            var code = GenerateVerificationCode();

            // 4. Сохраняем сырой пароль в кэш
            _verificationCache.Store(email, new VerificationData
            {
                code = code,
                username = username,
                raw_password = password,
                device_info = deviceInfo,
                expires_at = DateTime.UtcNow.AddMinutes(10)
            });
      

            // 5. Отправляем код на email через MailService (порт 5005)
            var emailServiceUrl = _configuration["MailService:Url"] ?? "http://localhost:5005";
            var requestBody = new
            {
                email = email,
                code = code,
                username = username
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var handler = new HttpClientHandler();

            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromSeconds(
                _configuration.GetValue<int>("MailService:TimeoutSeconds", 10));

            var response = await httpClient.PostAsync(
                $"{emailServiceUrl}/api/email/send-verification",
                content
            );

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Verification code sent successfully to {Email}", email);
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("MailService returned error: {StatusCode} - {Error}",
                    response.StatusCode, error);
                _verificationCache.Remove(email);
                return false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cannot connect to MailService. Make sure MailService is running at {Url}",
                _configuration["MailService:Url"]);
            _verificationCache.Remove(email);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout connecting to MailService");
            _verificationCache.Remove(email);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending verification code to {Email}", email);
            _verificationCache.Remove(email);
            return false;
        }
    }

    public async Task<AuthResponse?> VerifyCodeAndRegisterAsync(VerifyCodeRequest request)
    {
        try
        {
            var verification = _verificationCache.Get(request.email);

            if (verification == null)
            {
                _logger.LogWarning("No verification found for {Email}", request.email);
                return null;
            }

            if (verification.code != request.code)
            {
                _logger.LogWarning("Invalid verification code for {Email}", request.email);
                return null;
            }

            var existingUser = await _context.users
                .FirstOrDefaultAsync(u => u.username == request.username);

            if (existingUser != null)
            {
                _logger.LogWarning("Username {Username} already exists", request.username);
                return null;
            }

            var existingEmail = await _context.users_mail
                .FirstOrDefaultAsync(m => m.email == request.email);

            if (existingEmail != null)
            {
                _logger.LogWarning("Email {Email} already exists", request.email);
                return null;
            }

            var user = new User
            {
                username = request.username,
                password = verification.raw_password,
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };

            _context.users.Add(user);
            await _context.SaveChangesAsync();

            var userMail = new UserMail
            {
                user_id = user.id,
                email = request.email
            };
            _context.users_mail.Add(userMail);
            await _context.SaveChangesAsync();

            _verificationCache.Remove(request.email);

            var session = await CreateSessionAsync(user.id, request.device_info);

            // Вызов UserService (порт 5004)
            var userServiceUrl = _configuration["UserService:Url"] ?? "http://localhost:5004";
            _httpClientFactory.CreateClient("UserService");

            var initRequest = new { user_id = user.id, username = user.username };
            var content = new StringContent(JsonSerializer.Serialize(initRequest), Encoding.UTF8, "application/json");

            try
            {
                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(userServiceUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                var response = await httpClient.PostAsync("/api/User/internal/init", content);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("UserService init returned {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to call UserService init");
            }

            return new AuthResponse
            {
                user_id = user.id,
                username = user.username,
                id = session.session_hash,
                expires_at = session.expires_at
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying code for {Email}", request.email);
            return null;
        }
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        try
        {
            var sql = @"SELECT * FROM users WHERE username = {0} AND password = crypt({1}, password)"; // NOSONAR

            var user = await _context.users
                .FromSqlRaw(sql, request.username, request.password)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning("Invalid login attempt for username: {Username}", request.username);
                return null;
            }

            var session = await CreateSessionAsync(user.id, request.device_info);

            return new AuthResponse
            {
                user_id = user.id,
                username = user.username,
                id = session.session_hash,
                expires_at = session.expires_at
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for username: {Username}", request.username);
            return null;
        }
    }

    public async Task<bool> LogoutAsync(string sessionHash)
    {
        var session = await _context.sessions
            .FirstOrDefaultAsync(s => s.session_hash == sessionHash);

        if (session == null)
            return false;

        _context.sessions.Remove(session);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ValidateSessionAsync(string sessionHash)
    {
        var session = await _context.sessions
            .FirstOrDefaultAsync(s => s.session_hash == sessionHash);

        if (session == null)
            return false;

        if (session.expires_at < DateTime.UtcNow)
        {
            _context.sessions.Remove(session);
            await _context.SaveChangesAsync();
            return false;
        }

        return true;
    }

    public async Task<Session?> GetSessionAsync(string sessionHash)
    {
        return await _context.sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.session_hash == sessionHash);
    }

    private async Task<Session> CreateSessionAsync(Guid userId, string deviceInfo)
    {
        var sessionHash = GenerateSessionHash();
        var expiresAt = DateTime.UtcNow.AddDays(
            _configuration.GetValue<int>("Auth:SessionDurationDays", 7));

        var session = new Session
        {
            user_id = userId,
            device_info = deviceInfo,
            session_hash = sessionHash,
            created_at = DateTime.UtcNow,
            expires_at = expiresAt
        };

        _context.sessions.Add(session);
        await _context.SaveChangesAsync();

        return session;
    }

    public async Task<AuthResponse?> RefreshTokenAsync(string refreshToken)
    {
        var session = await _context.sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.session_hash == refreshToken && s.expires_at > DateTime.UtcNow);
        if (session == null)
            return null;

        return new AuthResponse
        {
            user_id = session.user_id,
            username = session.User.username,
            id = session.session_hash,
            expires_at = session.expires_at
        };
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
    {
        var session = await _context.sessions.FirstOrDefaultAsync(s => s.session_hash == refreshToken);
        if (session == null)
            return false;

        _context.sessions.Remove(session);
        await _context.SaveChangesAsync();
        return true;
    }

    private static string GenerateSessionHash()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static  string GenerateVerificationCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }
}