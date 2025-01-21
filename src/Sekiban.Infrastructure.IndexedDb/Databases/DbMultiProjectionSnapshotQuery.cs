using Sekiban.Core.Aggregate;
using Sekiban.Core.Partition;

namespace Sekiban.Infrastructure.IndexedDb;

public record DbMultiProjectionSnapshotQuery
{
    public string? AggregateContainerGroup { get; init; } = string.Empty;
    public string? PartitionKey { get; init; } = string.Empty;
    public string? PayloadVersionIdentifier { get; init; } = string.Empty;

    public bool IsLatestOnly { get; init; }

    public static DbMultiProjectionSnapshotQuery ForGetLatest(Type multiProjectionPayloadType, string payloadVersionIdentifier, string rootPartitionKey) =>
        new()
        {
            AggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType).ToString(),
            PartitionKey = PartitionKeyGenerator.ForMultiProjectionSnapshot(multiProjectionPayloadType, rootPartitionKey),
            PayloadVersionIdentifier = payloadVersionIdentifier,
            IsLatestOnly = true,
        };
}
