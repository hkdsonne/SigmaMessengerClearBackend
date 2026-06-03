using AuthService;
using AuthService.Data;
// using AuthService.RabbitMq;
using AuthService.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Npgsql;
using System.Security.Cryptography.X509Certificates;

// Для запуска через докер нужно передавать переменные окружения через докеркомпоуз
// Передам три возможных варианта, подставится нужный
var envPaths = new[]
{
    "../.env",           // Локальная разработка (из bin/Debug/net10.0/)
    ".env",              // Docker (файл скопирован в контейнер)
    "/app/.env"          // Docker (явный путь)
};
//
// Поиск подходящего пути к файлу с переменными окружения
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
//
if (!envLoaded)
{
    Console.WriteLine("ERROR! No .env file found, using environment variables from Docker/system");
}


var builder = WebApplication.CreateBuilder(args);


// Будьте людьми не трогайте моё логирование(((
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Читаем порт из переменной окружения
var authServicePort = Environment.GetEnvironmentVariable("AUTH_SERVICE_PORT");

// Устанавливаем URL с нужным портом
builder.WebHost.UseUrls($"http://+:{authServicePort}");

Console.WriteLine($"AUTH Service will run on port: {authServicePort}");

// Для настройки работы с докеркомпоузом решил отказаться от переменных в appsettings.json
var dbHost = Environment.GetEnvironmentVariable("POSTGRESQL_HOST");
var dbPort = Environment.GetEnvironmentVariable("POSTGRESQL_PORT");
var dbName = Environment.GetEnvironmentVariable("POSTGRESQL_DATABASE_USERS");
var dbUser = Environment.GetEnvironmentVariable("POSTGRESQL_USERNAME_USERS");
var dbPass = Environment.GetEnvironmentVariable("POSTGRESQL_PASSWORD_USERS");

// SSL параметры
var sslMode                = Environment.GetEnvironmentVariable("DB_SSL_MODE") ?? "Require";
var trustServerCertificate = Environment.GetEnvironmentVariable("DB_TRUST_SERVER_CERT") ?? "true";
var clientCertPath         = Environment.GetEnvironmentVariable("DB_AUTH_CLIENT_CERT_PATH") ?? "/certs/client.pfx";
var caCertPath             = Environment.GetEnvironmentVariable("DB_AUTH_CA_CERT_PATH") ?? "/certs/ca.crt";


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
builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseNpgsql(dataSource);
});



// Получение адреса почтового сервиса

var mailServiceUrl = Environment.GetEnvironmentVariable("MAIL_SERVICE_IP");
Console.WriteLine($"Mail Service ip is: {mailServiceUrl}");
builder.Configuration["MailService:Url"] = mailServiceUrl;

var userServiceUrl = Environment.GetEnvironmentVariable("USER_SERVICE_IP");
if (string.IsNullOrEmpty(userServiceUrl))
{
    Console.WriteLine("ERROR: USER_SERVICE_URL is not configured in .env");
}
builder.Services.AddHttpClient("UserService", client =>
{
    client.BaseAddress = new Uri(userServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
if (string.IsNullOrEmpty(jwtSecret))
    throw new Exception("JWT_SECRET is not configured in .env");
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


// Регистрируем сервисы
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthServiceImpl>();
builder.Services.AddSingleton<VerificationCache>(); // Кэш для кодов (один на все приложение)
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins("https://tchk.site", "http://tchk.site", "https://www.tchk.site", "http://www.tchk.site", "http://localhost:5173", "http://localhost:3000", "https://localhost:3000", "http://localhost:3001", "https://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    try
    {
        await dbContext.Database.EnsureCreatedAsync();
        Console.WriteLine("Database ensured created");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database error: {ex.Message}");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();
