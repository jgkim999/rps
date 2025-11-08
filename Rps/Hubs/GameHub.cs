using LiteBus.Commands.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Rps.Handlers.Commands;

namespace Rps.Hubs;

public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;
    private readonly ICommandMediator _commandMediator;

    public GameHub(
        ILogger<GameHub> logger,
        ICommandMediator commandMediator)
    {
        _logger = logger;
        _commandMediator = commandMediator;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            await _commandMediator.SendAsync(new ConnectCommand(Context.ConnectionId));
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
            
            await _commandMediator.SendAsync(new DisconnectCommand(Context.ConnectionId));

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
