namespace Sekiban.Core.Partition;

public static class PartitionKeyGenerator
{
    public static string ForAggregateCommand(Guid aggregateId, Type aggregateType)
    {
        return $"c_{aggregateType.Name}_{aggregateId}";
    }

    public static string ForAggregateEvent(Guid aggregateId, Type aggregateType)
    {
        return $"{aggregateType.Name}_{aggregateId}";
    }

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType)
    {
        return $"s_{aggregateType.Name}_{aggregateId}";
    }
}
