using MemoryPack;

namespace Rps.Share.Dto;

[MemoryPackable]
public partial class MemoryPackResponse
{
    public string ResponseId { get; set; } = string.Empty;
    public string OriginalRequestId { get; set; } = string.Empty;
    public string ResponseMessage { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public bool Success { get; set; }
    public Dictionary<string, string> ResponseData { get; set; } = new();
}
