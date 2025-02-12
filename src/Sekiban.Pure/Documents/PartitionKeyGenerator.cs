namespace Sekiban.Pure.Documents;

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
    /// <param name="group"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public static string ForEvent(PartitionKeys partitionKeys) =>
        partitionKeys.ToPrimaryKeysString();
}