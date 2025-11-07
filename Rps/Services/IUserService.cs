using Rps.Models;

namespace Rps.Services;

public interface IUserService
{
    Task<UserProfile> LoginOrCreateUserAsync(string nickname, string connectionId);
    Task<UserProfile?> GetUserByNicknameAsync(string nickname);
    Task<UserProfile?> GetUserByIdAsync(long userId);
    Task UpdateUserSkinAsync(long userId, int skinId);
    Task UpdateUserStatisticsAsync(long userId, GameResult result, HandShape choice);
}
