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

    public static DbSingleProjectionSnapshotQuery ForGetLatest(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey, string payloadVersionIdentifier) =>
        new()
        {
            AggregateContainerGroup = ToAggregateContainerGroup(aggregatePayloadType),
            PartitionKey = ToPartitionKey(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey),
            AggregateId = aggregateId.ToString(),
            RootPartitionKey = rootPartitionKey,
            AggregateType = aggregatePayloadType.Name,
            PayloadVersionIdentifier = payloadVersionIdentifier,
            IsLatestOnly = true,
        };

    public static DbSingleProjectionSnapshotQuery ForTestExistence(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, int version, string rootPartitionKey, string payloadVersionIdentifier) =>
        new()
        {
            AggregateContainerGroup = ToAggregateContainerGroup(aggregatePayloadType),
            PartitionKey = ToPartitionKey(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey),
            AggregateId = aggregateId.ToString(),
            RootPartitionKey = rootPartitionKey,
            AggregateType = aggregatePayloadType.Name,
            PayloadVersionIdentifier = payloadVersionIdentifier,
            SavedVersion = version,
            IsLatestOnly = true,
        };

    public static DbSingleProjectionSnapshotQuery ForGetById(Guid id, Guid aggregateId, Type aggregatePayloadType, string partitionKey, string rootPartitionKey) =>
        new()
        {
            Id = id.ToString(),
            AggregateContainerGroup = ToAggregateContainerGroup(aggregatePayloadType),
            PartitionKey = partitionKey,
            AggregateId = aggregateId.ToString(),
            RootPartitionKey = rootPartitionKey,
            AggregateType = aggregatePayloadType.Name,
            IsLatestOnly = true,
        };

    public static DbSingleProjectionSnapshotQuery ForGetAll(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey) =>
        new()
        {
            AggregateContainerGroup = ToAggregateContainerGroup(aggregatePayloadType),
            PartitionKey = ToPartitionKey(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey),
            AggregateId = aggregateId.ToString(),
            RootPartitionKey = rootPartitionKey,
            AggregateType = aggregatePayloadType.Name,
            IsLatestOnly = false,
        };

    private static string ToAggregateContainerGroup(Type aggregatePayloadType) =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType).ToString();

    private static string ToPartitionKey(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey) =>
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey);
}
