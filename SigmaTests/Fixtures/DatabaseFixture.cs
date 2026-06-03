using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AuthService.Data;
using ChatService.Data;
using FileService.Data;
using UeerService.Data;

namespace SigmaTests.Fixtures;

public class DatabaseFixture : IDisposable
{
    public AuthDbContext AuthDbContext { get; private set; }
    public ChatDbContext ChatDbContext { get; private set; }
    public FileDbContext FileDbContext { get; private set; }
    public AppDbContext UserDbContext { get; private set; }

    static DatabaseFixture()
    {
        // Загружаем .env.tests один раз при первом обращении к фикстуре
        var envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../SigmaTests/.env.tests");
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
    }

    public DatabaseFixture()
    {
        var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"AuthDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        AuthDbContext = new AuthDbContext(authOptions);

        var chatOptions = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase($"ChatDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        ChatDbContext = new ChatDbContext(chatOptions);

        var fileOptions = new DbContextOptionsBuilder<FileDbContext>()
            .UseInMemoryDatabase($"FileDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        FileDbContext = new FileDbContext(fileOptions);

        var userOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"UserDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        UserDbContext = new AppDbContext(userOptions);
    }

    public void Dispose()
    {
        AuthDbContext?.Dispose();
        ChatDbContext?.Dispose();
        FileDbContext?.Dispose();
        UserDbContext?.Dispose();
    }
}

[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }