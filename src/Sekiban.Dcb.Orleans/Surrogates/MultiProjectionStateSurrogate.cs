using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public record struct MultiProjectionStateSurrogate(
    [property: Id(0)]
    IMultiProjectionPayload Payload,
    [property: Id(1)]
    string ProjectorName,
    [property: Id(2)]
    string ProjectorVersion,
    [property: Id(3)]
    string LastSortableUniqueId,
    [property: Id(4)]
    Guid LastEventId,
    [property: Id(5)]
    int Version,
    [property: Id(6)]
    bool IsCatchedUp,
    [property: Id(7)]
    bool IsSafeState);
