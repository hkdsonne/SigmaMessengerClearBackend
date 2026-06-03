using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SigmaTests.Helpers;

public static class TokenHelper
{
    private static readonly string _jwtSecret;

    static TokenHelper()
    {
        // Сначала пробуем из переменной окружения
        _jwtSecret = Environment.GetEnvironmentVariable("TEST_JWT_SECRET");
        if (!string.IsNullOrEmpty(_jwtSecret))
            return;

        // Ищем .env.tests в папке SigmaTests (на уровень выше bin)
        var baseDir = AppDomain.CurrentDomain.BaseDirectory; // .../SigmaTests/bin/Debug/net10.0/
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "../../../../")); // поднимаемся до корня решения
        var envPath = Path.Combine(solutionRoot, "SigmaTests", ".env.tests");

        // Альтернативный путь, если не нашли
        if (!File.Exists(envPath))
            envPath = Path.Combine(baseDir, "../../../.env.tests");
        if (!File.Exists(envPath))
            envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env.tests");

        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("TEST_JWT_SECRET="))
                {
                    _jwtSecret = line.Substring("TEST_JWT_SECRET=".Length).Trim();
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(_jwtSecret))
            throw new InvalidOperationException(
                "TEST_JWT_SECRET not found. Create .env.tests in SigmaTests folder with TEST_JWT_SECRET=...");
    }

    // Остальные методы (GenerateTestToken, ValidateTestToken) без изменений
    public static string GenerateTestToken(Guid userId, string username = "testuser")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSecret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public static bool ValidateTestToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSecret);
        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }
}