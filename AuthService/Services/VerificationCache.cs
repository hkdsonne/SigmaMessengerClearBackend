using System.Collections.Concurrent;

namespace AuthService.Services;

public class VerificationCache
{
    private readonly ConcurrentDictionary<string, VerificationData> _cache = new();
    private readonly TimeSpan _expiration = TimeSpan.FromMinutes(10);

    public void Store(string email, VerificationData data)
    {
        _cache.AddOrUpdate(email, data, (key, old) => data);

        // Автоматическое удаление через 10 минут
        _ = Task.Run(async () =>
        {
            await Task.Delay(_expiration);
            _cache.TryRemove(email, out _);
        });
    }

    public VerificationData? Get(string email)
    {
        if (_cache.TryGetValue(email, out var data))
        {
            if (data.expires_at > DateTime.UtcNow)
            {
                return data;
            }
            // Если истекло, удаляем
            _cache.TryRemove(email, out _);
        }
        return null;
    }

    public void Remove(string email)
    {
        _cache.TryRemove(email, out _);
    }
}

public class VerificationData
{
    public string code { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public string raw_password { get; set; } = string.Empty;
    public string device_info { get; set; } = string.Empty;
    public DateTime expires_at { get; set; }
}
