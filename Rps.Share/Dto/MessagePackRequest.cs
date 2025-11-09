using MessagePack;

namespace Rps.Share.Dto;

/// <summary>
/// MessagePack 테스트 요청 DTO
/// </summary>
[MessagePackObject]
public class MessagePackRequest
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public DateTime Timestamp { get; set; }

    [Key(3)]
    public Dictionary<string, object> Data { get; set; } = new();
}
