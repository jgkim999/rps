using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using RabbitMQ.Client.Events;
using Rps.Share.Telemetry;

namespace Rps.Share.MessageBroker;

public class RabbitMqHandler
{
    private readonly RabbitMqConnection _connection;
    private readonly ITelemetryService _telemetryService;
    private readonly IMqMessageHandler _mqMessageHandler;
    private readonly ILogger<RabbitMqHandler> _logger;

    // 타입 캐시 - 성능 최적화를 위해 한번 로드된 타입을 캐시
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    // MessagePack deserialize 메서드 캐시 - 리플렉션 호출 성능 최적화
    private static readonly ConcurrentDictionary<Type, Func<ReadOnlyMemory<byte>, MessagePackSerializerOptions, CancellationToken, object?>> MessagePackDeserializeMethodCache = new();

    // ProtoBuf deserialize 메서드 캐시 - 리플렉션 호출 성능 최적화
    private static readonly ConcurrentDictionary<Type, Func<ReadOnlyMemory<byte>, CancellationToken, object?>> ProtoBufDeserializeMethodCache = new();

    // MemoryPack deserialize 메서드 캐시 - 리플렉션 호출 성능 최적화
    private static readonly ConcurrentDictionary<Type, Func<ReadOnlyMemory<byte>, CancellationToken, object?>> MemoryPackDeserializeMethodCache = new();

    // MemoryPack serialize 메서드 캐시 - 리플렉션 호출 성능 최적화
    private static readonly ConcurrentDictionary<Type, Func<Stream, object, CancellationToken, ValueTask>> MemoryPackSerializeMethodCache = new();

    // RecyclableMemoryStreamManager - MemoryStream 풀링을 통한 메모리 최적화
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

    // 헤더 문자열 캐시 - byte[] 헤더 값을 문자열로 변환할 때 메모리 할당 최소화
    private static readonly ConcurrentDictionary<string, string> HeaderStringCache = new();

    /// <summary>
    /// RabbitMqHandler의 새 인스턴스를 초기화합니다
    /// </summary>
    /// <param name="connection">RabbitMQ 연결 인스턴스</param>
    /// <param name="mqMessageHandler">메시지 처리를 위한 핸들러</param>
    /// <param name="logger">로거 인스턴스</param>
    /// <param name="telemetryService">텔레메트리 서비스</param>
    public RabbitMqHandler(
        RabbitMqConnection connection,
        IMqMessageHandler mqMessageHandler,
        ILogger<RabbitMqHandler> logger,
        ITelemetryService telemetryService)
    {
        _connection = connection;
        _mqMessageHandler = mqMessageHandler;
        _logger = logger;
        _telemetryService = telemetryService;

        // 초기화 시점에 알려진 타입들을 미리 등록하여 첫 메시지 처리 성능 향상
        PreloadKnownTypes();
    }

    /// <summary>
    /// 알려진 메시지 타입들을 초기화 시점에 미리 등록합니다
    /// 이를 통해 첫 메시지 처리 시 발생하는 리플렉션 오버헤드를 제거합니다
    /// </summary>
    private void PreloadKnownTypes()
    {
        try
        {
            // MessagePackFormatterAttribute나 특정 인터페이스를 가진 타입들을 자동으로 찾아서 등록
            var messagingAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            var knownTypes = new List<Type>();

            foreach (var assembly in messagingAssemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract &&
                                  (HasMessagePackAttribute(t) || HasProtoBufAttribute(t) || HasMemoryPackAttribute(t)))
                        .ToList();

                    knownTypes.AddRange(types);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load types from assembly {AssemblyName}", assembly.GetName().Name);
                }
            }

            _logger.LogInformation("Found {Count} known message types to preload", knownTypes.Count);

            // 타입 캐시 미리 등록
            foreach (var type in knownTypes)
            {
                if (type.FullName != null)
                {
                    TypeCache.TryAdd(type.FullName, type);
                }
            }

