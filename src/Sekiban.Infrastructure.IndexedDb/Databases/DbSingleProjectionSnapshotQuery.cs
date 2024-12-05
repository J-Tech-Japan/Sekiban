using Sekiban.Core.Aggregate;
using Sekiban.Core.Partition;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbSingleProjectionSnapshotQuery
{
    public string? Id { get; init; }
    public string? AggregateContainerGroup { get; init; }
    public string? PartitionKey { get; init; }
    public string? AggregateId { get; init; }
    public string? RootPartitionKey { get; init; }
    public string? AggregateType { get; init; }
    public string? PayloadVersionIdentifier { get; init; }
    public int? SavedVersion { get; init; }

    public bool IsLatestOnly { get; init; }

    private DbSingleProjectionSnapshotQuery(Guid? id, Guid? aggregateId, Type? aggregatePayloadType, Type? projectionPayloadType, int? version, string? rootPartitionKey, string? payloadVersionIdentifier, bool isLatestOnly)
    {
        Id = id?.ToString();
        AggregateContainerGroup = aggregatePayloadType != null ?
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType).ToString() : null;
        PartitionKey = (aggregateId != null && aggregatePayloadType != null && projectionPayloadType != null && rootPartitionKey != null) ?
            PartitionKeyGenerator.ForAggregateSnapshot(aggregateId.Value, aggregatePayloadType, projectionPayloadType, rootPartitionKey) : null;
        AggregateId = aggregateId?.ToString();
        RootPartitionKey = rootPartitionKey;
        AggregateType = aggregatePayloadType?.Name;
        PayloadVersionIdentifier = payloadVersionIdentifier;
        SavedVersion = version;
        IsLatestOnly = isLatestOnly;
    }

    public DbSingleProjectionSnapshotQuery(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier,
        bool isLatestOnly
    ) : this(
        id: null,
        aggregateId: aggregateId,
        aggregatePayloadType: aggregatePayloadType,
        projectionPayloadType: projectionPayloadType,
        version: null,
        rootPartitionKey: rootPartitionKey,
        payloadVersionIdentifier: payloadVersionIdentifier,
        isLatestOnly: isLatestOnly
    )
    { }

    public DbSingleProjectionSnapshotQuery(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier,
        bool isLatestOnly
    ) : this(
        id: null,
        aggregateId: aggregateId,
        aggregatePayloadType: aggregatePayloadType,
        projectionPayloadType: projectionPayloadType,
        version: version,
        rootPartitionKey: rootPartitionKey,
        payloadVersionIdentifier: payloadVersionIdentifier,
        isLatestOnly: isLatestOnly
    )
    { }

    public DbSingleProjectionSnapshotQuery(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        string partitionKey,
        string rootPartitionKey,
        bool isLatestOnly
    )
    {
        Id = id.ToString();
        AggregateId = aggregateId.ToString();
        AggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType).ToString();
        PartitionKey = partitionKey;
        RootPartitionKey = rootPartitionKey;
        IsLatestOnly = isLatestOnly;
    }

    public DbSingleProjectionSnapshotQuery(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        bool isLatestOnly
    ) : this(
        id: null,
        aggregateId: aggregateId,
        aggregatePayloadType: aggregatePayloadType,
        projectionPayloadType: projectionPayloadType,
        version: null,
        rootPartitionKey: rootPartitionKey,
        payloadVersionIdentifier: null,
        isLatestOnly: isLatestOnly
    )
    { }
}
