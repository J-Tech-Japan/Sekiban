using Sekiban.Pure.Documents;
namespace Sekiban.Pure.OrleansEventSourcing;

public static class PartitionKeysExtensions
{
    public static OrleansPartitionKeys ToOrleansPartitionKeys(this PartitionKeys partitionKeys) =>
        new(partitionKeys.AggregateId, partitionKeys.Group, partitionKeys.RootPartitionKey);
}