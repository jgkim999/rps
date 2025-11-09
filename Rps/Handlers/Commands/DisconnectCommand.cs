using LiteBus.Commands.Abstractions;
using Rps.Share;
using ZiggyCreatures.Caching.Fusion;

namespace Rps.Handlers.Commands;

public record DisconnectCommand(string ConnectionId) : ICommand;

public class DisconnectCommandHandler : ICommandHandler<DisconnectCommand>
{
    private readonly ILogger<DisconnectCommandHandler> _logger;
    private readonly IFusionCache _fusionCache;
    private readonly RedisManager _redisManager;

    public DisconnectCommandHandler(
        ILogger<DisconnectCommandHandler> logger,
        IFusionCache fusionCache,
        RedisManager redisManager)
    {
        _logger = logger;
        _fusionCache = fusionCache;
        _redisManager = redisManager;
    }

    public async Task HandleAsync(DisconnectCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Connection disconnected. ConnectionId: {ConnectionId}", command.ConnectionId);
        try
        {
            await _fusionCache.RemoveAsync($"ConnectedClient-{command.ConnectionId}", token: ct);
            var db = _redisManager.GetDatabase();
            await db.SetRemoveAsync("Users", command.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in OnDisconnectedAsync for ConnectionId: {ConnectionId}", command.ConnectionId);
            throw;
        }
    }
}
