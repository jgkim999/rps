using MessagePack;

namespace Rps.Share.Dto;

/// <summary>
/// MessagePack 테스트 응답 DTO
/// </summary>
[MessagePackObject]
public class TestResponse
{
    [Key(0)]
    public string ResponseId { get; set; } = string.Empty;

    [Key(1)]
    public string OriginalRequestId { get; set; } = string.Empty;

    [Key(2)]
    public string ResponseMessage { get; set; } = string.Empty;

    [Key(3)]
    public DateTime ProcessedAt { get; set; }

    [Key(4)]
    public bool Success { get; set; }

    [Key(5)]
    public Dictionary<string, object> ResponseData { get; set; } = new();
}
