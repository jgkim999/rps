using System.Collections.Frozen;
using Rps.Share.Dto;
using Rps.Share.MessageBroker;

namespace Rps.Handlers;

public class LobbyMqHandler : IMqMessageHandler
{
    private readonly ILogger<LobbyMqHandler> _logger;
    private readonly FrozenDictionary<
        string,
        Func<MqSenderType, string?, string?, string?, object, Type, CancellationToken, ValueTask<(BinaryMessageType ResponseType, object? Message)>>> _handlers;

    /// <summary>
    /// LobbyMqHandler의 새 인스턴스를 초기화합니다
    /// </summary>
    /// <param name="logger">로거 인스턴스</param>
    public LobbyMqHandler(ILogger<LobbyMqHandler> logger)
    {
        _logger = logger;

        _handlers = new Dictionary<string, Func<MqSenderType, string?, string?, string?, object, Type, CancellationToken, ValueTask<(BinaryMessageType ResponseType, object? Message)>>>(4)
        {
            { typeof(MqPublishRequest).FullName!, OnMqPublishRequestAsync },
            { typeof(MessagePackRequest).FullName!, OnMessagePackRequestAsync },
            { typeof(ProtobufRequest).FullName!, OnProtobufRequestAsync },
            { typeof(MemoryPackRequest).FullName!, OnMemoryPackRequestAsync}
        }.ToFrozenDictionary();
    }
    
    /// <summary>
    /// 메시지 큐에서 수신된 메시지를 비동기적으로 처리합니다
    /// ReplyTo가 있는 경우 응답 메시지를 반환합니다
    /// </summary>
    /// <param name="senderType">메시지 발송자 타입 (Multi, Any, Unique)</param>
    /// <param name="sender">메시지 발송자 식별자 (ReplyTo 큐 이름)</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="message">처리할 메시지 내용</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>응답 메시지 (null이면 응답하지 않음)</returns>
    public ValueTask<string?> HandleAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        string message,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Message Processed. MessageId: {MessageId}, Message: {Message}", messageId, message);

            // ReplyTo가 있는 경우에만 응답 메시지 생성
            if (!string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(correlationId))
            {
                var responseMessage = $"응답: '{message}' 메시지를 성공적으로 처리했습니다. 처리 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                _logger.LogDebug("Response prepared for {ReplyTo} with CorrelationId: {CorrelationId}", sender, correlationId);
                return ValueTask.FromResult<string?>(responseMessage);
            }

            return ValueTask.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message. MessageId: {MessageId}", messageId);

            // 오류가 발생한 경우에도 응답 생성 (ReplyTo가 있는 경우)
            if (!string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(correlationId))
            {
                var errorResponse = $"오류: 메시지 처리 중 오류가 발생했습니다. 오류: {ex.Message}";
                return ValueTask.FromResult<string?>(errorResponse);
            }

