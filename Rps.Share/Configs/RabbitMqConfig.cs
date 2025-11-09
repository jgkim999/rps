namespace Rps.Share.Configs;

public class RabbitMqConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    public ushort ConsumerDispatchConcurrency { get; set; } = 1;

    /// <summary>
    /// QoS Prefetch Count - Consumer가 한 번에 받을 수 있는 미확인 메시지의 최대 개수
    /// 0 = 무제한 (기본값, 메모리 부족 위험)
    /// 권장값: ConsumerDispatchConcurrency * 10 (예: 2 * 10 = 20)
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    public bool AutomaticRecoveryEnabled { get; set; } = true;
    public long NetworkRecoveryInterval { get; set; } = 5;
    public bool TopologyRecoveryEnabled { get; set; } = true;
    
    public string MultiExchange { get; set; } = string.Empty;
    public string MultiQueue { get; set; } = string.Empty;
    
    public string AnyQueue { get; set; } = string.Empty;
    
    public string UniqueQueue { get; set; } = string.Empty;
}
