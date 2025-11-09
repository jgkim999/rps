using ProtoBuf;

namespace Rps.Share.Dto;

[ProtoContract]
public class ProtobufResponse
{
    [ProtoMember(1)]
    public string ResponseId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string OriginalRequestId { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string ResponseMessage { get; set; } = string.Empty;

    [ProtoMember(4)]
    public DateTime ProcessedAt { get; set; }

    [ProtoMember(5)]
    public bool Success { get; set; }

    [ProtoMember(6)]
    public Dictionary<string, string> ResponseData { get; set; } = new();
}