            // Deserialize/Serialize 메서드 미리 컴파일
            foreach (var type in knownTypes)
            {
                try
                {
                    if (HasMessagePackAttribute(type))
                    {
                        GetMessagePackDeserializeMethod(type);
                        _logger.LogInformation("Preloaded MessagePack deserializer for {TypeName}", type.FullName);
                    }

                    if (HasProtoBufAttribute(type))
                    {
                        GetProtoBufDeserializeMethod(type);
                        _logger.LogInformation("Preloaded ProtoBuf deserializer for {TypeName}", type.FullName);
                    }

                    if (HasMemoryPackAttribute(type))
                    {
                        GetMemoryPackDeserializeMethod(type);
                        GetMemoryPackSerializeMethod(type);
                        _logger.LogInformation("Preloaded MemoryPack serializer/deserializer for {TypeName}", type.FullName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to preload serializers for type {TypeName}", type.FullName);
                }
            }

            _logger.LogInformation("Successfully preloaded {TypeCount} types, {MessagePackCount} MessagePack, {ProtoBufCount} ProtoBuf, {MemoryPackCount} MemoryPack deserializers",
                TypeCache.Count,
                MessagePackDeserializeMethodCache.Count,
                ProtoBufDeserializeMethodCache.Count,
                MemoryPackDeserializeMethodCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preloading known types");
        }
    }

    /// <summary>
    /// 타입이 MessagePack 직렬화 속성을 가지고 있는지 확인합니다
    /// </summary>
    private static bool HasMessagePackAttribute(Type type)
    {
        return type.GetCustomAttributes(typeof(MessagePackObjectAttribute), inherit: false).Length > 0;
    }

    /// <summary>
    /// 타입이 ProtoBuf 직렬화 속성을 가지고 있는지 확인합니다
    /// </summary>
    private static bool HasProtoBufAttribute(Type type)
    {
        return type.GetCustomAttributes(false)
            .Any(attr => attr.GetType().Name == "ProtoContractAttribute");
    }

    /// <summary>
    /// 타입이 MemoryPack 직렬화 속성을 가지고 있는지 확인합니다
    /// </summary>
    private static bool HasMemoryPackAttribute(Type type)
    {
        return type.GetCustomAttributes(false)
            .Any(attr => attr.GetType().Name == "MemoryPackableAttribute");
    }

    /// <summary>
    /// 수신된 메시지의 헤더에서 W3C Trace Context를 파싱하여 OpenTelemetry Activity를 생성합니다
    /// </summary>
    /// <param name="ea">RabbitMQ에서 수신된 메시지 이벤트 인자</param>
    /// <returns>생성된 Activity 또는 null (파싱 실패 시)</returns>
    private Activity? MakeActivity(BasicDeliverEventArgs ea)
    {
        try
        {
            // W3C Trace Context 표준에 따른 traceparent 헤더 파싱
            ActivityContext parentContext = default;
            var traceParentObj = ea.BasicProperties?.Headers?["traceparent"];
            string? traceparent = null;

            if (traceParentObj != null)
            {
                traceparent = traceParentObj switch
                {
                    string str => str,
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    _ => traceParentObj.ToString()
                };
            }

            if (!string.IsNullOrEmpty(traceparent))
            {
                try
                {
                    _logger.LogDebug("Receive traceParent: {TraceParent}", traceparent);
                    // traceparent 형식: version-traceid-spanid-traceflags
                    // 예: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
                    var parts = traceparent.Split('-');
                    if (parts.Length == 4 && parts[0] == "00") // version 00만 지원
                    {
                        var traceId = parts[1];
                        var spanId = parts[2];
                        var traceFlagsStr = parts[3];

                        if (traceId.Length == 32 && spanId.Length == 16 && traceFlagsStr.Length == 2)
                        {
                            var parsedTraceId = ActivityTraceId.CreateFromString(traceId.AsSpan());
                            var parsedSpanId = ActivitySpanId.CreateFromString(spanId.AsSpan());
                            var traceFlags = ActivityTraceFlags.None;
                            if (byte.TryParse(traceFlagsStr, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var flags))
                            {
                                traceFlags = (ActivityTraceFlags)flags;
                            }
                            else
                            {
                                _logger.LogDebug("Failed to parse trace flags '{TraceFlags}', using default", traceFlagsStr);
                                traceFlags = ActivityTraceFlags.Recorded;
                            }

                            parentContext = new ActivityContext(parsedTraceId, parsedSpanId, traceFlags);
                            _logger.LogDebug("Successfully parsed W3C traceparent: {Traceparent}", traceparent);
                        }
                        else
                        {
                            _logger.LogWarning("Invalid traceparent format lengths: TraceId={TraceIdLength}, SpanId={SpanIdLength}, TraceFlags={TraceFlagsLength}",
                                traceId.Length, spanId.Length, traceFlagsStr.Length);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unsupported traceparent version or invalid format: {Traceparent}", traceparent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse W3C traceparent header: {Traceparent}", traceparent);
                }
            }
            return _telemetryService.StartActivity("rabbitmq.handler", ActivityKind.Consumer, parentContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception");
            return null;
        }
    }
    
    /// <summary>
    /// RabbitMQ에서 수신된 메시지를 비동기적으로 처리합니다
    /// W3C Trace Context 파싱, 메시지 디코딩, 비즈니스 로직 처리, ACK/NACK 응답을 수행합니다
    /// </summary>
    /// <param name="senderType">메시지 발송자 타입 (Multi, Any, Unique)</param>
    /// <param name="ea">RabbitMQ에서 수신된 메시지 이벤트 인자</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>비동기 작업</returns>
    public async ValueTask HandleAsync(MqSenderType senderType, BasicDeliverEventArgs ea, CancellationToken ct = default)
    {
        try
        {
            string exchange = ea.Exchange;
            string routingKey = ea.RoutingKey;
            
            // 메시지 속성에서 Reply-To 정보 추출
            //var appId = ea.BasicProperties?.AppId;
            //var clusterId = ea.BasicProperties?.ClusterId;
            //var contentEncoding = ea.BasicProperties?.ContentEncoding;
            //var contentType = ea.BasicProperties?.ContentType;
            var correlationId = ea.BasicProperties?.CorrelationId;
            //var deliveryMode = ea.BasicProperties?.DeliveryMode;
            //var expiration = ea.BasicProperties?.Expiration;
            //var headers = ea.BasicProperties?.Headers;
            var messageId = ea.BasicProperties?.MessageId;
            //var persistent = ea.BasicProperties?.Persistent;
            //var priority = ea.BasicProperties?.Priority;
            var replyTo = ea.BasicProperties?.ReplyTo;
            //var replyToAddress = ea.BasicProperties?.ReplyToAddress;
            //var timestamp = ea.BasicProperties?.Timestamp.UnixTime;
            //var type = ea.BasicProperties?.Type;
            //var userId = ea.BasicProperties?.UserId;
            
            using var activity = MakeActivity(ea);

            ReadOnlySpan<byte> bodySpan = ea.Body.Span;

            // MessagePack 타입 정보 확인
            var binaryMessageType = GetBinaryMessageType(ea.BasicProperties?.Headers);

            _logger.LogInformation(
                "Received message from {QueueType} length: {Length}, Exchange: {Exchange}, RoutingKey: {RoutingKey}, ReplyTo: {ReplyTo}, CorrelationId: {CorrelationId}, BinaryMessageType: {BinaryMessageType}",
                senderType, bodySpan.Length, exchange, routingKey, replyTo, correlationId, binaryMessageType);

            if (binaryMessageType == BinaryMessageType.MessagePack)
            {
                // MessagePack 타입 객체를 직접 처리
                ReadOnlyMemory<byte> bodyArray = ea.Body;
                await ProcessMessagePackMessageAsync(bodyArray, ea.BasicProperties?.Headers, senderType, replyTo, correlationId, messageId, ct);
            }
            else if (binaryMessageType == BinaryMessageType.ProtoBuf)
            {
                // ProtoBuf 타입 객체를 직접 처리
                ReadOnlyMemory<byte> bodyArray = ea.Body;
                await ProcessProtoBufMessageAsync(bodyArray, ea.BasicProperties?.Headers, senderType, replyTo, correlationId, messageId, ct);
            }
            else if (binaryMessageType == BinaryMessageType.MemoryPack)
            {
                // MemoryPack 타입 객체를 직접 처리
                ReadOnlyMemory<byte> bodyArray = ea.Body;
                await ProcessMemoryPackMessageAsync(bodyArray, ea.BasicProperties?.Headers, senderType, replyTo, correlationId, messageId, ct);
            }
            else
            {
                var message = Encoding.UTF8.GetString(bodySpan);
                // 일반 문자열 메시지 처리
                var response = await _mqMessageHandler.HandleAsync(senderType, replyTo, correlationId, messageId, message, ct);

                // 응답이 있고 ReplyTo가 있는 경우 응답 전송
                if (!string.IsNullOrEmpty(response) && !string.IsNullOrEmpty(replyTo) && !string.IsNullOrEmpty(correlationId))
                {
                    await SendBinaryMessageResponseAsync(BinaryMessageType.Unknown, replyTo, response, correlationId, ct);
                }
            }
            // 성공적으로 처리된 경우 Ack 전송
            await _connection.Channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            
            _logger.LogDebug("Message acknowledged for {QueueType} queue, DeliveryTag: {DeliveryTag}",
                senderType, ea.DeliveryTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {QueueType} queue", senderType);
            await _connection.Channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }
    
    /// <summary>
    /// RabbitMQ 헤더 값을 안전하게 문자열로 변환합니다
    /// RabbitMQ는 헤더 값을 byte[]로 전송하므로 적절한 변환이 필요합니다
    /// 문자열 캐싱을 통해 동일한 헤더 값에 대한 반복 할당을 방지합니다
    /// </summary>
    /// <param name="headerValue">헤더 값</param>
    /// <returns>변환된 문자열 또는 null</returns>
    private static string? GetHeaderValueAsString(object? headerValue)
    {
        return headerValue switch
        {
            null => null,
            string str => str,
            byte[] bytes => GetCachedStringFromBytes(bytes),
            _ => headerValue.ToString()
        };
    }

    /// <summary>
    /// byte 배열을 문자열로 변환하고 결과를 캐시합니다
    /// 동일한 헤더 값(message_type, content_type 등)이 반복적으로 나타날 때 메모리 할당 감소
    /// </summary>
    /// <param name="bytes">변환할 byte 배열</param>
    /// <returns>캐시된 문자열</returns>
    private static string GetCachedStringFromBytes(byte[] bytes)
    {
        // Base64 인코딩을 사용하여 byte 배열을 고유 키로 변환
        // 짧은 문자열(헤더 값)의 경우 Base64가 효율적
        var key = Convert.ToBase64String(bytes);

        return HeaderStringCache.GetOrAdd(key, _ => Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// 메시지 헤더를 확인하여 MessagePack 메시지인지 판단합니다
    /// RabbitMQ 헤더 값이 byte 배열로 전송되는 것을 고려하여 안전하게 처리합니다
    /// </summary>
    /// <param name="headers">메시지 헤더</param>
    /// <returns>MessagePack 메시지 여부</returns>
    private static BinaryMessageType GetBinaryMessageType(IDictionary<string, object?>? headers)
    {
        if (headers == null)
            return BinaryMessageType.Unknown;

        var contentType = GetHeaderValueAsString(headers.TryGetValue("content_type", out var contentTypeValue) ? contentTypeValue : null);
        //var messageType = GetHeaderValueAsString(headers.TryGetValue("message_type", out var messageTypeValue) ? messageTypeValue : null);

        return contentType switch
        {
            ContentTypes.MessagePack => BinaryMessageType.MessagePack,
            ContentTypes.ProtoBuf => BinaryMessageType.ProtoBuf,
            ContentTypes.MemoryPack => BinaryMessageType.MemoryPack,
            _ => BinaryMessageType.Unknown
        };
    }

    /// <summary>
    /// 효율적인 타입 로딩을 위한 헬퍼 메서드
    /// 캐시를 사용하여 이미 로드된 타입을 재사용합니다
    /// 로드된 모든 어셈블리에서 타입을 검색합니다
    /// </summary>
    /// <param name="messageTypeName">타입의 전체 이름</param>
    /// <returns>타입 객체 또는 null</returns>
    private Type? GetTypeFromCache(string messageTypeName)
    {
        return TypeCache.GetOrAdd(messageTypeName, typeName =>
        {
            // 1. 먼저 Type.GetType으로 시도 (mscorlib 및 현재 어셈블리)
            var type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type != null)
            {
                _logger.LogDebug("Type {TypeName} found via Type.GetType() and added to cache", typeName);
                return type;
            }

            // 2. 로드된 모든 어셈블리에서 타입 검색
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                    {
                        _logger.LogDebug("Type {TypeName} found in assembly {AssemblyName} and added to cache",
                            typeName, assembly.GetName().Name);
                        return type;
                    }
                }
                catch (Exception)
                {
                    // 어셈블리 접근 오류 시 무시하고 계속
                }
            }

            _logger.LogWarning("Type {TypeName} not found in any assembly and null cached", typeName);
            return null;
        });
    }

    /// <summary>
    /// MessagePack deserialize 메서드를 컴파일된 델리게이트로 캐시합니다
    /// 리플렉션 호출을 피하여 성능을 크게 향상시킵니다
    /// </summary>
    /// <param name="messageType">deserialize할 메시지 타입</param>
    /// <returns>컴파일된 deserialize 델리게이트</returns>
    private Func<ReadOnlyMemory<byte>, MessagePackSerializerOptions, CancellationToken, object?> GetMessagePackDeserializeMethod(Type messageType)
    {
        return MessagePackDeserializeMethodCache.GetOrAdd(messageType, type =>
        {
            _logger.LogDebug("Creating deserialize method for type {TypeName} and adding to cache", type.FullName);

            // MessagePackSerializer.Deserialize<T>(ReadOnlyMemory<byte>, MessagePackSerializerOptions, CancellationToken) 메서드 가져오기
            var deserializeMethod = typeof(MessagePackSerializer)
                .GetMethod("Deserialize", new[] { typeof(ReadOnlyMemory<byte>), typeof(MessagePackSerializerOptions), typeof(CancellationToken) })
                ?.MakeGenericMethod(type);

            if (deserializeMethod == null)
            {
                _logger.LogError("Could not find MessagePackSerializer.Deserialize method for type {TypeName}", type.FullName);
                throw new InvalidOperationException($"Could not find MessagePackSerializer.Deserialize method for type {type.FullName}");
            }

            // Expression Tree를 사용하여 컴파일된 델리게이트 생성
            var bytesParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "bytes");
            var optionsParam = Expression.Parameter(typeof(MessagePackSerializerOptions), "options");
            var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

            var methodCall = Expression.Call(deserializeMethod, bytesParam, optionsParam, cancellationTokenParam);
            var convertToObject = Expression.Convert(methodCall, typeof(object));

            var lambda = Expression.Lambda<Func<ReadOnlyMemory<byte>, MessagePackSerializerOptions, CancellationToken, object?>>(
                convertToObject, bytesParam, optionsParam, cancellationTokenParam);

            var compiledDelegate = lambda.Compile();
            _logger.LogDebug("Successfully compiled deserialize method for type {TypeName} and cached", type.FullName);

            return compiledDelegate;
        });
    }
    
    /// <summary>
    /// ProtoBuf deserialize 메서드를 컴파일된 델리게이트로 캐시합니다
    /// 리플렉션 호출을 피하여 성능을 크게 향상시킵니다
    /// </summary>
    /// <param name="messageType">deserialize할 메시지 타입</param>
    /// <returns>컴파일된 deserialize 델리게이트</returns>
    private Func<ReadOnlyMemory<byte>, CancellationToken, object?> GetProtoBufDeserializeMethod(Type messageType)
    {
        return ProtoBufDeserializeMethodCache.GetOrAdd(messageType, type =>
        {
            _logger.LogDebug("Creating ProtoBuf deserialize method for type {TypeName} and adding to cache", type.FullName);

            // ProtoBuf.Serializer.Deserialize<T>(ReadOnlyMemory<byte> source) 메서드 가져오기
            var deserializeMethod = typeof(ProtoBuf.Serializer)
                .GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "Deserialize" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length >= 1 &&
                    m.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>))
                ?.MakeGenericMethod(type);

            if (deserializeMethod == null)
            {
                _logger.LogError("Could not find ProtoBuf.Serializer.Deserialize method for type {TypeName}", type.FullName);
                throw new InvalidOperationException($"Could not find ProtoBuf.Serializer.Deserialize method for type {type.FullName}");
            }

            // Expression Tree를 사용하여 컴파일된 델리게이트 생성
            var bytesParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "bytes");
            var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

            // ProtoBuf.Serializer.Deserialize<T>(bytes, default(T), null) 호출
            var defaultValueParam = Expression.Default(type);
            var userStateParam = Expression.Constant(null, typeof(object));

            var methodCall = Expression.Call(deserializeMethod, bytesParam, defaultValueParam, userStateParam);
            var convertToObject = Expression.Convert(methodCall, typeof(object));

            var lambda = Expression.Lambda<Func<ReadOnlyMemory<byte>, CancellationToken, object?>>(
                convertToObject, bytesParam, cancellationTokenParam);

            var compiledDelegate = lambda.Compile();
            _logger.LogDebug("Successfully compiled ProtoBuf deserialize method for type {TypeName} and cached", type.FullName);

            return compiledDelegate;
        });
    }

