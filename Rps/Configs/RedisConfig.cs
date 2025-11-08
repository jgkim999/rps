namespace Rps.Configs;

public class RedisConfig
{
    public string SignalRBackplane { get; set; } = string.Empty;
    public string FusionCacheRedisCache { get; set; } = string.Empty;
    public string FusionCacheBackplane { get; set; } = string.Empty;
    public string? AuthToken { get; set; }
}
