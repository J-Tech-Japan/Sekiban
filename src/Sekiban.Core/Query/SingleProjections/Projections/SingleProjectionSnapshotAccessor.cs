using Sekiban.Core.Aggregate;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Single Projection Snapshot Accessor implementation.
///     Aggregate developer does not need to use this class directly
/// </summary>
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
        where TPayload : IAggregatePayloadCommon
    {
        await Task.CompletedTask;
        var parentAggregateType = typeof(TPayload).GetBaseAggregatePayloadTypeFromAggregate();
        return new SnapshotDocument(
            state.AggregateId,
            parentAggregateType,
            state.Payload.GetType(),
            state,
            state.LastEventId,
            state.LastSortableUniqueId,
            state.Version,
            state.GetPayloadVersionIdentifier(),
            state.RootPartitionKey);
    }

    public async Task<SnapshotDocument?> SnapshotDocumentFromSingleProjectionStateAsync<TPayload>(
        SingleProjectionState<TPayload> state,
        Type aggregateType) where TPayload : class, ISingleProjectionPayloadCommon
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
            state.GetPayloadVersionIdentifier(),
            state.RootPartitionKey);
        return snapshotDocument;
    }

    public async Task<SnapshotDocument?> FillSnapshotDocumentWithBlobAsync(SnapshotDocument document)
    {
        try
        {
            if (document.Snapshot is not null) { return document; }
            var stream = await _blobAccessor.GetBlobWithGZipAsync(SekibanBlobContainer.SingleProjectionState, document.FilenameForSnapshot());
            if (stream is null) { return null; }
            using var reader = new StreamReader(stream);
            var snapshotString = await reader.ReadToEndAsync();
            switch (GetStateType(document))
            {
                case StateType.Aggregate:
                {
                    var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == document.AggregateType);
                    if (aggregateType is null) { return null; }
                    var aggregateStateType = typeof(AggregateState<>).MakeGenericType(aggregateType.Aggregate);
                    var state = JsonSerializer.Deserialize(snapshotString, aggregateStateType);
                    if (state != null)
                    {
                        return document with { Snapshot = state };
                    }
                }
                    break;
                case StateType.AggregateSubtype:
                {
                    var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == document.AggregateType);
                    if (aggregateType is null) { return null; }
                    var subAggregateStateType
                        = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == document.DocumentTypeName);
                    if (subAggregateStateType is null) { return null; }
                    var aggregateStateType = typeof(AggregateState<>).MakeGenericType(subAggregateStateType.Aggregate);
                    var state = JsonSerializer.Deserialize(snapshotString, aggregateStateType);
                    if (state != null)
                    {
                        return document with { Snapshot = state };
                    }
                }
                    break;
                case StateType.SingleProjection:
                    foreach (var singleProjection in _sekibanAggregateTypes.SingleProjectionTypes)
                    {
                        if (singleProjection.SingleProjectionPayloadType.Name == document.DocumentTypeName)
                        {
                            var aggregateStateType = typeof(SingleProjectionState<>).MakeGenericType(singleProjection.SingleProjectionPayloadType);
                            var state = JsonSerializer.Deserialize(snapshotString, aggregateStateType);
                            if (state != null)
                            {
                                return document with { Snapshot = state };
                            }
                        }
                    }
                    break;
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    public async Task<SnapshotDocument?> FillSnapshotDocumentAsync(SnapshotDocument document) =>
        document.Snapshot switch
        {
            null => await FillSnapshotDocumentWithBlobAsync(document),
            _ => await FillSnapshotDocumentWithJObjectAsync(document)
        };

    public async Task<SnapshotDocument?> FillSnapshotDocumentWithJObjectAsync(SnapshotDocument document)
    {
        switch (GetStateType(document))
        {
            case StateType.Aggregate:
            {
                var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == document.AggregateType);
                if (aggregateType == null) { return null; }
                var targetClassType = typeof(AggregateState<>).MakeGenericType(aggregateType.Aggregate);
                var state = SekibanJsonHelper.ConvertTo(document.Snapshot, targetClassType);
                if (state is not null)
                {
                    var snapshot = Activator.CreateInstance(targetClassType, state, state.Payload);
                    return document with { Snapshot = snapshot };
                }
            }
                break;
            case StateType.AggregateSubtype:
            {
                var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == document.AggregateType);
                if (aggregateType is null) { return null; }
                var subAggregateStateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == document.DocumentTypeName);
                if (subAggregateStateType is null) { return null; }
                var aggregateStateType = typeof(AggregateState<>).MakeGenericType(subAggregateStateType.Aggregate);
                var state = SekibanJsonHelper.ConvertTo(document.Snapshot, aggregateStateType);
                if (state is not null)
                {
                    var snapshot = Activator.CreateInstance(aggregateStateType, state, state.Payload);
                    return document with { Snapshot = snapshot };
                }
            }

                break;
            case StateType.SingleProjection:
            {
                var projectionType
                    = _sekibanAggregateTypes.SingleProjectionTypes.FirstOrDefault(
                        m => m.SingleProjectionPayloadType.Name == document.DocumentTypeName);
                if (projectionType != null)
                {
                    var targetClassType = typeof(SingleProjectionState<>).MakeGenericType(projectionType.SingleProjectionPayloadType);
                    var state = SekibanJsonHelper.ConvertTo(document.Snapshot, targetClassType);
                    if (state is not null)
                    {
                        var snapshot = Activator.CreateInstance(targetClassType, state, state.Payload);
                        return document with { Snapshot = snapshot };
                    }
                }
            }
                break;
            default:
                return null;
        }

        await Task.CompletedTask;
        return null;
    }

    private StateType GetStateType(SnapshotDocument document)
    {
        if (document.AggregateType == document.DocumentTypeName)
            return StateType.Aggregate;

        if (_sekibanAggregateTypes.AggregateTypes.Any(m => m.Aggregate.Name == document.DocumentTypeName))
            return StateType.AggregateSubtype;

        return StateType.SingleProjection;
    }

    private enum StateType
    {
        Aggregate = 1, AggregateSubtype, SingleProjection
    }
}
