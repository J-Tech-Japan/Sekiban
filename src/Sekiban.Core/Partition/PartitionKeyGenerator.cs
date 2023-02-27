namespace Sekiban.Core.Partition;

public static class PartitionKeyGenerator
{
    public static string ForCommand(Guid aggregateId, Type aggregateType)
    {
        return $"c_{aggregateType.Name}_{aggregateId}";
    }

    public static string ForEvent(Guid aggregateId, Type aggregateType)
    {
        return $"{aggregateType.Name}_{aggregateId}";
    }

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType, Type projectionType)
    {
        return aggregateType == projectionType
            ? $"s_{aggregateType.Name}_{aggregateId}" : $"s_{aggregateType.Name}_{projectionType.Name}_{aggregateId}";
    }

    public static string ForMultiProjectionSnapshot(Type projectionType)
    {
        return $"m_{projectionType.Name}";
    }
}