    /// <summary>
    /// MemoryPack deserialize 메서드를 컴파일된 델리게이트로 캐시합니다
    /// 리플렉션 호출을 피하여 성능을 크게 향상시킵니다
    /// </summary>
    /// <param name="messageType">deserialize할 메시지 타입</param>
    /// <returns>컴파일된 deserialize 델리게이트</returns>
    private Func<ReadOnlyMemory<byte>, CancellationToken, object?> GetMemoryPackDeserializeMethod(Type messageType)
    {
        return MemoryPackDeserializeMethodCache.GetOrAdd(messageType, type =>
        {
            _logger.LogDebug("Creating MemoryPack deserialize method for type {TypeName} and adding to cache", type.FullName);

            // MemoryPackSerializer.Deserialize<T>(ReadOnlySpan<byte>, MemoryPackSerializerOptions) 메서드 가져오기
            var deserializeMethod = typeof(MemoryPack.MemoryPackSerializer)
                .GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "Deserialize" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(ReadOnlySpan<byte>) &&
                    m.GetParameters()[1].ParameterType == typeof(MemoryPack.MemoryPackSerializerOptions))
                ?.MakeGenericMethod(type);

            if (deserializeMethod == null)
            {
                _logger.LogError("Could not find MemoryPackSerializer.Deserialize method for type {TypeName}", type.FullName);
                throw new InvalidOperationException($"Could not find MemoryPackSerializer.Deserialize method for type {type.FullName}");
            }

            // Expression Tree를 사용하여 컴파일된 델리게이트 생성
            var bytesParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "bytes");
            var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

            // ReadOnlyMemory<byte>를 ReadOnlySpan<byte>으로 변환하기 위한 Span 속성 접근
            var spanProperty = typeof(ReadOnlyMemory<byte>).GetProperty("Span");
            var spanAccess = Expression.Property(bytesParam, spanProperty!);

            // null을 MemoryPackSerializerOptions로 전달 (기본 옵션 사용)
            var optionsParam = Expression.Constant(null, typeof(MemoryPack.MemoryPackSerializerOptions));

            // MemoryPackSerializer.Deserialize<T>(span, options) 호출
            var methodCall = Expression.Call(deserializeMethod, spanAccess, optionsParam);
            var convertToObject = Expression.Convert(methodCall, typeof(object));

            var lambda = Expression.Lambda<Func<ReadOnlyMemory<byte>, CancellationToken, object?>>(
                convertToObject, bytesParam, cancellationTokenParam);

            var compiledDelegate = lambda.Compile();
            _logger.LogDebug("Successfully compiled MemoryPack deserialize method for type {TypeName} and cached", type.FullName);

            return compiledDelegate;
        });
    }

    /// <summary>
    /// MemoryPack serialize 메서드를 컴파일된 델리게이트로 캐시합니다
    /// 리플렉션 호출을 피하여 성능을 크게 향상시킵니다
    /// </summary>
    /// <param name="messageType">serialize할 메시지 타입</param>
    /// <returns>컴파일된 serialize 델리게이트</returns>
    private Func<Stream, object, CancellationToken, ValueTask> GetMemoryPackSerializeMethod(Type messageType)
    {
        return MemoryPackSerializeMethodCache.GetOrAdd(messageType, type =>
        {
            _logger.LogDebug("Creating MemoryPack serialize method for type {TypeName} and adding to cache", type.FullName);

            // MemoryPackSerializer.SerializeAsync<T>(Stream, T, MemoryPackSerializerOptions, CancellationToken) 메서드 가져오기
            var serializeMethod = typeof(MemoryPack.MemoryPackSerializer)
                .GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "SerializeAsync" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 4 &&
                    m.GetParameters()[0].ParameterType == typeof(Stream) &&
                    m.GetParameters()[2].ParameterType == typeof(MemoryPack.MemoryPackSerializerOptions) &&
                    m.GetParameters()[3].ParameterType == typeof(CancellationToken))
                ?.MakeGenericMethod(type);

            if (serializeMethod == null)
            {
                _logger.LogError("Could not find MemoryPackSerializer.SerializeAsync method for type {TypeName}", type.FullName);
                throw new InvalidOperationException($"Could not find MemoryPackSerializer.SerializeAsync method for type {type.FullName}");
            }

            // Expression Tree를 사용하여 컴파일된 델리게이트 생성
            var streamParam = Expression.Parameter(typeof(Stream), "stream");
            var valueParam = Expression.Parameter(typeof(object), "value");
            var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

            // object를 실제 타입으로 변환
            var typedValue = Expression.Convert(valueParam, type);

            // null을 MemoryPackSerializerOptions로 전달 (기본 옵션 사용)
            var optionsParam = Expression.Constant(null, typeof(MemoryPack.MemoryPackSerializerOptions));

            // MemoryPackSerializer.SerializeAsync<T>(stream, value, options, ct) 호출
            var methodCall = Expression.Call(serializeMethod, streamParam, typedValue, optionsParam, cancellationTokenParam);

            var lambda = Expression.Lambda<Func<Stream, object, CancellationToken, ValueTask>>(
                methodCall, streamParam, valueParam, cancellationTokenParam);

            var compiledDelegate = lambda.Compile();
            _logger.LogDebug("Successfully compiled MemoryPack serialize method for type {TypeName} and cached", type.FullName);

            return compiledDelegate;
        });
    }

    /// <summary>
    /// MessagePack 메시지를 처리하여 타입 객체로 deserialize하고 직접 핸들러에 전달합니다
    /// 타입 정보를 헤더에서 읽어와 해당 타입으로 deserialize한 후 핸들러에 전달합니다
    /// </summary>
    /// <param name="bodyArray">메시지 본문</param>
    /// <param name="headers">메시지 헤더</param>
    /// <param name="senderType">메시지 발송자 타입</param>
    /// <param name="sender">메시지 발송자 식별자</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>비동기 작업</returns>
    private async ValueTask ProcessMessagePackMessageAsync(
        ReadOnlyMemory<byte> bodyArray,
        IDictionary<string, object?>? headers,
        MqSenderType senderType,
        string? sender,
        string? correlationId,
        string? messageId,
        CancellationToken ct)
    {
        try
        {
            if (headers == null)
            {
                _logger.LogWarning("MessagePack message received but headers are null");
                return;
            }

            var messageTypeName = GetHeaderValueAsString(headers.TryGetValue("message_type", out var messageTypeValue) ? messageTypeValue : null);

            if (string.IsNullOrEmpty(messageTypeName))
            {
                _logger.LogWarning("MessagePack message received but message_type header is missing");
                return;
            }

            // 캐시에서 타입 가져오기 (어셈블리 순회 대신)
            var messageType = GetTypeFromCache(messageTypeName);

            if (messageType == null)
            {
                _logger.LogWarning("Could not resolve type {MessageType}. Available assemblies: {Assemblies}",
                    messageTypeName,
                    string.Join(", ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name)));
                return;
            }
          
            // MessagePack deserialize - 컴파일된 델리게이트 사용으로 성능 최적화
            try
            {
                var deserializeFunc = GetMessagePackDeserializeMethod(messageType);
                var deserializedObject = deserializeFunc(bodyArray, MessagePackSerializerOptions.Standard, ct);

                if (deserializedObject == null)
                {
                    _logger.LogWarning("MessagePack deserialization returned null for type {MessageType}", messageTypeName);
                    return;
                }

                // 타입 객체를 직접 핸들러에 전달
                _logger.LogDebug("Calling HandleBinaryMessageAsync for type {MessageType}, sender: {Sender}, correlationId: {CorrelationId}",
                    messageType.FullName, sender, correlationId);

                var response = await _mqMessageHandler.HandleBinaryMessageAsync(senderType, sender, correlationId, messageId, deserializedObject, messageType, ct);

                _logger.LogDebug("HandleBinaryMessageAsync returned response: {Response} (type: {ResponseType})",
                    response.ResponseType, response.Message?.GetType().FullName ?? "null");

                // 응답이 있고 ReplyTo가 있는 경우 응답 전송
                if (response.Message != null && !string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogInformation("Sending response to {Sender} with correlationId {CorrelationId}", sender, correlationId);
                    await SendBinaryMessageResponseAsync(response.ResponseType, sender, response, correlationId, ct);
                }
                else
                {
                    _logger.LogWarning("No response sent - Response: {Response}, Sender: {Sender}, CorrelationId: {CorrelationId}",
                        response, sender, correlationId);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Could not create deserialize method for type {MessageType}", messageTypeName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MessagePack message");
        }
    }
    
    /// <summary>
    /// ProtoBuf 메시지를 처리하여 타입 객체로 deserialize하고 직접 핸들러에 전달합니다
    /// 타입 정보를 헤더에서 읽어와 해당 타입으로 deserialize한 후 핸들러에 전달합니다
    /// </summary>
    /// <param name="bodyArray">메시지 본문</param>
    /// <param name="headers">메시지 헤더</param>
    /// <param name="senderType">메시지 발송자 타입</param>
    /// <param name="replyTo">메시지 발송자 식별자</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>비동기 작업</returns>
    private async Task ProcessProtoBufMessageAsync(
        ReadOnlyMemory<byte> bodyArray,
        IDictionary<string, object?>? headers,
        MqSenderType senderType,
        string? replyTo,
        string? correlationId,
        string? messageId,
        CancellationToken ct)
    {
        try
        {
            if (headers == null)
            {
                _logger.LogWarning("ProtoBuf message received but headers are null");
                return;
            }

            var messageTypeName = GetHeaderValueAsString(headers.TryGetValue("message_type", out var messageTypeValue) ? messageTypeValue : null);

            if (string.IsNullOrEmpty(messageTypeName))
            {
                _logger.LogWarning("ProtoBuf message received but message_type header is missing");
                return;
            }

            // 캐시에서 타입 가져오기 (어셈블리 순회 대신)
            var messageType = GetTypeFromCache(messageTypeName);

            if (messageType == null)
            {
                _logger.LogWarning("Could not resolve type {MessageType}. Available assemblies: {Assemblies}",
                    messageTypeName,
                    string.Join(", ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name)));
                return;
            }

            // ProtoBuf deserialize - 컴파일된 델리게이트 사용으로 성능 최적화
            try
            {
                var deserializeFunc = GetProtoBufDeserializeMethod(messageType);
                var deserializedObject = deserializeFunc(bodyArray, ct);

                if (deserializedObject == null)
                {
                    _logger.LogWarning("ProtoBuf deserialization returned null for type {MessageType}", messageTypeName);
                    return;
                }

                // 타입 객체를 직접 핸들러에 전달
                _logger.LogDebug("Calling HandleProtoBufAsync for type {MessageType}, replyTo: {ReplyTo}, correlationId: {CorrelationId}",
                    messageType.FullName, replyTo, correlationId);

                var response = await _mqMessageHandler.HandleBinaryMessageAsync(senderType, replyTo, correlationId, messageId, deserializedObject, messageType, ct);

                _logger.LogDebug("HandleProtoBufAsync returned response: {Response} (type: {ResponseType})",
                    response.ResponseType, response.Message?.GetType().FullName ?? "null");

                // 응답이 있고 ReplyTo가 있는 경우 응답 전송
                if (response.Message != null && !string.IsNullOrEmpty(replyTo) && !string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogInformation("Sending response to {ReplyTo} with correlationId {CorrelationId}", replyTo, correlationId);
                    await SendBinaryMessageResponseAsync(BinaryMessageType.ProtoBuf, replyTo, response, correlationId, ct);
                }
                else
                {
                    _logger.LogWarning("No response sent - Response: {Response}, ReplyTo: {ReplyTo}, CorrelationId: {CorrelationId}",
                        response, replyTo, correlationId);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Could not create deserialize method for type {MessageType}", messageTypeName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ProtoBuf message");
        }
    }

    /// <summary>
    /// MemoryPack 메시지를 처리하여 타입 객체로 deserialize하고 직접 핸들러에 전달합니다
    /// 타입 정보를 헤더에서 읽어와 해당 타입으로 deserialize한 후 핸들러에 전달합니다
    /// </summary>
    /// <param name="bodyArray">메시지 본문</param>
    /// <param name="headers">메시지 헤더</param>
    /// <param name="senderType">메시지 발송자 타입</param>
    /// <param name="replyTo">메시지 발송자 식별자</param>
    /// <param name="correlationId">메시지 상관 관계 ID</param>
    /// <param name="messageId">메시지 고유 ID</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>비동기 작업</returns>
    private async Task ProcessMemoryPackMessageAsync(
        ReadOnlyMemory<byte> bodyArray,
        IDictionary<string, object?>? headers,
        MqSenderType senderType,
        string? replyTo,
        string? correlationId,
        string? messageId,
        CancellationToken ct)
    {
        try
        {
            if (headers == null)
            {
                _logger.LogWarning("MemoryPack message received but headers are null");
                return;
            }

            var messageTypeName = GetHeaderValueAsString(headers.TryGetValue("message_type", out var messageTypeValue) ? messageTypeValue : null);

            if (string.IsNullOrEmpty(messageTypeName))
            {
                _logger.LogWarning("MemoryPack message received but message_type header is missing");
                return;
            }

            // 캐시에서 타입 가져오기 (어셈블리 순회 대신)
            var messageType = GetTypeFromCache(messageTypeName);

            if (messageType == null)
            {
                _logger.LogWarning("Could not resolve type {MessageType}. Available assemblies: {Assemblies}",
                    messageTypeName,
                    string.Join(", ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name)));
                return;
            }

            // MemoryPack deserialize - 컴파일된 델리게이트 사용으로 성능 최적화
            try
            {
                var deserializeFunc = GetMemoryPackDeserializeMethod(messageType);
                var deserializedObject = deserializeFunc(bodyArray, ct);

                if (deserializedObject == null)
                {
                    _logger.LogWarning("MemoryPack deserialization returned null for type {MessageType}", messageTypeName);
                    return;
                }

                // 타입 객체를 직접 핸들러에 전달
                _logger.LogDebug("Calling HandleMemoryPackAsync for type {MessageType}, replyTo: {ReplyTo}, correlationId: {CorrelationId}",
                    messageType.FullName, replyTo, correlationId);

                var response = await _mqMessageHandler.HandleBinaryMessageAsync(senderType, replyTo, correlationId, messageId, deserializedObject, messageType, ct);

                _logger.LogDebug("HandleMemoryPackAsync returned response: {Response} (type: {ResponseType})",
                    response.ResponseType, response.Message?.GetType().FullName ?? "null");

                // 응답이 있고 ReplyTo가 있는 경우 응답 전송
                if (response.Message != null && !string.IsNullOrEmpty(replyTo) && !string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogInformation("Sending response to {ReplyTo} with correlationId {CorrelationId}", replyTo, correlationId);
                    await SendBinaryMessageResponseAsync(BinaryMessageType.MemoryPack, replyTo, response, correlationId, ct);
                }
                else
                {
                    _logger.LogWarning("No response sent - Response: {Response}, ReplyTo: {ReplyTo}, CorrelationId: {CorrelationId}",
                        response, replyTo, correlationId);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Could not create deserialize method for type {MessageType}", messageTypeName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MemoryPack message");
        }
    }

    /// <summary>
    /// 응답을 RabbitMQ를 통해 전송합니다
    /// </summary>
    /// <param name="binaryMessageType">바이너리 메시지 타입 (MessagePack, ProtoBuf 등)</param>
    /// <param name="replyTo">응답을 보낼 큐 이름</param>
    /// <param name="response">응답 객체 (문자열 또는 객체)</param>
    /// <param name="correlationId">상관 관계 ID</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>비동기 작업</returns>
    private async ValueTask SendBinaryMessageResponseAsync(
        BinaryMessageType binaryMessageType, string replyTo, object response, string correlationId, CancellationToken ct)
    {
        try
        {
            using var span = _telemetryService.StartActivity("rabbitmq.response", ActivityKind.Producer, Activity.Current?.Context);
            span?.SetTag("correlation_id", correlationId);
            span?.SetTag("reply_to", replyTo);

            byte[] responseBody;
            var headers = new Dictionary<string, object>();

            // 응답 타입에 따라 직렬화 방식 결정
            if (response is string stringResponse)
            {
                // 문자열 응답
                responseBody = Encoding.UTF8.GetBytes(stringResponse);
                _logger.LogDebug("Sending string response to {ReplyTo}, Length: {Length}", replyTo, responseBody.Length);
            }
            else
            {
                // 객체 응답 - BinaryMessageType에 따라 직렬화
                // RecyclableMemoryStream을 사용하여 메모리 풀링 적용
                await using var memoryStream = MemoryStreamManager.GetStream();
                var responseType = response.GetType();

                switch (binaryMessageType)
                {
                    case BinaryMessageType.MessagePack:
                        await MessagePackSerializer.SerializeAsync(memoryStream, response, cancellationToken: ct);
                        headers["content_type"] = ContentTypes.MessagePack;
                        break;
                    case BinaryMessageType.ProtoBuf:
                        ProtoBuf.Serializer.Serialize(memoryStream, response);
                        headers["content_type"] = ContentTypes.ProtoBuf;
                        break;
                    case BinaryMessageType.MemoryPack:
                        // MemoryPack serialize - 컴파일된 델리게이트 사용으로 성능 최적화
                        var serializeFunc = GetMemoryPackSerializeMethod(responseType);
                        await serializeFunc(memoryStream, response, ct);
                        headers["content_type"] = ContentTypes.MemoryPack;
                        break;
                    case BinaryMessageType.Unknown:
                    default:
                        // 기본값은 MessagePack
                        await MessagePackSerializer.SerializeAsync(memoryStream, response, cancellationToken: ct);
                        headers["content_type"] = ContentTypes.MessagePack;
                        break;
                }

                // ToArray() 대신 GetBuffer()와 Length 사용하여 불필요한 메모리 할당 방지
                responseBody = memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length).ToArray();

                headers["message_type"] = responseType.FullName ?? responseType.Name;
                headers["message_assembly"] = responseType.Assembly.GetName().Name ?? string.Empty;

                _logger.LogDebug("Sending {BinaryMessageType} response to {ReplyTo}, Type: {Type}, Length: {Length}",
                    binaryMessageType, replyTo, responseType.Name, responseBody.Length);
            }

            // 응답 속성 설정
            var properties = new RabbitMQ.Client.BasicProperties
            {
                CorrelationId = correlationId,
                Timestamp = new RabbitMQ.Client.AmqpTimestamp(new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()),
                MessageId = Ulid.NewUlid().ToString(),
                Headers = headers!
            };

            // W3C Trace Context 추가
            if (Activity.Current != null)
            {
                var traceParent = $"00-{Activity.Current.TraceId}-{Activity.Current.SpanId}-{(byte)Activity.Current.ActivityTraceFlags:x2}";
                headers["traceparent"] = traceParent;
                headers["trace_id"] = Activity.Current.TraceId.ToString();
                headers["span_id"] = Activity.Current.SpanId.ToString();
            }

            // 응답 전송 (Default exchange 사용, replyTo를 routing key로 사용)
            await _connection.Channel.BasicPublishAsync(
                exchange: "", // Default exchange
                routingKey: replyTo,
                basicProperties: properties,
                body: responseBody,
                mandatory: false,
                cancellationToken: ct);

            _logger.LogInformation("Response sent successfully to {ReplyTo} with CorrelationId: {CorrelationId}",
                replyTo, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send response to {ReplyTo} with CorrelationId: {CorrelationId}",
                replyTo, correlationId);
        }
    }
}