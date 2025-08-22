using Orleans;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Orleans.Surrogates;

[RegisterConverter]
public sealed class MultiProjectionStateSurrogateConverter : IConverter<MultiProjectionState, MultiProjectionStateSurrogate>
{
    public MultiProjectionState ConvertFromSurrogate(in MultiProjectionStateSurrogate surrogate) =>
        new(
            surrogate.Payload,
            surrogate.ProjectorName,
            surrogate.ProjectorVersion,
            surrogate.LastSortableUniqueId,
            surrogate.LastEventId,
            surrogate.Version,
            surrogate.IsCatchedUp,
            surrogate.IsSafeState);

    public MultiProjectionStateSurrogate ConvertToSurrogate(in MultiProjectionState value) =>
        new(
            value.Payload,
            value.ProjectorName,
            value.ProjectorVersion,
            value.LastSortableUniqueId,
            value.LastEventId,
            value.Version,
            value.IsCatchedUp,
            value.IsSafeState);
}