using Sekiban.Core.Aggregate;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjectionSnapshotAccessor
{
    Task<SnapshotDocument?> SnapshotDocumentFromAggregateStateAsync<TPayload>(AggregateState<TPayload> state)
        where TPayload : IAggregatePayloadCommon;
    Task<SnapshotDocument?> SnapshotDocumentFromSingleProjectionStateAsync<TPayload>(SingleProjectionState<TPayload> state, Type aggregateType)
        where TPayload : ISingleProjectionPayloadCommon, new();

    Task<SnapshotDocument?> FillSnapshotDocumentWithBlobAsync(SnapshotDocument document);
    Task<SnapshotDocument?> FillSnapshotDocumentAsync(SnapshotDocument document);
    Task<SnapshotDocument?> FillSnapshotDocumentWithJObjectAsync(SnapshotDocument document);
}
