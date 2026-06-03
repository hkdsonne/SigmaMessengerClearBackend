using ChatService.Data;
using ChatService.Hubs;
using ChatService.Models;
using ChatService.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Security.Cryptography.X509Certificates;
using System.Text;


// 1. Загрузка .env из нескольких возможных мест
var envPaths = new[]
{
    "../.env",           // Локальная разработка (из bin/Debug/net10.0/)
    ".env",              // Docker (файл скопирован в контейнер)
    "/app/.env"          // Docker (явный путь)
};

bool envLoaded = false;
foreach (var path in envPaths)
{
    if (File.Exists(path))
    {
        Env.Load(path);
        Console.WriteLine($"Loaded .env from: {path}");
        envLoaded = true;
        break;
    }
}
if (!envLoaded)
{
    Console.WriteLine("ERROR! No .env file found, using environment variables from Docker/system");
}

// 2. Настройка приложения
var builder = WebApplication.CreateBuilder(args);

// Логирование
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Порт сервиса
var servicePort = Environment.GetEnvironmentVariable("CHATS_SERVICE_PORT") ?? "5004";
builder.WebHost.UseUrls($"http://+:{servicePort}");
Console.WriteLine($"CHATS Service will run on port: {servicePort}");

// 3. Чтение параметров БД (с дефолтами)
var dbHost = Environment.GetEnvironmentVariable("CHAT_DB_HOST") ?? "localhost";
var dbPort = Environment.GetEnvironmentVariable("CHAT_DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("CHAT_DB_NAME") ?? "chatdb";
var dbUser = Environment.GetEnvironmentVariable("CHAT_DB_USER") ?? "postgres";
var dbPass = Environment.GetEnvironmentVariable("CHAT_DB_PASSWORD") ?? "";

// SSL параметры
var sslMode                = Environment.GetEnvironmentVariable("DB_SSL_MODE") ?? "Require";
var trustServerCertificate = Environment.GetEnvironmentVariable("DB_TRUST_SERVER_CERT") ?? "true";
var clientCertPath         = Environment.GetEnvironmentVariable("DB_CHAT_CLIENT_CERT_PATH") ?? "/certs/client.pfx";
var caCertPath             = Environment.GetEnvironmentVariable("DB_CHAT_CA_CERT_PATH") ?? "/certs/ca.crt";

// Строка подключения
var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};" +
                       $"SSL Mode={sslMode};Trust Server Certificate={trustServerCertificate};" +
                       $"Include Error Detail=true";

Console.WriteLine($"CHATS PostgreSQL: {dbHost}:{dbPort}/{dbName} (SSL Mode: {sslMode})");

// Загрузка сертификатов
X509Certificate2? clientCert = null;
X509Certificate2? caCert = null;

if (File.Exists(clientCertPath))
{
    clientCert = X509CertificateLoader.LoadPkcs12FromFile(clientCertPath, null);
    Console.WriteLine($"Client certificate loaded from: {clientCertPath}");
}
else
{
    Console.WriteLine($"Client certificate not found at: {clientCertPath}");
}

if (File.Exists(caCertPath))
{
    caCert = X509CertificateLoader.LoadCertificateFromFile(caCertPath);
    Console.WriteLine($"CA certificate loaded from: {caCertPath}");
}

// Создаём DataSource с настройками SSL
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

// Клиентский сертификат
if (clientCert != null)
{
    dataSourceBuilder.UseClientCertificate(clientCert);
}

// Валидация сервера через CA
if (caCert != null)
{
    // Настройки валидации для сборки датасурса
    dataSourceBuilder.UseUserCertificateValidationCallback((sender, certificate, chain, errors) =>
    {
        if (errors == System.Net.Security.SslPolicyErrors.None)
            return true;

        using var customChain = new X509Chain();
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(caCert);
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        return customChain.Build(new X509Certificate2(certificate!));
    });
}

var dataSource = dataSourceBuilder.Build();

// Передаём готовый DataSource в EF Core
builder.Services.AddDbContext<ChatDbContext>(options =>
{
    options.UseNpgsql(dataSource);
});




// 4. SignalR + базовые сервисы
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// 5. JWT аутентификация (из куки access_token)
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? throw new Exception("JWT_SECRET not configured");
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Cookies["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

var encryptionKey = Environment.GetEnvironmentVariable("CHAT_ENCRYPTION_KEY");
if (string.IsNullOrEmpty(encryptionKey))
    throw new Exception("CHAT_ENCRYPTION_KEY not configured");

builder.Services.AddSingleton<IMessageEncryption>(new MessageEncryption(encryptionKey));

// 6. Собственные сервисы (JwtService, AuthClient)
builder.Services.AddSingleton<IJwtService, JwtService>();

var authServiceUrl = Environment.GetEnvironmentVariable("AUTH_SERVICE_IP") ?? "http://localhost:5001";

builder.Services.AddHttpClient("AuthService", client =>
{
    client.BaseAddress = new Uri(authServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// 7. CORS для фронтенда (SignalR требует AllowCredentials)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("https://tchk.site", "http://tchk.site", "https://www.tchk.site", "http://www.tchk.site", "http://localhost:5173", "http://localhost:3000", "https://localhost:3000", "http://localhost:3001", "https://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 8. Создание глобального чата (chat_id = 1) при первом запуске
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();

    // Фиксированный UUID для глобального чата
    var globalChatId = new Guid("11111111-1111-1111-1111-111111111111");
    var globalChat = await db.Chats.FirstOrDefaultAsync(c => c.tip == "global");
    if (globalChat == null)
    {
        var chat = new Chat
        {
            chat_id = globalChatId,
            tip = "global",
            nazvanie = "Общий чат",
            opisanie = "Главный чат для всех пользователей",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow,
            user_id = new Guid("3c3d9c4f-892f-4915-b443-0c3a85ea3b2f")
        };
        db.Chats.Add(chat);
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Global chat created.");
    }
}

// 9. Конвейер middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();   
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub").AllowAnonymous(); 

app.Run();app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));
