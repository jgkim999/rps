namespace Rps.Share.MessageBroker;

/// <summary>
/// RabbitMQ 메시지 Content-Type 상수 관리
/// 바이너리 직렬화 포맷의 MIME 타입을 중앙에서 관리합니다
/// </summary>
public static class ContentTypes
{
    /// <summary>
    /// MessagePack 직렬화 포맷
    /// https://msgpack.org/
    /// </summary>
    public const string MessagePack = "application/x-msgpack";

    /// <summary>
    /// Protocol Buffers 직렬화 포맷
    /// https://protobuf.dev/
    /// </summary>
    public const string ProtoBuf = "application/x-protobuf";

    /// <summary>
    /// MemoryPack 직렬화 포맷 (Cysharp/MemoryPack)
    /// https://github.com/Cysharp/MemoryPack
    /// </summary>
    public const string MemoryPack = "application/x-memorypack";

    /// <summary>
    /// 일반 텍스트 (기본값)
    /// </summary>
    public const string PlainText = "text/plain";

    /// <summary>
    /// JSON 포맷
    /// </summary>
    public const string Json = "application/json";
}
