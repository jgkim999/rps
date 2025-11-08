using LiteBus.Commands.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Rps.Handlers.Commands;

using ZiggyCreatures.Caching.Fusion;

namespace Rps.Hubs;

public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;
    private readonly IServiceProvider _provider;
    private readonly ICommandMediator _commandMediator;

    public GameHub(
        ILogger<GameHub> logger,
        IServiceProvider provider,
        ICommandMediator commandMediator)
    {
        _logger = logger;
        _provider = provider;
        _commandMediator = commandMediator;
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
                    await cache.SetAsync($"ConnectedClient-{Context.ConnectionId}", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(60));
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
            var command = new SendMessageCommand(user, message);
            await _commandMediator.SendAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message from user: {User}, ClientId: {ClientId}", user, Context.ConnectionId);
            try
            {
                await Clients.Caller.SendAsync("OnError", "메시지 전송에 실패했습니다");
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Failed to send error message to client. ClientId: {ClientId}", Context.ConnectionId);
            }
        }
    }

    public async Task LoginUser(string nickname)
    {
        try
        {
            var command = new LoginUserCommand(nickname, Context.ConnectionId);
            var userProfile = await _commandMediator.SendAsync(command);

            try
            {
                await Clients.Caller.SendAsync("OnLoginSuccess",
                    userProfile.UserId,
                    userProfile.Nickname,
                    userProfile.SelectedSkin,
                    userProfile.Statistics);
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
            var command = new SelectSkinCommand(userId, skinId);
            await _commandMediator.SendAsync(command);

            try
            {
                await Clients.Caller.SendAsync("OnSkinSelected", skinId);
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
