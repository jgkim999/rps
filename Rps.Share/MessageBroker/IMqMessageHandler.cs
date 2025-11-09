namespace Rps.Share.MessageBroker;

/// <summary>
/// 메시지 큐 메시지 처리를 위한 인터페이스
/// </summary>
public interface IMqMessageHandler
{
    /// <summary>
    /// 메시지 큐에서 수신된 메시지를 비동기적으로 처리합니다
    /// </summary>
    /// <param name="senderType">메시지 발송자 타입 (Multi, Any, Unique)</param>
    /// <param name="sender">메시지 발송자 식별자</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="message">처리할 메시지 내용</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>응답 메시지 (null이면 응답하지 않음)</returns>
    ValueTask<string?> HandleAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        string message,
        CancellationToken ct);

    /// <summary>
    /// Binary Message를 처리합니다
    /// </summary>
    /// <param name="senderType">메시지 발송자 타입 (Multi, Any, Unique)</param>
    /// <param name="sender">메시지 발송자 식별자</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="messageObject">deserialize된 메시지 객체</param>
    /// <param name="messageType">메시지 객체의 타입</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>응답 객체 (null이면 응답하지 않음)</returns>
    ValueTask<(BinaryMessageType ResponseType, object? Message)> HandleBinaryMessageAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        object messageObject,
        Type messageType,
        CancellationToken ct);
}
