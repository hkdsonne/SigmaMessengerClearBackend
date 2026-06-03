using DotNetEnv;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using UeerService.Data;
using UeerService.Services;

// 1. Загрузка .env
var envPaths = new[]
{
    "../.env",
    ".env",
    "/app/.env"
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
    Console.WriteLine("ERROR! No .env file found, using environment variables from Docker/system");

// 2. Настройка порта
var builder = WebApplication.CreateBuilder(args);
var servicePort = Environment.GetEnvironmentVariable("USER_SERVICE_PORT");
if (!string.IsNullOrEmpty(servicePort))
    builder.WebHost.UseUrls($"http://+:{servicePort}");
Console.WriteLine($"USER Service will run on port: {servicePort}");

// 3. Базовые сервисы
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 4. JWT-аутентификация
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
if (string.IsNullOrEmpty(jwtSecret))
    throw new Exception("JWT_SECRET not configured");
var jwtKey = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
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

// 5. HttpClient для AuthService (оставляем как есть)
var authServiceUrl = Environment.GetEnvironmentVariable("AUTH_SERVICE_URL") ?? "http://localhost:5001";

// 6. Настройка PostgreSQL
var dbHost = Environment.GetEnvironmentVariable("USER_PS_DB_HOST") ?? "localhost";
var dbPort = Environment.GetEnvironmentVariable("USER_PS_DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("USER_PS_DB_NAME") ?? "userdb";
var dbUser = Environment.GetEnvironmentVariable("USER_PS_DB_USER") ?? "postgres";
var dbPass = Environment.GetEnvironmentVariable("USER_PS_DB_PASSWORD") ?? "";

// SSL параметры
var sslMode                = Environment.GetEnvironmentVariable("DB_SSL_MODE") ?? "Require";
var trustServerCertificate = Environment.GetEnvironmentVariable("DB_TRUST_SERVER_CERT") ?? "true";
var clientCertPath         = Environment.GetEnvironmentVariable("DB_USER_CLIENT_CERT_PATH") ?? "/certs/client.pfx";
var caCertPath             = Environment.GetEnvironmentVariable("DB_USER_CA_CERT_PATH") ?? "/certs/ca.crt";

// Строка подключения к постгресу

var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};" +
                       $"SSL Mode={sslMode};Trust Server Certificate={trustServerCertificate};" +
                       $"Include Error Detail=true";

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
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(dataSource);
});


// 7. Регистрация сервиса пользователей (реализация для PostgreSQL уже написана)
builder.Services.AddScoped<IUserService, UserServiceImpl>();
builder.Services.AddHttpClient();

// 8. CORS
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

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// 9. Проверка подключения к PostgreSQL
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (canConnect)
            logger.LogInformation("✅ Successfully connected to PostgreSQL");
        else
            logger.LogError("❌ Cannot connect to PostgreSQL");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ PostgreSQL connection error");
    }
}

// 10. Конвейер
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
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();
