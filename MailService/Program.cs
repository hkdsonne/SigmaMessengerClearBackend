using DotNetEnv;
using MailService.Services;

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
if (!envLoaded)
{
    Console.WriteLine("ERROR! No .env file found, using environment variables from Docker/system");
}

var builder = WebApplication.CreateBuilder(args);


// Будьте людьми не трогайте моё логирование(((
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var mailHost = Environment.GetEnvironmentVariable("MAIL_HOST") ?? "";
var mailPortStr = Environment.GetEnvironmentVariable("MAIL_PORT") ?? "465";
var mailUsername = Environment.GetEnvironmentVariable("MAIL_USERNAME") ?? "";
var mailPassword = Environment.GetEnvironmentVariable("MAIL_PASSWORD") ?? "";

builder.Configuration["Email:Smtp:Host"] = mailHost;
builder.Configuration["Email:Smtp:Port"] = mailPortStr;
builder.Configuration["Email:Smtp:Username"] = mailUsername;
builder.Configuration["Email:Smtp:Password"] = mailPassword;
builder.Configuration["Email:Smtp:From"] = mailUsername;  
builder.Configuration["Email:FromEmail"] = mailUsername;  
builder.Configuration["Email:FromName"] = "Messenger App";

// Читаем порт из переменной окружения
var servicePort = Environment.GetEnvironmentVariable("MAIL_SERVICE_PORT");

// Устанавливаем URL с нужным портом
builder.WebHost.UseUrls($"http://+:{servicePort}");

Console.WriteLine($"MAIL Service will run on port: {servicePort}");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ВАЖНО: регистрируем IMailService
builder.Services.AddScoped<IMailService, MailServiceImpl>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();
