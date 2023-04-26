using Sekiban.Core.Aggregate;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
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
    public async Task<SnapshotDocument?> FillSnapshotDocumentWithBlobAsync(SnapshotDocument document)
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
    public async Task<SnapshotDocument?> FillSnapshotDocumentAsync(SnapshotDocument document) => document.Snapshot switch
    {
        null => await FillSnapshotDocumentWithBlobAsync(document),
        _ => await FillSnapshotDocumentWithJObjectAsync(document)
    };
    public async Task<SnapshotDocument?> FillSnapshotDocumentWithJObjectAsync(SnapshotDocument document)
    {
        var documentTypeName = document.DocumentTypeName;
        var aggregateTypeName = document.AggregateTypeName;
        var isAggregate = documentTypeName.Equals(aggregateTypeName);
        if (isAggregate)
        {
            var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == aggregateTypeName);
            if (aggregateType != null)
            {
                var targetClassType = typeof(AggregateState<>).MakeGenericType(aggregateType.Aggregate);
                var state = SekibanJsonHelper.ConvertTo(document.Snapshot, targetClassType);
                if (state is not null)
                {
                    var snapshot = Activator.CreateInstance(targetClassType, state, state.Payload);
                    return document with { Snapshot = snapshot };
                }
            }
        }
        else
        {

            var projectionType = _sekibanAggregateTypes.SingleProjectionTypes.FirstOrDefault(m => m.PayloadType.Name == documentTypeName);
            if (projectionType != null)
            {
                var targetClassType = typeof(SingleProjectionState<>).MakeGenericType(projectionType.PayloadType);
                var state = SekibanJsonHelper.ConvertTo(document.Snapshot, targetClassType);
                if (state is not null)
                {
                    var snapshot = Activator.CreateInstance(targetClassType, state, state.Payload);
                    return document with { Snapshot = snapshot };
                }
            }
        }
        await Task.CompletedTask;
        return null;
    }
}
