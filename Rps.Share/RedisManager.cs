using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace Rps.Share;

public class RedisManager
{
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly string _prefix;

    public RedisManager(ConnectionMultiplexer multiplexer, string prefix)
    {
        _multiplexer = multiplexer;
        _prefix = prefix;
    }

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        var idb = _multiplexer.GetDatabase(db, asyncState);
        return idb.WithKeyPrefix(_prefix);
    }

    public async Task<string?> GetKeyAsync(string key)
    {
        IDatabase db = GetDatabase();
        return await db.StringGetAsync(key);
    }

    public async Task SetKeyAsync(string key, string value, TimeSpan? expiry = null)
    {
        IDatabase db = GetDatabase();
        await db.StringSetAsync(key, value, expiry);
    }
}