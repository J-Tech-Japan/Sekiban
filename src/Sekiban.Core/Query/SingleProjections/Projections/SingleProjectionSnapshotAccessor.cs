using Sekiban.Core.Aggregate;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public class SingleProjectionSnapshotAccessor : ISingleProjectionSnapshotAccessor
{
    private readonly IBlobAccessor _blobAccessor;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    public SingleProjectionSnapshotAccessor(SekibanAggregateTypes sekibanAggregateTypes, IBlobAccessor blobAccessor)
    {
        _sekibanAggregateTypes = sekibanAggregateTypes;
        _blobAccessor = blobAccessor;
    }
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
    public async Task<SnapshotDocument?> SnapshotDocumentFromSingleProjectionStateAsync<TPayload>(
        SingleProjectionState<TPayload> state,
        Type aggregateType)
        where TPayload : ISingleProjectionPayloadCommon, new()
    {
        await Task.CompletedTask;
        var snapshotDocument = new SnapshotDocument(
            state.AggregateId,
            aggregateType,
            state.Payload.GetType(),
            state,
            state.LastEventId,
            state.LastSortableUniqueId,
            state.Version,
            state.GetPayloadVersionIdentifier());
        return snapshotDocument;
    }
    public async Task<TState?> StateFromSnapshotDocumentAsync<TState>(SnapshotDocument document) where TState : IAggregateCommon
    {
        await Task.CompletedTask;
        return document.ToState<TState>(_sekibanAggregateTypes);
    }
    public async Task<SnapshotDocument?> FillSnapshotDocumentWithBlob(SnapshotDocument document)
    {
        if (document.Snapshot is not null) { return document; }
        var stream = await _blobAccessor.GetBlobWithGZipAsync(SekibanBlobContainer.SingleProjectionState, document.FilenameForSnapshot());
        if (stream is null) { return null; }
        using var reader = new StreamReader(stream);
        var snapshotString = await reader.ReadToEndAsync();
        foreach (var aggregate in _sekibanAggregateTypes.AggregateTypes)
        {
            if (aggregate.Aggregate.Name == document.DocumentTypeName)
            {
                var aggregateStateType = typeof(AggregateState<>).MakeGenericType(aggregate.Aggregate);
                var state = JsonSerializer.Deserialize(snapshotString, aggregateStateType);
                if (state != null)
                {
                    return document with { Snapshot = state };
                }
            }
        }

        foreach (var singleProjection in _sekibanAggregateTypes.SingleProjectionTypes)
        {
            if (singleProjection.PayloadType.Name == document.DocumentTypeName)
            {
                var aggregateStateType = typeof(SingleProjectionState<>).MakeGenericType(singleProjection.PayloadType);
                var state = JsonSerializer.Deserialize(snapshotString, aggregateStateType);
                if (state != null)
                {
                    return document with { Snapshot = state };
                }
            }
        }
        return null;
    }
}
