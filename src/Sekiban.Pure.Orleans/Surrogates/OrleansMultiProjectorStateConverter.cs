using Orleans;
using Sekiban.Pure.Projectors;

namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansMultiProjectorStateConverter : IConverter<MultiProjectionState, OrleansMultiProjectorState>
{
    public MultiProjectionState ConvertFromSurrogate(in OrleansMultiProjectorState surrogate) =>
        new(surrogate.ProjectorCommon,
            surrogate.LastEventId,
            surrogate.LastSortableUniqueId,
            surrogate.Version,
            surrogate.AppliedSnapshotVersion,
            surrogate.RootPartitionKey);

    public OrleansMultiProjectorState ConvertToSurrogate(in MultiProjectionState value) =>
        new(value.ProjectorCommon,
            value.LastEventId,
            value.LastSortableUniqueId,
            value.Version,
            value.AppliedSnapshotVersion,
            value.RootPartitionKey);
}

[RegisterConverter]
public sealed class OrleansMultiProjectorStateConverter<TMultiProjector> : 
    IConverter<MultiProjectionState<TMultiProjector>, OrleansMultiProjectorState>
    where TMultiProjector : IMultiProjector<TMultiProjector>
{
    public MultiProjectionState<TMultiProjector> ConvertFromSurrogate(in OrleansMultiProjectorState surrogate)
    {
        if (surrogate.ProjectorCommon is not TMultiProjector projector)
        {
            throw new InvalidOperationException($"Expected projector of type {typeof(TMultiProjector).Name}");
        }

        return new MultiProjectionState<TMultiProjector>(
            projector,
            surrogate.LastEventId,
            surrogate.LastSortableUniqueId,
            surrogate.AppliedSnapshotVersion,
            surrogate.Version,
            surrogate.RootPartitionKey);
    }

    public OrleansMultiProjectorState ConvertToSurrogate(in MultiProjectionState<TMultiProjector> value) =>
        new(value.Payload,
            value.LastEventId,
            value.LastSortableUniqueId,
            value.Version,
            value.AppliedSnapshotVersion,
            value.RootPartitionKey);
}
