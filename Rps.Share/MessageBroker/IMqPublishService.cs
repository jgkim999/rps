namespace Rps.Share.MessageBroker;

/// <summary>
/// 통합된 메시지 큐 서비스 인터페이스 - 발행과 요청-응답 패턴을 모두 지원
/// </summary>
public interface IMqPublishService
{
    /// <summary>
    /// Multicast
    /// </summary>
    /// <param name="binaryMessageType"></param>
    /// <param name="exchangeName"></param>
    /// <param name="msg"></param>
    /// <param name="ct"></param>
    /// <param name="correlationId"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    ValueTask PublishMultiAsync<T>(
        BinaryMessageType binaryMessageType,
        string exchangeName,
        T msg,
        CancellationToken ct = default,
        string? correlationId = null) where T : class;
    
    /// <summary>
    /// UniCast
    /// </summary>
    /// <param name="binaryMessageType"></param>
    /// <param name="queueName"></param>
    /// <param name="msg"></param>
    /// <param name="ct"></param>
    /// <param name="correlationId"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    ValueTask PublishUniqueAsync<T>(
        BinaryMessageType binaryMessageType,
        string queueName,
        T msg,
        CancellationToken ct = default,
        string? correlationId = null) where T : class;
    
    /// <summary>
    /// AnyCast
    /// </summary>
    /// <param name="binaryMessageType"></param>
    /// <param name="queueName"></param>
    /// <param name="message"></param>
    /// <param name="ct"></param>
    /// <param name="correlationId"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    ValueTask PublishAnyAsync<T>(
        BinaryMessageType binaryMessageType,
        string queueName,
        T message,
        CancellationToken ct = default,
        string? correlationId = null) where T : class;
    
    /// <summary>
    /// 직렬화된 메시지를 보내고 응답을 대기합니다 (타임아웃 지원)
    /// </summary>
    /// <param name="binaryMessageType"></param>
    /// <param name="queueName"></param>
    /// <param name="request"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    /// <returns></returns>
    Task<TResponse?> PublishAndWaitForResponseAsync<TRequest, TResponse>(
        BinaryMessageType binaryMessageType,
        string queueName,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TRequest : class
        where TResponse : class;
}
