namespace Sekiban.Core.Partition;

public static class PartitionKeyGenerator
{
    public static string ForCommand(Guid aggregateId, Type aggregateType) => $"c_{aggregateType.Name}_{aggregateId}";

    public static string ForEvent(Guid aggregateId, Type aggregateType) => $"{aggregateType.Name}_{aggregateId}";

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType, Type projectionType) => aggregateType == projectionType
        ? $"s_{aggregateType.Name}_{aggregateId}" : $"s_{aggregateType.Name}_{projectionType.Name}_{aggregateId}";
}