            return ValueTask.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// MessagePack으로 deserialize된 타입 객체를 직접 처리합니다
    /// 타입별로 다른 처리 로직을 구현할 수 있습니다
    /// </summary>
    /// <param name="senderType">메시지 발송자 타입 (Multi, Any, Unique)</param>
    /// <param name="sender">메시지 발송자 식별자</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="messageObject">deserialize된 메시지 객체</param>
    /// <param name="messageType">메시지 객체의 타입</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>응답 객체 (null이면 응답하지 않음)</returns>
    public async ValueTask<(BinaryMessageType ResponseType, object? Message)> HandleBinaryMessageAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        object messageObject,
        Type messageType,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "MessagePack Object Processed. MessageId: {MessageId}, Type: {MessageType}",
                messageId, messageType.FullName);

            if (_handlers.TryGetValue(messageType.FullName!, out var handler))
            {
                return await handler(senderType, sender, correlationId, messageId, messageObject, messageType, ct);
            }
            else
            {
                _logger.LogWarning("No handler registered for message type: {MessageType}, MessageId: {MessageId}",
                    messageType.FullName, messageId);
                return (BinaryMessageType.Unknown, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MessagePack object. MessageId: {MessageId}, Type: {MessageType}",
                messageId, messageType.FullName);
            return (BinaryMessageType.Unknown, null);
        }
    }

    private async ValueTask<(BinaryMessageType ResponseType, object? Message)> OnMqPublishRequestAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        object messageObject,
        Type messageType,
        CancellationToken ct)
    {
        try
        {
            if (messageObject is not MqPublishRequest request)
            {
                _logger.LogError("Message casting error. MessageId: {MessageId}, ExpectedType: {ExpectedType}, ActualType: {ActualType}",
                    messageId, nameof(MqPublishRequest), messageObject.GetType().Name);
                return(BinaryMessageType.Unknown, null);
            }
            _logger.LogInformation("Message Processed. MessageId: {MessageId}, Message: {Message}", messageId, request.Message);
            return (BinaryMessageType.Unknown, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MqPublishRequest. MessageId: {MessageId}", messageId);
            (BinaryMessageType ResponseType, object? Message) result = new(BinaryMessageType.Unknown, null);
            return await ValueTask.FromResult(result);
        }
    }

    private async ValueTask<(BinaryMessageType ResponseType, object? Message)> OnMessagePackRequestAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        object messageObject,
        Type messageType,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("MessagePackRequest received. MessageId: {MessageId}, Type: {MessageType}", messageId, messageType.FullName);

            if (messageObject is not MessagePackRequest request)
            {
                _logger.LogError("Message casting error. MessageId: {MessageId}, ExpectedType: {ExpectedType}, ActualType: {ActualType}",
                    messageId, nameof(MessagePackRequest), messageObject.GetType().Name);
                return (BinaryMessageType.Unknown, null);
            }

            // TestResponse 생성
            var response = new TestResponse
            {
                ResponseId = Ulid.NewUlid().ToString(),
                OriginalRequestId = request.Id,
                ResponseMessage = $"성공적으로 처리했습니다: {request.Message}",
                ProcessedAt = DateTime.Now,
                Success = true,
                ResponseData = new Dictionary<string, object>
                {
                    { "서버", Environment.MachineName },
                    { "처리시간", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "원본메시지", request.Message },
                    { "처리결과", "성공" }
                }
            };

            _logger.LogInformation("MessagePackRequest processed successfully. RequestId: {RequestId}, ResponseId: {ResponseId}",
                request.Id, response.ResponseId);

            return (BinaryMessageType.MessagePack, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MessagePackRequest. MessageId: {MessageId}", messageId);
            (BinaryMessageType ResponseType, object? Message) result = new(BinaryMessageType.Unknown, null);
            return await ValueTask.FromResult(result);
        }
    }
    
    private async ValueTask<(BinaryMessageType ResponseType, object? Message)> OnProtobufRequestAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        object messageObject,
        Type messageType,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("ProtobufRequest received. MessageId: {MessageId}, Type: {MessageType}", messageId, messageType.FullName);

            if (messageObject is not ProtobufRequest request)
            {
                _logger.LogError("Message casting error. MessageId: {MessageId}, ExpectedType: {ExpectedType}, ActualType: {ActualType}",
                    messageId, nameof(ProtobufRequest), messageObject.GetType().Name);
                return (BinaryMessageType.Unknown, null);
            }

            // TestResponse 생성
            var response = new ProtobufResponse
            {
                ResponseId = Ulid.NewUlid().ToString(),
                OriginalRequestId = request.Id,
                ResponseMessage = $"성공적으로 처리했습니다: {request.Message}",
                ProcessedAt = DateTime.Now,
                Success = true,
                ResponseData = new Dictionary<string, string>
                {
                    { "서버", Environment.MachineName },
                    { "처리시간", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "원본메시지", request.Message },
                    { "처리결과", "성공" }
                }
            };

            _logger.LogInformation("ProtobufRequest processed successfully. RequestId: {RequestId}, ResponseId: {ResponseId}",
                request.Id, response.ResponseId);

            return (BinaryMessageType.ProtoBuf, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ProtobufRequest. MessageId: {MessageId}", messageId);
            (BinaryMessageType ResponseType, object? Message) result = new(BinaryMessageType.Unknown, null);
            return await ValueTask.FromResult(result);
        }
    }
    
    private async ValueTask<(BinaryMessageType ResponseType, object? Message)> OnMemoryPackRequestAsync(
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        object messageObject,
        Type messageType,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("MemoryPackRequest received. MessageId: {MessageId}, Type: {MessageType}", messageId, messageType.FullName);

            if (messageObject is not MemoryPackRequest request)
            {
                _logger.LogError("Message casting error. MessageId: {MessageId}, ExpectedType: {ExpectedType}, ActualType: {ActualType}",
                    messageId, nameof(MemoryPackRequest), messageObject.GetType().Name);
                return (BinaryMessageType.Unknown, null);
            }

            // TestResponse 생성
            var response = new MemoryPackResponse
            {
                ResponseId = Ulid.NewUlid().ToString(),
                OriginalRequestId = request.Id,
                ResponseMessage = $"성공적으로 처리했습니다: {request.Message}",
                ProcessedAt = DateTime.Now,
                Success = true,
                ResponseData = new Dictionary<string, string>
                {
                    { "서버", Environment.MachineName },
                    { "처리시간", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "원본메시지", request.Message },
                    { "처리결과", "성공" }
                }
            };

            _logger.LogInformation("MemoryPackRequest processed successfully. RequestId: {RequestId}, ResponseId: {ResponseId}",
                request.Id, response.ResponseId);

            return (BinaryMessageType.MemoryPack, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MemoryPackRequest. MessageId: {MessageId}", messageId);
            (BinaryMessageType ResponseType, object? Message) result = new(BinaryMessageType.Unknown, null);
            return await ValueTask.FromResult(result);
        }
    }
}
