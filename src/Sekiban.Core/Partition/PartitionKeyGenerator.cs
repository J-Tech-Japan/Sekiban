namespace Sekiban.Core.Partition;

public static class PartitionKeyGenerator
{
    public static string ForCommand(Guid aggregateId, Type aggregateType) => $"c_{aggregateType.Name}_{aggregateId}";

    public static string ForEvent(Guid aggregateId, Type aggregateType) => $"{aggregateType.Name}_{aggregateId}";

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType) => $"s_{aggregateType.Name}_{aggregateId}";
}
