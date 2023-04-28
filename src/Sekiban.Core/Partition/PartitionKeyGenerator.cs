using Sekiban.Core.Types;
namespace Sekiban.Core.Partition;

public static class PartitionKeyGenerator
{
    public static string ForCommand(Guid aggregateId, Type aggregateType) => $"c_{aggregateType.Name}_{aggregateId}";

    public static string ForEvent(Guid aggregateId, Type aggregateType) => $"{aggregateType.Name}_{aggregateId}";

    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType, Type projectionType) =>
        aggregateType == projectionType || projectionType.IsAggregateSubtypePayload()
            ? $"s_{aggregateType.Name}_{aggregateId}" : $"s_{aggregateType.Name}_{projectionType.Name}_{aggregateId}";

    public static string ForMultiProjectionSnapshot(Type projectionType)
    {
        if (projectionType.IsSingleProjectionListStateType())
        {
            return $"m_list_{projectionType.GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType().Name}";
        }
        return $"m_{projectionType.Name}";
    }
}
