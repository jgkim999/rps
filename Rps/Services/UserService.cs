using Rps.Models;
using StackExchange.Redis;

namespace Rps.Services;

public class UserService : IUserService
{
    private readonly RedisManager _redisManager;
    private readonly ILogger<UserService> _logger;

    public UserService(RedisManager redisManager, ILogger<UserService> logger)
    {
        _redisManager = redisManager;
        _logger = logger;
    }

    public async Task<UserProfile> LoginOrCreateUserAsync(string nickname, string connectionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                _logger.LogWarning("Login attempt with empty nickname");
                throw new ArgumentException("닉네임은 비어있을 수 없습니다", nameof(nickname));
            }

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                _logger.LogWarning("Login attempt with empty connectionId for nickname: {Nickname}", nickname);
                throw new ArgumentException("연결 ID는 비어있을 수 없습니다", nameof(connectionId));
            }

            IDatabase db;
            try
            {
                db = _redisManager.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Redis database connection");
                throw new InvalidOperationException("데이터베이스 연결에 실패했습니다. 잠시 후 다시 시도해주세요", ex);
            }
            
            // 닉네임으로 사용자 ID 조회
            RedisValue userIdStr;
            try
            {
                userIdStr = await db.StringGetAsync($"user:nickname:{nickname}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve user ID for nickname: {Nickname}", nickname);
                throw new InvalidOperationException("사용자 정보를 조회하는 중 오류가 발생했습니다", ex);
            }
            
            if (userIdStr.IsNullOrEmpty)
            {
                // 새 사용자 생성
                try
                {
                    var userId = await db.StringIncrementAsync("user:id:counter");
                    
                    var userProfile = new UserProfile
                    {
                        UserId = userId,
                        Nickname = nickname,
                        ConnectionId = connectionId,
                        Statistics = new UserStatistics()
                    };
                    
                    // Redis에 저장
                    await SaveUserProfileAsync(db, userProfile);
                    await db.StringSetAsync($"user:nickname:{nickname}", userId);
                    
                    _logger.LogInformation("Created new user: {UserId} with nickname: {Nickname}", userId, nickname);
                    
                    return userProfile;
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    _logger.LogError(ex, "Failed to create new user with nickname: {Nickname}", nickname);
                    throw new InvalidOperationException("새 사용자를 생성하는 중 오류가 발생했습니다", ex);
                }
            }
            else
            {
                // 기존 사용자 로드
                try
                {
                    var userId = (long)userIdStr;
                    var userProfile = await GetUserByIdAsync(userId);
                    
                    if (userProfile == null)
                    {
                        _logger.LogWarning("User profile not found for userId: {UserId}, nickname: {Nickname}", userId, nickname);
                        throw new InvalidOperationException("사용자 프로필을 찾을 수 없습니다");
                    }
                    
                    // ConnectionId 업데이트
                    userProfile.ConnectionId = connectionId;
                    await db.HashSetAsync($"user:{userId}", "ConnectionId", connectionId);
                    
                    _logger.LogInformation("User logged in: {UserId} with nickname: {Nickname}", userId, nickname);
                    
                    return userProfile;
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    _logger.LogError(ex, "Failed to load existing user with nickname: {Nickname}", nickname);
                    throw new InvalidOperationException("사용자 정보를 불러오는 중 오류가 발생했습니다", ex);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Re-throw known exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for nickname: {Nickname}", nickname);
            throw new InvalidOperationException("로그인 처리 중 예상치 못한 오류가 발생했습니다", ex);
        }
    }

    public async Task<UserProfile?> GetUserByNicknameAsync(string nickname)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                _logger.LogWarning("GetUserByNicknameAsync called with empty nickname");
                throw new ArgumentException("닉네임은 비어있을 수 없습니다", nameof(nickname));
            }

            IDatabase db;
            try
            {
                db = _redisManager.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Redis database connection");
                throw new InvalidOperationException("데이터베이스 연결에 실패했습니다", ex);
            }
            
            // 닉네임으로 사용자 ID 조회
            RedisValue userIdStr;
            try
            {
                userIdStr = await db.StringGetAsync($"user:nickname:{nickname}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve user ID for nickname: {Nickname}", nickname);
                throw new InvalidOperationException("사용자 정보를 조회하는 중 오류가 발생했습니다", ex);
            }
            
            if (userIdStr.IsNullOrEmpty)
            {
                _logger.LogDebug("User not found with nickname: {Nickname}", nickname);
                return null;
            }
            
            var userId = (long)userIdStr;
            return await GetUserByIdAsync(userId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetUserByNicknameAsync for nickname: {Nickname}", nickname);
            throw new InvalidOperationException("사용자 조회 중 예상치 못한 오류가 발생했습니다", ex);
        }
    }

    public async Task<UserProfile?> GetUserByIdAsync(long userId)
    {
        try
        {
            if (userId <= 0)
            {
                _logger.LogWarning("GetUserByIdAsync called with invalid userId: {UserId}", userId);
                throw new ArgumentException("사용자 ID는 0보다 커야 합니다", nameof(userId));
            }

            IDatabase db;
            try
            {
                db = _redisManager.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Redis database connection");
                throw new InvalidOperationException("데이터베이스 연결에 실패했습니다", ex);
            }
            
            // Redis HashSet에서 사용자 프로필 조회
            HashEntry[] hashEntries;
            try
            {
                hashEntries = await db.HashGetAllAsync($"user:{userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve user profile for userId: {UserId}", userId);
                throw new InvalidOperationException("사용자 프로필을 조회하는 중 오류가 발생했습니다", ex);
            }
            
            if (hashEntries.Length == 0)
            {
                _logger.LogDebug("User profile not found for userId: {UserId}", userId);
                return null;
            }
            
            // UserProfile 객체로 변환
            try
            {
                var userProfile = new UserProfile
                {
                    UserId = userId,
                    Statistics = new UserStatistics()
                };
                
                foreach (var entry in hashEntries)
                {
                    switch (entry.Name.ToString())
                    {
                        case "Nickname":
                            userProfile.Nickname = entry.Value.ToString();
                            break;
                        case "ConnectionId":
                            userProfile.ConnectionId = entry.Value.ToString();
                            break;
                        case "SelectedSkin":
                            userProfile.SelectedSkin = (int)entry.Value;
                            break;
                        case "Wins":
                            userProfile.Statistics.Wins = (int)entry.Value;
                            break;
                        case "Losses":
                            userProfile.Statistics.Losses = (int)entry.Value;
                            break;
                        case "Draws":
                            userProfile.Statistics.Draws = (int)entry.Value;
                            break;
                        case "RockCount":
                            userProfile.Statistics.RockCount = (int)entry.Value;
                            break;
                        case "PaperCount":
                            userProfile.Statistics.PaperCount = (int)entry.Value;
                            break;
                        case "ScissorsCount":
                            userProfile.Statistics.ScissorsCount = (int)entry.Value;
                            break;
                    }
                }
                
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse user profile data for userId: {UserId}", userId);
                throw new InvalidOperationException("사용자 프로필 데이터를 처리하는 중 오류가 발생했습니다", ex);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetUserByIdAsync for userId: {UserId}", userId);
            throw new InvalidOperationException("사용자 조회 중 예상치 못한 오류가 발생했습니다", ex);
        }
    }

    public async Task UpdateUserSkinAsync(long userId, int skinId)
    {
        try
        {
            if (userId <= 0)
            {
                _logger.LogWarning("UpdateUserSkinAsync called with invalid userId: {UserId}", userId);
                throw new ArgumentException("사용자 ID는 0보다 커야 합니다", nameof(userId));
            }

            if (skinId < 0)
            {
                _logger.LogWarning("UpdateUserSkinAsync called with invalid skinId: {SkinId} for userId: {UserId}", skinId, userId);
                throw new ArgumentException("스킨 ID는 0 이상이어야 합니다", nameof(skinId));
            }

            IDatabase db;
            try
            {
                db = _redisManager.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Redis database connection");
                throw new InvalidOperationException("데이터베이스 연결에 실패했습니다", ex);
            }
            
            // Redis에 선택된 스킨 ID 저장
            try
            {
                await db.HashSetAsync($"user:{userId}", "SelectedSkin", skinId);
                _logger.LogInformation("Updated skin for user {UserId} to {SkinId}", userId, skinId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update skin for userId: {UserId}, skinId: {SkinId}", userId, skinId);
                throw new InvalidOperationException("스킨 정보를 저장하는 중 오류가 발생했습니다", ex);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in UpdateUserSkinAsync for userId: {UserId}, skinId: {SkinId}", userId, skinId);
            throw new InvalidOperationException("스킨 업데이트 중 예상치 못한 오류가 발생했습니다", ex);
        }
    }

    public async Task UpdateUserStatisticsAsync(long userId, GameResult result, HandShape choice)
    {
        try
        {
            if (userId <= 0)
            {
                _logger.LogWarning("UpdateUserStatisticsAsync called with invalid userId: {UserId}", userId);
                throw new ArgumentException("사용자 ID는 0보다 커야 합니다", nameof(userId));
            }

            IDatabase db;
            try
            {
                db = _redisManager.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Redis database connection");
                throw new InvalidOperationException("데이터베이스 연결에 실패했습니다", ex);
            }
            
            // 게임 결과에 따라 승/패/무 통계 업데이트
            string resultField = result switch
            {
                GameResult.Win => "Wins",
                GameResult.Loss => "Losses",
                GameResult.Draw => "Draws",
                _ => throw new ArgumentException("잘못된 게임 결과입니다", nameof(result))
            };
            
            // 선택한 손모양 통계 업데이트
            string choiceField = choice switch
            {
                HandShape.Rock => "RockCount",
                HandShape.Paper => "PaperCount",
                HandShape.Scissors => "ScissorsCount",
                _ => throw new ArgumentException("잘못된 손모양입니다", nameof(choice))
            };
            
            try
            {
                await db.HashIncrementAsync($"user:{userId}", resultField);
                await db.HashIncrementAsync($"user:{userId}", choiceField);
                
                _logger.LogInformation("Updated statistics for user {UserId}: {Result}, {Choice}", userId, result, choice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update statistics for userId: {UserId}, result: {Result}, choice: {Choice}", 
                    userId, result, choice);
                throw new InvalidOperationException("통계 정보를 업데이트하는 중 오류가 발생했습니다", ex);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in UpdateUserStatisticsAsync for userId: {UserId}", userId);
            throw new InvalidOperationException("통계 업데이트 중 예상치 못한 오류가 발생했습니다", ex);
        }
    }

    private async Task SaveUserProfileAsync(IDatabase db, UserProfile profile)
    {
        try
        {
            var hashEntries = new HashEntry[]
            {
                new HashEntry("UserId", profile.UserId),
                new HashEntry("Nickname", profile.Nickname),
                new HashEntry("ConnectionId", profile.ConnectionId),
                new HashEntry("SelectedSkin", profile.SelectedSkin),
                new HashEntry("Wins", profile.Statistics.Wins),
                new HashEntry("Losses", profile.Statistics.Losses),
                new HashEntry("Draws", profile.Statistics.Draws),
                new HashEntry("RockCount", profile.Statistics.RockCount),
                new HashEntry("PaperCount", profile.Statistics.PaperCount),
                new HashEntry("ScissorsCount", profile.Statistics.ScissorsCount)
            };
            
            await db.HashSetAsync($"user:{profile.UserId}", hashEntries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user profile for userId: {UserId}", profile.UserId);
            throw new InvalidOperationException("사용자 프로필을 저장하는 중 오류가 발생했습니다", ex);
        }
    }
}
