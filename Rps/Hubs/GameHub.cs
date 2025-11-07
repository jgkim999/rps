using Microsoft.AspNetCore.SignalR;
using Rps.Models;
using Rps.Services;
using ZiggyCreatures.Caching.Fusion;

namespace Rps.Hubs;

public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;
    private readonly IServiceProvider _provider;

    public GameHub(ILogger<GameHub> logger, IServiceProvider provider)
    {
        _logger = logger;
        _provider = provider;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            _logger.LogInformation("Connected. ClientId:{ClientId}", Context.ConnectionId);

            using var scope = _provider.CreateScope();
            
            try
            {
                IFusionCache? cache = scope.ServiceProvider.GetService<IFusionCache>();
                if (cache is not null)
                {
                    await cache.SetAsync($"ConnectedClient-{Context.ConnectionId}", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set cache for connected client: {ClientId}", Context.ConnectionId);
                // Continue execution - cache failure is not critical
            }

            try
            {
                var redisManager = scope.ServiceProvider.GetService<RedisManager>();
                var db = redisManager?.GetDatabase();
                if (db != null)
                {
                    await db.SetAddAsync("Users", Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add user to Redis set for ClientId: {ClientId}", Context.ConnectionId);
                // Continue execution - this will be retried on next operation
            }

            try
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "SignalR Users");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add client to SignalR group: {ClientId}", Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in OnConnectedAsync for ClientId: {ClientId}", Context.ConnectionId);
            throw;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "Client disconnected with exception. ClientId:{ClientId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Disconnected. ClientId:{ClientId}", Context.ConnectionId);
            }

            using var scope = _provider.CreateScope();
            
            try
            {
                var cache = scope.ServiceProvider.GetService<IFusionCache>();
                if (cache is not null)
                {
                    await cache.RemoveAsync($"ConnectedClient-{Context.ConnectionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove cache for disconnected client: {ClientId}", Context.ConnectionId);
                // Continue execution - cache cleanup failure is not critical
            }

            try
            {
                var redisManager = scope.ServiceProvider.GetService<RedisManager>();
                var db = redisManager?.GetDatabase();
                if (db != null)
                {
                    await db.SetRemoveAsync("Users", Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove user from Redis set for ClientId: {ClientId}", Context.ConnectionId);
                // Continue execution - cleanup will happen on next connection
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in OnDisconnectedAsync for ClientId: {ClientId}", Context.ConnectionId);
            // Don't throw - disconnection should complete
        }
    }

    public async Task SendMessage(string user, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("SendMessage called with empty user from ClientId: {ClientId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("OnError", "사용자 이름이 비어있습니다");
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                _logger.LogWarning("SendMessage called with empty message from user: {User}, ClientId: {ClientId}", 
                    user, Context.ConnectionId);
                await Clients.Caller.SendAsync("OnError", "메시지가 비어있습니다");
                return;
            }

            await Clients.All.SendAsync("ReceiveMessage", user, message);
            _logger.LogDebug("Message sent from user: {User}, ClientId: {ClientId}", user, Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message from user: {User}, ClientId: {ClientId}", user, Context.ConnectionId);
            await Clients.Caller.SendAsync("OnError", "메시지 전송에 실패했습니다");
        }
    }

    public async Task LoginUser(string nickname)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                _logger.LogWarning("LoginUser called with empty nickname from ClientId: {ClientId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("OnLoginFailed", "닉네임을 입력해주세요");
                return;
            }

            if (nickname.Length < 2)
            {
                _logger.LogWarning("LoginUser called with too short nickname: {Nickname} from ClientId: {ClientId}", 
                    nickname, Context.ConnectionId);
                await Clients.Caller.SendAsync("OnLoginFailed", "닉네임은 최소 2자 이상이어야 합니다");
                return;
            }

            if (nickname.Length > 20)
            {
                _logger.LogWarning("LoginUser called with too long nickname from ClientId: {ClientId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("OnLoginFailed", "닉네임은 최대 20자까지 가능합니다");
                return;
            }

            IUserService userService;
            try
            {
                userService = _provider.GetRequiredService<IUserService>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get UserService for login. ClientId: {ClientId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("OnLoginFailed", "서비스를 사용할 수 없습니다. 잠시 후 다시 시도해주세요");
                return;
            }

            UserProfile userProfile;
            try
            {
                userProfile = await userService.LoginOrCreateUserAsync(nickname, Context.ConnectionId);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument in LoginUser for nickname: {Nickname}", nickname);
                await Clients.Caller.SendAsync("OnLoginFailed", ex.Message);
                return;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operation failed in LoginUser for nickname: {Nickname}", nickname);
                await Clients.Caller.SendAsync("OnLoginFailed", ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in LoginUser for nickname: {Nickname}", nickname);
                await Clients.Caller.SendAsync("OnLoginFailed", "로그인 처리 중 오류가 발생했습니다. 다시 시도해주세요");
                return;
            }
            
            try
            {
                await Clients.Caller.SendAsync("OnLoginSuccess", 
                    userProfile.UserId, 
                    userProfile.Nickname, 
                    userProfile.Statistics);
                
                _logger.LogInformation("User logged in successfully. UserId:{UserId}, Nickname:{Nickname}, ClientId:{ClientId}", 
                    userProfile.UserId, userProfile.Nickname, Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send login success message to client. UserId:{UserId}, ClientId:{ClientId}", 
                    userProfile.UserId, Context.ConnectionId);
                // User is logged in but client notification failed - client should retry
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in LoginUser for nickname: {Nickname}, ClientId: {ClientId}", 
                nickname, Context.ConnectionId);
            
            try
            {
                await Clients.Caller.SendAsync("OnLoginFailed", "로그인 처리 중 예상치 못한 오류가 발생했습니다");
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Failed to send error message to client. ClientId: {ClientId}", Context.ConnectionId);
            }
        }
    }

    public async Task SelectSkin(long userId, int skinId)
    {
        try
        {
            if (userId <= 0)
            {
                _logger.LogWarning("SelectSkin called with invalid userId: {UserId} from ClientId: {ClientId}", 
                    userId, Context.ConnectionId);
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", "잘못된 사용자 ID입니다");
                return;
            }

            if (skinId < 0)
            {
                _logger.LogWarning("SelectSkin called with invalid skinId: {SkinId} for userId: {UserId}, ClientId: {ClientId}", 
                    skinId, userId, Context.ConnectionId);
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", "잘못된 스킨 ID입니다");
                return;
            }

            IUserService userService;
            try
            {
                userService = _provider.GetRequiredService<IUserService>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get UserService for skin selection. ClientId: {ClientId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", "서비스를 사용할 수 없습니다. 잠시 후 다시 시도해주세요");
                return;
            }

            try
            {
                await userService.UpdateUserSkinAsync(userId, skinId);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument in SelectSkin for userId: {UserId}, skinId: {SkinId}", userId, skinId);
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", ex.Message);
                return;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operation failed in SelectSkin for userId: {UserId}, skinId: {SkinId}", userId, skinId);
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SelectSkin for userId: {UserId}, skinId: {SkinId}", userId, skinId);
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", "스킨 선택 중 오류가 발생했습니다. 다시 시도해주세요");
                return;
            }
            
            try
            {
                await Clients.Caller.SendAsync("OnSkinSelected", skinId);
                
                _logger.LogInformation("Skin selected successfully. UserId:{UserId}, SkinId:{SkinId}, ClientId:{ClientId}", 
                    userId, skinId, Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send skin selection success message. UserId:{UserId}, ClientId:{ClientId}", 
                    userId, Context.ConnectionId);
                // Skin is selected but client notification failed - client should check state
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SelectSkin for userId: {UserId}, skinId: {SkinId}, ClientId: {ClientId}", 
                userId, skinId, Context.ConnectionId);
            
            try
            {
                await Clients.Caller.SendAsync("OnSkinSelectionFailed", "스킨 선택 중 예상치 못한 오류가 발생했습니다");
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Failed to send error message to client. ClientId: {ClientId}", Context.ConnectionId);
            }
        }
    }
}
