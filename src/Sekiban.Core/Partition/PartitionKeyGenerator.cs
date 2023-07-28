using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
namespace Sekiban.Core.Partition;

/// <summary>
///     Partition key generator
/// </summary>
public static class PartitionKeyGenerator
{
    /// <summary>
    ///     Partition Key for the Command Document
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="aggregateType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public static string ForCommand(Guid aggregateId, Type aggregateType, string rootPartitionKey) =>
        $"c_{rootPartitionKey}_{aggregateType.Name}_{aggregateId}";

    /// <summary>
    ///     Partition Key for the Event Document
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="aggregateType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public static string ForEvent(Guid aggregateId, Type aggregateType, string rootPartitionKey) =>
        $"{rootPartitionKey}_{aggregateType.Name}_{aggregateId}";

    /// <summary>
    ///     Partition Key for the Aggregate Snapshot Document
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="aggregateType"></param>
    /// <param name="projectionType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public static string ForAggregateSnapshot(Guid aggregateId, Type aggregateType, Type projectionType, string rootPartitionKey) =>
        aggregateType == projectionType || projectionType.IsAggregateSubtypePayload()
            ? $"s_{rootPartitionKey}_{aggregateType.Name}_{aggregateId}"
            : $"s_{rootPartitionKey}_{aggregateType.Name}_{projectionType.Name}_{aggregateId}";

    /// <summary>
    ///     Partition Key for the Aggregate Snapshot Document
    /// </summary>
    /// <param name="projectionType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public static string ForMultiProjectionSnapshot(Type projectionType, string rootPartitionKey)
    {
        var rootPartitionKeyToUse = IMultiProjectionService.ProjectionAllRootPartitions.Equals(rootPartitionKey)
            ? MultiProjectionSnapshotDocument.AllRootPartitionKeySnapshotValue
            : rootPartitionKey;

        if (projectionType.IsSingleProjectionListStateType())
        {
            return
                $"m_list_{projectionType.GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType().Name}_{rootPartitionKeyToUse}";
        }
        return $"m_{projectionType.Name}_{rootPartitionKeyToUse}";
    }
}
