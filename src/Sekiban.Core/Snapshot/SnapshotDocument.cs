using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Snapshot;

public record SnapshotDocument : Document, IDocument
{
    public SnapshotDocument()
    {
    }

    public SnapshotDocument(
        Guid aggregateId,
        Type aggregateType,
        Type payloadType,
        IAggregateCommon stateToSnapshot,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion,
        string payloadVersionIdentifier) : base(
        aggregateId,
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType, payloadType),
        DocumentType.AggregateSnapshot,
        payloadType.Name ?? string.Empty)
    {
        Snapshot = stateToSnapshot;
        AggregateTypeName = aggregateType.Name;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
        PayloadVersionIdentifier = payloadVersionIdentifier;
    }

    public dynamic? Snapshot { get; init; }

    public string AggregateTypeName { get; init; } = string.Empty;

    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;

    public T? ToState<T>(SekibanAggregateTypes? sekibanAggregateTypes = null) where T : IAggregateCommon
    {

        // if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(AggregateState<>))
        // {
        //     var aggregateType = sekibanAggregateTypes?.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == AggregateTypeName);
        //     if (aggregateType != null)
        //     {
        //         var state = SekibanJsonHelper.ConvertTo(Snapshot, typeof(AggregateState<>).MakeGenericType(aggregateType.Aggregate));
        //         if (state is not null)
        //         {
        //             return Activator.CreateInstance(typeof(T), state, state.Payload);
        //         }
        //     }
        // }
        // if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(SingleProjectionState<>))
        // {
        //     var projectionType = sekibanAggregateTypes?.SingleProjectionTypes.FirstOrDefault(m => m.PayloadType.Name == DocumentTypeName);
        //     if (projectionType != null)
        //     {
        //         var state = SekibanJsonHelper.ConvertTo(Snapshot, typeof(SingleProjectionState<>).MakeGenericType(projectionType.PayloadType));
        //         if (state is not null)
        //         {
        //             return Activator.CreateInstance(typeof(T), state, state.Payload);
        //         }
        //     }
        // }
        if (Snapshot is T t)
        {
            return t;
        }
        var baseType = typeof(T);
        if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(AggregateState<>))
        {
            if (AggregateTypeName != typeof(T).Name && sekibanAggregateTypes != null)
            {
                var aggregateType = sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == AggregateTypeName);
                if (aggregateType != null)
                {
                    var state = SekibanJsonHelper.ConvertTo(Snapshot, typeof(AggregateState<>).MakeGenericType(aggregateType.Aggregate));
                    if (state is not null)
                    {
                        return Activator.CreateInstance(typeof(T), state, state.Payload);
                    }
                }
            }
        }
        return SekibanJsonHelper.ConvertTo<T>(Snapshot);
    }

    public string FilenameForSnapshot() => $"{DocumentTypeName}_{AggregateId}_{SavedVersion}_{PayloadVersionIdentifier}.json";
}
