using Sekiban.Core.Aggregate;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjectionSnapshotAccessor
{
    Task<SnapshotDocument?> SnapshotDocumentFromAggregateStateAsync<TPayload>(AggregateState<TPayload> state)
        where TPayload : IAggregatePayloadCommon, new();
    Task<SnapshotDocument?> SnapshotDocumentFromSingleProjectionStateAsync<TPayload>(SingleProjectionState<TPayload> state, Type aggregateType)
        where TPayload : ISingleProjectionPayloadCommon, new();

    Task<TState?> StateFromSnapshotDocumentAsync<TState>(SnapshotDocument document)
        where TState : IAggregateCommon;

    Task<SnapshotDocument?> FillSnapshotDocumentWithBlob(SnapshotDocument document);
}
