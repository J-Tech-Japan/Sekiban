using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections;
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

    public IAggregateStateCommon? GetState() => Snapshot as IAggregateStateCommon;

    public TProjection? ToProjection<TProjection>(SekibanAggregateTypes sekibanAggregateTypes) where TProjection : IAggregateCommon
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
