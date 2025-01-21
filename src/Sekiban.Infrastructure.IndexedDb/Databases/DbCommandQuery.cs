using Sekiban.Core.Aggregate;
using Sekiban.Core.Partition;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbCommandQuery
{
    public string? SortableIdStart { get; init; }
    public string? PartitionKey { get; init; }
    public string? AggregateContainerGroup { get; init; }

    public static DbCommandQuery ForAggregateId(Guid aggregateId, Type aggregatePayloadType, string? sinceSortableUniqueId, string rootPartitionKey) =>
        new()
        {
            SortableIdStart = sinceSortableUniqueId,
            PartitionKey = PartitionKeyGenerator.ForCommand(aggregateId, aggregatePayloadType, rootPartitionKey),
            AggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType).ToString(),
        };
}
