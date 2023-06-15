using Sekiban.Core.Types;
namespace Sekiban.Core.Partition;

public static class PartitionKeyGenerator
{
    public static string ForCommand(Guid aggregateId, Type aggregateType, string rootPartitionKey) =>
        $"c_{rootPartitionKey}_{aggregateType.Name}_{aggregateId}";

    public static string ForEvent(Guid aggregateId, Type aggregateType, string rootPartitionKey) =>
        $"{rootPartitionKey}_{aggregateType.Name}_{aggregateId}";

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType, Type projectionType, string rootPartitionKey) =>
        aggregateType == projectionType || projectionType.IsAggregateSubtypePayload()
            ? $"s_{rootPartitionKey}_{aggregateType.Name}_{aggregateId}"
            : $"s_{rootPartitionKey}_{aggregateType.Name}_{projectionType.Name}_{aggregateId}";

    public static string ForMultiProjectionSnapshot(Type projectionType, string rootPartitionKey)
    {
        if (projectionType.IsSingleProjectionListStateType())
        {
            return
                $"m_list_{projectionType.GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType().Name}_{rootPartitionKey}";
        }
        return $"m_{projectionType.Name}_{rootPartitionKey}";
    }
}
