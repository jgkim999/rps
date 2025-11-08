using LiteBus.Commands.Abstractions;
using ZiggyCreatures.Caching.Fusion;

namespace Rps.Handlers.Commands;

public record ConnectCommand(string ConnectionId) : ICommand;

public class ConnectCommandHandler : ICommandHandler<ConnectCommand>
{
    private readonly ILogger<ConnectCommandHandler> _logger;
    private readonly IFusionCache _fusionCache;
    private readonly RedisManager _redisManager;

    public ConnectCommandHandler(
        ILogger<ConnectCommandHandler> logger,
        IFusionCache fusionCache,
        RedisManager redisManager)
    {
        _logger = logger;
        _fusionCache = fusionCache;
        _redisManager = redisManager;
    }

    public async Task HandleAsync(ConnectCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Connection established. ConnectionId: {ConnectionId}", command.ConnectionId);
        try
        {
            await _fusionCache.SetAsync($"ConnectedClient-{command.ConnectionId}", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(60), token: ct);
            var db = _redisManager.GetDatabase();
            await db.SetAddAsync("Users", command.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in OnConnectedAsync for ClientId: {ClientId}", command.ConnectionId);
            throw;
        }
    }
}
