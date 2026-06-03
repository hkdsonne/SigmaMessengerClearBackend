namespace UeerService.DTOs
{
    // Запрос на создание/обновление информации о пользователе

    public class UpdateUserInfoRequest
    {
        public string full_name { get; set; } = string.Empty;
        public string? avatar_url { get; set; }
        public string? bio { get; set; }
        public string email { get; set; } = string.Empty;
    }

    // Запрос на обновление настроек
    public class UpdateSettingsRequest
    {
        public bool? notifications_enabled { get; set; }
        public string? theme { get; set; }
    }

    // Ответ с информацией о пользователе
    public class UserInfoResponse
    {
        public Guid user_id { get; set; }
        public string full_name { get; set; } = string.Empty;
        public string? avatar_url { get; set; }
        public string? bio { get; set; }
        public DateTime last_activity_at { get; set; }
        public bool is_active { get; set; }
        public bool is_blocked { get; set; }
        public string email { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public UserSettingsResponse settings { get; set; } = new();
    }

    // Ответ с настройками
    public class UserSettingsResponse
    {
        public bool notifications_enabled { get; set; }
        public string theme { get; set; } = string.Empty;
    }

    // Ответ при блокировке
    public class BlockedResponse
    {
        public bool is_blocked { get; set; }
        public string message { get; set; } = string.Empty;
    }
}
