using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Rps.Share.Configs;
using Rps.Share.Telemetry;

namespace Rps.Share.MessageBroker;

public class RabbitMqPublishService : IMqPublishService, IDisposable
{
    private readonly RabbitMqConfig _config;
    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqHandler _handler;
    
    private readonly string _uniqueQueue;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<RabbitMqPublishService> _logger;

    // 요청-응답 매칭을 위한 대기 중인 요청 저장소
    private readonly ConcurrentDictionary<string, TaskCompletionSource<(string? StringResponse, byte[]? ByteResponse)>> _pendingRequests;
    private readonly AsyncEventingBasicConsumer _uniqueConsumer;

    // 메모리 최적화를 위한 객체 풀링
    private readonly ConcurrentBag<StringBuilder> _stringBuilderPool;

    // RecyclableMemoryStream 관리자
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager =
        new RecyclableMemoryStreamManager();

    // AssemblyName 캐시 - Type별 어셈블리 이름을 캐싱하여 반복 할당 방지
    private static readonly ConcurrentDictionary<Type, string> AssemblyNameCache = new();

    // 헤더 문자열 캐시 - byte[] 헤더 값을 문자열로 변환할 때 메모리 할당 최소화
    private static readonly ConcurrentDictionary<string, string> HeaderStringCache = new();

    public RabbitMqPublishService(
        IOptions<RabbitMqConfig> config,
        RabbitMqConnection connection,
        RabbitMqHandler handler,
        ITelemetryService telemetryService,
        ILogger<RabbitMqPublishService> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(telemetryService);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config.Value;
        _connection = connection;
        _logger = logger;
        _telemetryService = telemetryService;
        _handler = handler;

        // 요청-응답 매칭용 딕셔너리 초기화 (성능 최적화)
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<(string?, byte[]?)>>(
            Environment.ProcessorCount, 100);

        // 메모리 최적화를 위한 객체 풀 초기화
        _stringBuilderPool = new ConcurrentBag<StringBuilder>();

        // 초기 StringBuilder들을 미리 생성하여 풀에 추가
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            _stringBuilderPool.Add(new StringBuilder(64)); // traceparent 길이 고려
        }

        // Unique queue 생성 (메시지를 받기 위한 고유 queue)
        _uniqueQueue = $"{Environment.MachineName}.unique.{Ulid.NewUlid()}";
        _connection.Channel.QueueDeclareAsync(
            queue: _uniqueQueue,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null);

        // Unique consumer 설정
        _uniqueConsumer = new AsyncEventingBasicConsumer(_connection.Channel);
        _uniqueConsumer.ReceivedAsync += OnUniqueReceived;

        // Unique queue에서 메시지 수신 시작
        _connection.Channel.BasicConsumeAsync(
            queue: _uniqueQueue,
            autoAck: true,
            consumer: _uniqueConsumer);

