namespace Sekiban.EventSourcing.Partitions;

public static class PartitionKeyGenerator
{
    public static string ForAggregateCommand(Guid aggregateId, Type aggregateType) =>
        $"c_{aggregateType.Name}_{aggregateId}";

    public static string ForAggregateEvent(Guid aggregateId, Type aggregateType) =>
        $"{aggregateType.Name}_{aggregateId}";

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType) =>
        $"s_{aggregateType.Name}_{aggregateId}";
}
