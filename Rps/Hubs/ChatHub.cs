using Microsoft.AspNetCore.SignalR;

using ZiggyCreatures.Caching.Fusion;

namespace Rps.Hubs;

public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly IServiceProvider _provider;

    public ChatHub(ILogger<ChatHub> logger, IServiceProvider provider)
    {
        _logger = logger;
        _provider = provider;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Connected. ClientId:{ClientId}", Context.ConnectionId);

        using var scope = _provider.CreateScope();
        IFusionCache? cache = scope.ServiceProvider.GetService<IFusionCache>();
        if (cache is not null)
        {
            await cache.SetAsync($"ConnectedClient-{Context.ConnectionId}", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        }

        var redisManager = scope.ServiceProvider.GetService<RedisManager>();
        var db = redisManager?.GetDatabase();
        await db?.SetAddAsync("Users", Context.ConnectionId)!;

        await Groups.AddToGroupAsync(Context.ConnectionId, "SignalR Users");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        using var scope = _provider.CreateScope();
        var cache = scope.ServiceProvider.GetService<IFusionCache>();
        if (cache is not null)
        {
            await cache.RemoveAsync($"ConnectedClient-{Context.ConnectionId}");
        }

        var redisManager = scope.ServiceProvider.GetService<RedisManager>();
        var db = redisManager?.GetDatabase();
        await db?.SetRemoveAsync("Users", Context.ConnectionId)!;

        _logger.LogInformation("Disconnected. ClientId:{ClientId}", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}