        // Exchange들은 이미 RabbitMqConnection에서 선언됨
        // - ProducerExchangeMulti: fanout 타입
        // - ProducerExchangeAny: direct 타입
    }
    
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    // Consumer 이벤트 핸들러 해제
                    _uniqueConsumer.ReceivedAsync -= OnUniqueReceived;

                    // 대기 중인 요청들 정리 (타임아웃 처리)
                    foreach (var pendingRequest in _pendingRequests.Values)
                    {
                        pendingRequest.TrySetCanceled();
                    }
                    _pendingRequests.Clear();

                    // StringBuilder 풀 정리 (GC가 처리하도록)
                    while (_stringBuilderPool.TryTake(out _))
                    {
                        // StringBuilder 객체들을 풀에서 제거
                    }

                    // Connection 해제
                    _connection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during RabbitMqPublishService disposal");
                }
            }
            _disposed = true;
        }
    }


    /// <summary>
    /// StringBuilder를 풀에서 가져오거나 새로 생성합니다
    /// </summary>
    private StringBuilder GetStringBuilder()
    {
        if (_stringBuilderPool.TryTake(out var sb))
        {
            sb.Clear();
            return sb;
        }
        return new StringBuilder(64);
    }

    /// <summary>
    /// StringBuilder를 풀에 반환합니다
    /// </summary>
    private void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb.Capacity <= 256) // 너무 큰 StringBuilder는 풀에 반환하지 않음
        {
            _stringBuilderPool.Add(sb);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="body"></param>
    /// <param name="messageType"></param>
    /// <param name="contentType">
    /// MessagePack = "application/x-msgpack"
    /// Protocol Buffers = "application/x-protobuf"
    /// </param>
    /// <param name="correlationId"></param>
    /// <returns></returns>
    private (ReadOnlyMemory<byte> Body, BasicProperties Properties) MakeDataWithType(
        ReadOnlyMemory<byte> body,
        Type? messageType,
        string contentType,
        string? correlationId = null)
    {
        // 헤더는 매번 새로 생성 (RabbitMQ 메시지와 함께 전송되므로 풀링 불가)
        var headers = new Dictionary<string, object>(8);

        // MessagePack 타입 정보를 헤더에 추가
        if (messageType != null)
        {
            headers["message_type"] = messageType.FullName ?? messageType.Name;
            // AssemblyName 캐싱을 통한 메모리 최적화
            headers["message_assembly"] = AssemblyNameCache.GetOrAdd(messageType,
                t => t.Assembly.GetName().Name ?? string.Empty);
            headers["content_type"] = contentType;
        }

        var properties = new BasicProperties
        {
            ReplyTo = _uniqueQueue,
            CorrelationId = correlationId ?? Ulid.NewUlid().ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()),
            MessageId = Ulid.NewUlid().ToString(),
            Headers = headers!
        };

        // W3C Trace Context 표준에 따른 traceparent 헤더 추가
        if (Activity.Current != null)
        {
            // StringBuilder를 사용한 문자열 연결 최적화
            var sb = GetStringBuilder();
            try
            {
                // ToString()을 사용하되 StringBuilder로 최적화
                var traceId = Activity.Current.TraceId.ToString();
                var spanId = Activity.Current.SpanId.ToString();
                var traceFlagsValue = (byte)Activity.Current.ActivityTraceFlags;

                // StringBuilder로 traceparent 구성 (메모리 할당 최소화)
                sb.Append("00-")
                  .Append(traceId)
                  .Append('-')
                  .Append(spanId)
                  .Append('-')
                  .Append(traceFlagsValue.ToString("x2"));

                var traceParent = sb.ToString();

                // 로깅을 Debug 레벨로 변경 (프로덕션 성능 최적화)
                _logger.LogDebug("Send traceParent: {TraceParent}", traceParent);

                headers["traceparent"] = traceParent;
                headers["trace_id"] = traceId;
                headers["span_id"] = spanId;
            }
            finally
            {
                ReturnStringBuilder(sb);
            }
        }

        return (body, properties);
    }

    public async ValueTask PublishUniqueAsync<T>(
        BinaryMessageType binaryMessageType,
        string queueName,
        T message,
        CancellationToken ct = default,
        string? correlationId = null) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // RecyclableMemoryStream을 사용한 메모리 최적화
        await using var memoryStream = MemoryStreamManager.GetStream();

        string contentType = string.Empty;
        
        switch (binaryMessageType)
        {
            case BinaryMessageType.MemoryPack:
                contentType = ContentTypes.MemoryPack;
                MemoryPack.MemoryPackSerializer.Serialize(memoryStream, message);        
                break;
            case BinaryMessageType.MessagePack:
                contentType = ContentTypes.MessagePack;
                await MessagePackSerializer.SerializeAsync(memoryStream, message, cancellationToken: ct);
                break;
            case BinaryMessageType.ProtoBuf:
                contentType = ContentTypes.ProtoBuf;
                ProtoBuf.Serializer.Serialize(memoryStream, message);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binaryMessageType), binaryMessageType, null);
        }
        
        // GetBuffer()와 Length를 사용하여 불필요한 배열 복사 제거
        var buffer = memoryStream.GetBuffer();
        var serializedMemory = buffer.AsMemory(0, (int)memoryStream.Length);
        var data = MakeDataWithType(serializedMemory, typeof(T), contentType, correlationId);
        await PublishUniqueAsync(queueName, data.Body, data.Properties, ct);
    }
    
    private async ValueTask PublishUniqueAsync(
        string queueName, ReadOnlyMemory<byte> body, BasicProperties properties, CancellationToken ct = default)
    {
        using var span = _telemetryService.StartActivity("rabbitmq.publish.unique", ActivityKind.Producer, Activity.Current?.Context);
        // 특정 Consumer의 Reply queue로 직접 전송 (exchange 없이)
        await _connection.Channel.BasicPublishAsync(
            exchange: "", // Default exchange 사용 (queue 이름을 routing key로 사용)
            routingKey: queueName,
            basicProperties: properties,
            body: body,
            mandatory: false,
            cancellationToken: ct);
        _logger.LogDebug(
            "Unique Message sent. Target: {Target}, CorrelationId: {CorrelationId}",
            queueName, properties.CorrelationId);
    }

    public async ValueTask PublishMultiAsync<T>(
        BinaryMessageType binaryMessageType,
        string exchangeName,
        T msg,
        CancellationToken ct = default,
        string? correlationId = null) where T : class
    {
        switch (binaryMessageType)
        {
            case BinaryMessageType.MemoryPack:
                await PublishMemoryPackMultiAsync(exchangeName, msg, ct, correlationId);
                break;
            case BinaryMessageType.MessagePack:
                await PublishMessagePackMultiAsync(exchangeName, msg, ct, correlationId);
                break;
            case BinaryMessageType.ProtoBuf:
                await PublishProtoBufMultiAsync(exchangeName, msg, ct, correlationId);
                break;
            case BinaryMessageType.Unknown:
            default:
                throw new ArgumentOutOfRangeException(nameof(binaryMessageType), binaryMessageType, null);
        } 
    }
    
    private async ValueTask PublishMessagePackMultiAsync<T>(
        string exchangeName, T messagePack, CancellationToken ct = default, string? correlationId = null) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // RecyclableMemoryStream을 사용한 메모리 최적화 (동기 Dispose이므로 using 사용)
        using var memoryStream = MemoryStreamManager.GetStream();
        await MessagePackSerializer.SerializeAsync(memoryStream, messagePack, cancellationToken: ct);

        // GetBuffer()와 Length를 사용하여 불필요한 배열 복사 제거
        var buffer = memoryStream.GetBuffer();
        var serializedMemory = buffer.AsMemory(0, (int)memoryStream.Length);
        var data = MakeDataWithType(serializedMemory, typeof(T), ContentTypes.MessagePack, correlationId);
        await PublishMultiAsync(exchangeName, data.Body, data.Properties, ct);
    }

    public async ValueTask PublishProtoBufMultiAsync<T>(
        string exchangeName, T protoBuf, CancellationToken ct = default, string? correlationId = null)
        where T : class
    {
        // RecyclableMemoryStream은 동기 Dispose이므로 using 사용
        using var memoryStream = MemoryStreamManager.GetStream();
        ProtoBuf.Serializer.Serialize(memoryStream, protoBuf);

        // GetBuffer()와 Length를 사용하여 불필요한 배열 복사 제거
        var buffer = memoryStream.GetBuffer();
        var serializedMemory = buffer.AsMemory(0, (int)memoryStream.Length);
        var data = MakeDataWithType(serializedMemory, typeof(T), ContentTypes.ProtoBuf, correlationId);
        await PublishMultiAsync(exchangeName, data.Body, data.Properties, ct);
    }

    public async ValueTask PublishMemoryPackMultiAsync<T>(
        string exchangeName, T memoryPack, CancellationToken ct = default, string? correlationId = null)
        where T : class
    {
        // RecyclableMemoryStream은 동기 Dispose이므로 using 사용
        using var memoryStream = MemoryStreamManager.GetStream();
        await MemoryPack.MemoryPackSerializer.SerializeAsync(memoryStream, memoryPack, cancellationToken: ct);

        // GetBuffer()와 Length를 사용하여 불필요한 배열 복사 제거
        var buffer = memoryStream.GetBuffer();
        var serializedMemory = buffer.AsMemory(0, (int)memoryStream.Length);
        var data = MakeDataWithType(serializedMemory, typeof(T), ContentTypes.MemoryPack, correlationId);
        await PublishMultiAsync(exchangeName, data.Body, data.Properties, ct);
    }

    private async ValueTask PublishMultiAsync(
        string exchangeName, ReadOnlyMemory<byte> body, BasicProperties properties, CancellationToken ct = default)
    {
        using var span = _telemetryService.StartActivity("rabbitmq.publish.multi", ActivityKind.Producer, Activity.Current?.Context);
        await _connection.Channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: "", // fanout에서는 routing key 불필요
            basicProperties: properties,
            body: body,
            mandatory: false,
            cancellationToken: ct);
        _logger.LogDebug("Multi message sent: CorrelationId: {CorrelationId}", properties.CorrelationId);
    }
    
    public async ValueTask PublishAnyAsync<T>(BinaryMessageType binaryMessageType, string queueName, T message,
        CancellationToken ct = default, string? correlationId = null) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // RecyclableMemoryStream을 사용한 메모리 최적화
        await using var memoryStream = MemoryStreamManager.GetStream();

        string contentType = string.Empty;
        switch (binaryMessageType)
        {
            case BinaryMessageType.MemoryPack:
                contentType = ContentTypes.MemoryPack;
                await MemoryPack.MemoryPackSerializer.SerializeAsync(memoryStream, message, cancellationToken: ct);
                break;
            case BinaryMessageType.MessagePack:
                contentType = ContentTypes.MessagePack;
                await MessagePackSerializer.SerializeAsync(memoryStream, message, cancellationToken: ct);
                break;
            case BinaryMessageType.ProtoBuf:
                contentType = ContentTypes.ProtoBuf;
                ProtoBuf.Serializer.Serialize(memoryStream, message);
                break;
            case BinaryMessageType.Unknown:
            default:
                throw new ArgumentOutOfRangeException(nameof(binaryMessageType), binaryMessageType, null);
        }
        
        // GetBuffer()와 Length를 사용하여 불필요한 배열 복사 제거
        var buffer = memoryStream.GetBuffer();
        var serializedMemory = buffer.AsMemory(0, (int)memoryStream.Length);
        var data = MakeDataWithType(serializedMemory, typeof(T), contentType, correlationId);
        await PublishAnyAsync(queueName, data.Body, data.Properties, ct);
    }
    
    private async ValueTask PublishAnyAsync(
        string queueName, ReadOnlyMemory<byte> body, BasicProperties properties, CancellationToken ct = default)
    {
        using var span = _telemetryService.StartActivity("rabbitmq.publish.any", ActivityKind.Producer, Activity.Current?.Context);
        // Round-robin: Direct exchange를 사용하여 동일한 routing key로 바인딩된 모든 consumer가 round-robin으로 처리
        await _connection.Channel.BasicPublishAsync(
            exchange: "",
            routingKey: queueName, // 모든 Any queue가 이 routing key로 바인딩됨
            basicProperties: properties,
            body: body,
            mandatory: false,
            cancellationToken: ct);
        _logger.LogDebug(
            "Any message sent (round-robin): CorrelationId: {CorrelationId}",
            properties.CorrelationId);
    }

    public async Task<TResponse> PublishMessagePackAndWaitForResponseAsync<TRequest, TResponse>(string queueName, TRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
        where TRequest : class
        where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var correlationId = Ulid.NewUlid().ToString();
        var timeoutSpan = timeout ?? TimeSpan.FromSeconds(30);

        using var span = _telemetryService.StartActivity("rabbitmq.publish.unique.response", ActivityKind.Client, Activity.Current?.Context);
        span?.SetTag("correlation_id", correlationId);
        span?.SetTag("target", queueName);
        span?.SetTag("request_type", typeof(TRequest).Name);
        span?.SetTag("response_type", typeof(TResponse).Name);
        span?.SetTag("timeout_seconds", timeoutSpan.TotalSeconds);

        var tcs = new TaskCompletionSource<(string?, byte[]?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[correlationId] = tcs;

        try
        {
            // MessagePack 직렬화
            using var memoryStream = MemoryStreamManager.GetStream();
            await MessagePackSerializer.SerializeAsync(memoryStream, request, cancellationToken: ct);

            // GetBuffer()를 사용하여 불필요한 배열 복사 제거
            var buffer = memoryStream.GetBuffer();
            var requestBytes = buffer.AsMemory(0, (int)memoryStream.Length).ToArray();

            // 요청 메시지 전송
            await SendRequestMessageAsync(queueName, requestBytes, correlationId, ct, typeof(TRequest));

            // 응답 대기 (타임아웃 지원)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutSpan);

            var responseTask = tcs.Task;
            var timeoutTask = Task.Delay(timeoutSpan, timeoutCts.Token);

            var completedTask = await Task.WhenAny(responseTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Request timeout for correlation ID: {CorrelationId}, Target: {Target}", correlationId, queueName);
                throw new TimeoutException($"Request timeout after {timeoutSpan.TotalSeconds} seconds for queueName: {queueName}");
            }

            var (_, byteResponse) = await responseTask;

            if (byteResponse == null)
            {
                throw new InvalidOperationException("Received null response");
            }

            // MessagePack 역직렬화
            var response = MessagePackSerializer.Deserialize<TResponse>(byteResponse, cancellationToken: ct);

            _logger.LogDebug("Response received and deserialized for correlation ID: {CorrelationId}", correlationId);
            return response;
        }
        finally
        {
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }
    
    public async Task<TResponse?> PublishAndWaitForResponseAsync<TRequest, TResponse>(BinaryMessageType binaryMessageType, string queueName,
        TRequest request, TimeSpan? timeout = null, CancellationToken ct = default) where TRequest : class where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var correlationId = Ulid.NewUlid().ToString();
        var timeoutSpan = timeout ?? TimeSpan.FromSeconds(30);

        using var span = _telemetryService.StartActivity("rabbitmq.publish.unique.response", ActivityKind.Client, Activity.Current?.Context);
        span?.SetTag("correlation_id", correlationId);
        span?.SetTag("target", queueName);
        span?.SetTag("request_type", typeof(TRequest).Name);
        span?.SetTag("response_type", typeof(TResponse).Name);
        span?.SetTag("timeout_seconds", timeoutSpan.TotalSeconds);

        var tcs = new TaskCompletionSource<(string?, byte[]?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[correlationId] = tcs;

        try
        {
            string contentType = string.Empty;
            // ProtoBuf 직렬화
            await using var memoryStream = MemoryStreamManager.GetStream();
            switch (binaryMessageType)
            {
                case BinaryMessageType.MemoryPack:
                    contentType = ContentTypes.MemoryPack;
                    await MemoryPack.MemoryPackSerializer.SerializeAsync(memoryStream, request, cancellationToken: ct);
                    break;
                case BinaryMessageType.MessagePack:
                    contentType = ContentTypes.MessagePack;
                    await MessagePackSerializer.SerializeAsync(memoryStream, request, cancellationToken: ct);
                    break;
                case BinaryMessageType.ProtoBuf:
                    contentType = ContentTypes.ProtoBuf;
                    ProtoBuf.Serializer.Serialize(memoryStream, request);
                    break;
                case BinaryMessageType.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(binaryMessageType), binaryMessageType, null);
            }
            
            var requestBytes = memoryStream.ToArray();

            // 요청 메시지 전송
            await SendRequestMessageAsync(queueName, requestBytes, correlationId, ct, typeof(TRequest), contentType);

            // 응답 대기 (타임아웃 지원)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutSpan);

            var responseTask = tcs.Task;
            var timeoutTask = Task.Delay(timeoutSpan, timeoutCts.Token);

            var completedTask = await Task.WhenAny(responseTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("ProtoBuf request timeout for correlation ID: {CorrelationId}, Target: {Target}", correlationId, queueName);
                throw new TimeoutException($"ProtoBuf request timeout after {timeoutSpan.TotalSeconds} seconds for queueName: {queueName}");
            }

            var (_, byteResponse) = await responseTask;

            if (byteResponse == null)
            {
                throw new InvalidOperationException("Received null ProtoBuf response");
            }

            // 역직렬화
            TResponse? response = null;
            switch (binaryMessageType)
            {
                case BinaryMessageType.MemoryPack:
                    response = MemoryPack.MemoryPackSerializer.Deserialize<TResponse>(byteResponse);
                    break;
                case BinaryMessageType.MessagePack:
                    response = MessagePackSerializer.Deserialize<TResponse>(byteResponse, cancellationToken: ct);
                    break;
                case BinaryMessageType.ProtoBuf:
                    response = ProtoBuf.Serializer.Deserialize<TResponse>(new ReadOnlyMemory<byte>(byteResponse));
                    break;
            }
            
            _logger.LogDebug("Response received and deserialized for correlation ID: {CorrelationId}", correlationId);
            return response;
        }
        finally
        {
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }
    
    private async Task SendRequestMessageAsync(string target, byte[] body, string correlationId, CancellationToken ct, Type? messageType = null, string contentType = ContentTypes.MessagePack)
    {
        var headers = new Dictionary<string, object>();

        // 타입 정보를 헤더에 추가 (필요한 경우)
        if (messageType != null)
        {
            headers["message_type"] = messageType.FullName ?? messageType.Name;
            // AssemblyName 캐싱을 통한 메모리 최적화
            headers["message_assembly"] = AssemblyNameCache.GetOrAdd(messageType,
                t => t.Assembly.GetName().Name ?? string.Empty);
            headers["content_type"] = contentType;
        }

        var properties = new BasicProperties
        {
            ReplyTo = _uniqueQueue,
            CorrelationId = correlationId,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()),
            MessageId = Ulid.NewUlid().ToString(),
            Headers = headers!
        };

        // W3C Trace Context 표준에 따른 traceparent 헤더 추가
        if (Activity.Current != null)
        {
            // StringBuilder를 사용하여 문자열 보간 대신 최적화
            var sb = GetStringBuilder();
            try
            {
                var traceId = Activity.Current.TraceId.ToString();
                var spanId = Activity.Current.SpanId.ToString();
                var traceFlagsValue = (byte)Activity.Current.ActivityTraceFlags;

                sb.Append("00-")
                  .Append(traceId)
                  .Append('-')
                  .Append(spanId)
                  .Append('-')
                  .Append(traceFlagsValue.ToString("x2"));

                var traceParent = sb.ToString();

                headers["traceparent"] = traceParent;
                headers["trace_id"] = traceId;
                headers["span_id"] = spanId;

                _logger.LogDebug("Send traceParent: {TraceParent}", traceParent);
            }
            finally
            {
                ReturnStringBuilder(sb);
            }
        }

        // 특정 타겟 큐로 직접 메시지 전송
        await _connection.Channel.BasicPublishAsync(
            exchange: "", // Default exchange 사용
            routingKey: target,
            basicProperties: properties,
            body: body,
            mandatory: false,
            cancellationToken: ct);

        _logger.LogDebug(
            "Request sent to target: {Target}, CorrelationId: {CorrelationId}, ReplyTo: {ReplyTo}, ContentType: {ContentType}",
            target, correlationId, _uniqueQueue, contentType);
    }
    
    private async Task OnUniqueReceived(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var correlationId = ea.BasicProperties.CorrelationId;

            _logger.LogDebug("Received message in unique queue. CorrelationId: {CorrelationId}, HasHeaders: {HasHeaders}",
                correlationId, ea.BasicProperties.Headers != null);

            // 요청-응답 처리 확인
            if (!string.IsNullOrEmpty(correlationId) && _pendingRequests.TryGetValue(correlationId, out var tcs))
            {
                // 응답 타입 확인 (헤더 기반) - RabbitMQ 헤더는 byte[]로 전송됨
                var isBinaryMessage = false;
                if (ea.BasicProperties.Headers?.TryGetValue("content_type", out var contentTypeObj) == true)
                {
                    var contentType = contentTypeObj switch
                    {
                        string str => str,
                        byte[] bytes => Encoding.UTF8.GetString(bytes),
                        _ => contentTypeObj?.ToString()
                    };
                    // MessagePack, ProtoBuf, MemoryPack 응답 모두 바이너리로 처리
                    isBinaryMessage = contentType == ContentTypes.MessagePack
                        || contentType == ContentTypes.ProtoBuf
                        || contentType == ContentTypes.MemoryPack;

                    _logger.LogDebug("Response content type: {ContentType}, IsBinaryMessage: {IsBinaryMessage}",
                        contentType, isBinaryMessage);
                }

                if (isBinaryMessage)
                {
                    // 바이너리 응답 처리 (MessagePack 또는 ProtoBuf)
                    var responseBytes = ea.Body.ToArray();
                    _logger.LogDebug("Processing binary response, bytes length: {Length}", responseBytes.Length);
                    tcs.TrySetResult((null, responseBytes));
                }
                else
                {
                    // 문자열 응답 처리
                    var responseText = Encoding.UTF8.GetString(ea.Body.Span);
                    _logger.LogDebug("Processing string response: {Response}", responseText);
                    tcs.TrySetResult((responseText, null));
                }

                _logger.LogInformation("Response processed for correlation ID: {CorrelationId}", correlationId);
            }
            else
            {
                _logger.LogDebug("No pending request found for correlation ID: {CorrelationId}. Processing as regular message.", correlationId);
                // 일반 메시지 처리 (기존 로직)
                await _handler.HandleAsync(MqSenderType.Unique, ea);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reply message");
        }
    }
}
