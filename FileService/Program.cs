using System.Text;
using DotNetEnv;
using FileService.Data;
using FileService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using AspNetCoreRateLimit;
using Npgsql;
using System.Security.Cryptography.X509Certificates;

// Load .env file
var envPaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    "../.env",
    ".env"
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
    Console.WriteLine($"ERROR! No .env file found");
}

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for larger files
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50 MB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Logging.AddFilter("AspNetCoreRateLimit", LogLevel.Warning);

// Добавляем MemoryCache для Rate Limiting
builder.Services.AddMemoryCache();

// Rate Limiting
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddHttpContextAccessor();

// Read environment variables
var servicePort = Environment.GetEnvironmentVariable("FILE_SERVICE_PORT") ?? "5006";
builder.WebHost.UseUrls($"http://+:{servicePort}");
Console.WriteLine($"FILE Service will run on port: {servicePort}");

// URL сервиса чатов (для проверки участников)
var chatServiceUrl = Environment.GetEnvironmentVariable("CHATS_SERVICE_IP") ?? "http://localhost:5002";

var dbHost = Environment.GetEnvironmentVariable("FILE_PS_DB_HOST") ?? "localhost";
var dbPort = Environment.GetEnvironmentVariable("FILE_PS_DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("FILE_PS_DB_NAME") ?? "db_to_file_db";
var dbUser = Environment.GetEnvironmentVariable("FILE_PS_DB_USER") ?? "postgres";
var dbPass = Environment.GetEnvironmentVariable("FILE_PS_DB_PASSWORD") ?? "";

// SSL параметры
var sslMode = Environment.GetEnvironmentVariable("DB_SSL_MODE") ?? "Require";
var trustServerCertificate = Environment.GetEnvironmentVariable("DB_TRUST_SERVER_CERT") ?? "true";
var clientCertPath = Environment.GetEnvironmentVariable("DB_FILE_CLIENT_CERT_PATH") ?? "/certs/client.pfx";
var caCertPath = Environment.GetEnvironmentVariable("DB_FILE_CA_CERT_PATH") ?? "/certs/ca.crt";

// Строка подключения к постгресу
var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};" +
                       $"SSL Mode={sslMode};Trust Server Certificate={trustServerCertificate};" +
                       $"Include Error Detail=true";

Console.WriteLine($"String to connect PostgreSQL: {dbHost}:{dbPort}/{dbName} with SSL Mode: {sslMode}");

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

if (clientCert != null)
{
    dataSourceBuilder.UseClientCertificate(clientCert);
}

if (caCert != null)
{
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

builder.Services.AddDbContext<FileDbContext>(options =>
{
    options.UseNpgsql(dataSource);
});

// JWT Configuration
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")?.Trim();
if (string.IsNullOrEmpty(jwtSecret))
{
    Console.WriteLine("ERROR: JWT_SECRET is not configured in .env");
    throw new Exception("JWT_SECRET is not configured");
}

var key = Encoding.UTF8.GetBytes(jwtSecret);

// JWT Authentication
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
                // 1. Сначала пробуем взять из заголовка Authorization
                var authorization = context.Request.Headers["Authorization"].FirstOrDefault();
                if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
                {
                    context.Token = authorization.Substring("Bearer ".Length).Trim();
                    return Task.CompletedTask;
                }

                // 2. Если нет — берем из куки access_token
                var accessToken = context.Request.Cookies["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger configuration - обновляем для поддержки GUID в postId
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "File Service API",
        Version = "v1",
        Description = "API для управления файлами с поддержкой сжатия"
    });

    c.OperationFilter<FileUploadOperation>();

    // Настройка для GUID параметров
    c.MapType<Guid>(() => new OpenApiSchema
    {
        Type = "string",
        Format = "uuid",
        Example = OpenApiAnyFactory.CreateFromJson("\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"")
    });
});

builder.Services.AddSingleton<IStorageService, YandexStorageService>();

builder.Services.AddHttpClient("ChatService", client =>
{
    client.BaseAddress = new Uri(chatServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient();

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

app.UseIpRateLimiting();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FileDbContext>();
    try
    {
        await dbContext.Database.EnsureCreatedAsync();
        Console.WriteLine("✅ Database ensured created");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database connection failed: {ex.Message}");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine($"✅ File Service started. Swagger: http://localhost:{servicePort}/swagger");
Console.WriteLine($"📡 ChatService URL: {chatServiceUrl}");

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();

// Filter for file upload support in Swagger
public class FileUploadOperation : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath == "api/File/upload" &&
            context.ApiDescription.HttpMethod == "POST")
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["file"] = new OpenApiSchema
                                {
                                    Type = "string",
                                    Format = "binary"
                                },
                                ["postId"] = new OpenApiSchema
                                {
                                    Type = "string",
                                    Format = "uuid",
                                    Description = "GUID чата/поста",
                                    Example = OpenApiAnyFactory.CreateFromJson("\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"")
                                }
                            },
                            Required = new HashSet<string> { "file", "postId" }
                        }
                    }
                }
            };
        }
    }
}