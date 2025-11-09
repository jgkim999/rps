using ProtoBuf;

namespace Rps.Share.Dto;

[ProtoContract]
public class ProtobufRequest
{
    [ProtoMember(1)]
    public string Id { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string Message { get; set; } = string.Empty;

    [ProtoMember(3)]
    public DateTime Timestamp { get; set; }

    [ProtoMember(4)]
    public Dictionary<string, string> Data { get; set; } = new();
}
