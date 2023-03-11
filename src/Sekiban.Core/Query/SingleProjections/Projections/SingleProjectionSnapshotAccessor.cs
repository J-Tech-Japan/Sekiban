using Sekiban.Core.Aggregate;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public class SingleProjectionSnapshotAccessor : ISingleProjectionSnapshotAccessor
{
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;

    public SingleProjectionSnapshotAccessor(SekibanAggregateTypes sekibanAggregateTypes) => _sekibanAggregateTypes = sekibanAggregateTypes;
    public async Task<SnapshotDocument?> SnapshotDocumentFromAggregateStateAsync<TPayload>(AggregateState<TPayload> state)
        where TPayload : IAggregatePayloadCommon, new()
    {
        await Task.CompletedTask;
        return new SnapshotDocument(
            state.AggregateId,
            typeof(TPayload),
            state.Payload.GetType(),
            state,
            state.LastEventId,
            state.LastSortableUniqueId,
            state.Version,
            state.GetPayloadVersionIdentifier());
    }
    public Task<SnapshotDocument?> SnapshotDocumentFromSingleProjectionStateAsync<TPayload>(SingleProjectionState<TPayload> state)
        where TPayload : ISingleProjectionPayloadCommon, new() => throw new NotImplementedException();
    public async Task<TState?> StateFromSnapshotDocumentAsync<TState>(SnapshotDocument document) where TState : IAggregateCommon
    {
        await Task.CompletedTask;
        return document.ToState<TState>(_sekibanAggregateTypes);
    }
}
