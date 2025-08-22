using Orleans;
using System;

namespace Sekiban.Dcb.Orleans.MultiProjections;

/// <summary>
/// Orleans 通信用 DTO (コア型を直接シリアライズしない)
/// </summary>
[GenerateSerializer]
public sealed record SerializableMultiProjectionStateDto(
    [property: Id(0)] byte[] Payload,
    [property: Id(1)] string MultiProjectionPayloadType,
    [property: Id(2)] string ProjectorName,
    [property: Id(3)] string ProjectorVersion,
    [property: Id(4)] string LastSortableUniqueId,
    [property: Id(5)] Guid LastEventId,
    [property: Id(6)] int Version,
    [property: Id(7)] bool IsCatchedUp,
    [property: Id(8)] bool IsSafeState)
{
    public static SerializableMultiProjectionStateDto FromCore(Sekiban.Dcb.MultiProjections.SerializableMultiProjectionState c)
        => new(c.Payload, c.MultiProjectionPayloadType, c.ProjectorName, c.ProjectorVersion, c.LastSortableUniqueId, c.LastEventId, c.Version, c.IsCatchedUp, c.IsSafeState);
}
