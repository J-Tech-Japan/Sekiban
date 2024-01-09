using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Snapshot;

/// <summary>
///     Snapshot document for single projection aggregate. This document will save to persistent storage.
/// </summary>
public record SnapshotDocument : Document
{

    public dynamic? Snapshot { get; init; }
    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;
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
        string payloadVersionIdentifier,
        string rootPartitionKey) : base(
        aggregateId,
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType, payloadType, rootPartitionKey),
        DocumentType.AggregateSnapshot,
        payloadType.Name,
        aggregateType.Name,
        rootPartitionKey)
    {
        Snapshot = stateToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
        PayloadVersionIdentifier = payloadVersionIdentifier;
    }

    public IAggregateStateCommon? GetState() => Snapshot as IAggregateStateCommon;

    public static TProjection? ToProjection<TProjection>(SekibanAggregateTypes sekibanAggregateTypes) where TProjection : IAggregateCommon
    {
        var projectionType = typeof(TProjection);
        if (!projectionType.IsGenericType) { return default; }
        if (projectionType.GetGenericTypeDefinition() == typeof(Aggregate<>))
        {

        }
        if (projectionType.GetGenericTypeDefinition() == typeof(SingleProjection<>))
        {

        }
        return default;
    }

    public string FilenameForSnapshot() => $"{DocumentTypeName}_{AggregateId}_{SavedVersion}_{PayloadVersionIdentifier}.json";
}
