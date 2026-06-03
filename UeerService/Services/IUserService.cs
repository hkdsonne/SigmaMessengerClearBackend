using UeerService.DTOs;
using UeerService.Models;

namespace UeerService.Services
{
    public interface IUserService
    {
        // Создание или обновление информации о пользователе
        Task<bool> CreateOrUpdateUserInfoAsync(Guid userId, UpdateUserInfoRequest request);

        // Получение информации о пользователе
        Task<UserInfo?> GetUserInfoAsync(Guid userId);

        // Обновление настроек пользователя
        Task<bool> UpdateSettingsAsync(Guid userId, UpdateSettingsRequest request);

        // Получение настроек пользователя
        Task<UserSettings?> GetSettingsAsync(Guid userId);

        // Получение полной информации (профиль + настройки)
        Task<UserInfoResponse?> GetFullUserInfoAsync(Guid userId);

        // Обновление активности пользователя
        Task<bool> UpdateActivityAsync(Guid userId);

        // Блокировка пользователя
        Task<bool> BlockUserAsync(Guid userId);

        // Разблокировка пользователя
        Task<bool> UnblockUserAsync(Guid userId);

        // Проверка, заблокирован ли пользователь
        Task<bool> IsUserBlockedAsync(Guid userId);
        Task<List<UserSummaryDto>> GetAllUsersAsync();
        Task<UserProfileDto?> GetUserByUsernameAsync(string username);

    }
}
