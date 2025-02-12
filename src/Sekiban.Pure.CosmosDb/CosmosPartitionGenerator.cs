using Microsoft.Azure.Cosmos;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.CosmosDb;

public class CosmosPartitionGenerator
{
    public static PartitionKey ForEvent(IEventDocument document) =>
        new PartitionKeyBuilder()
            .Add(document.RootPartitionKey)
            .Add(document.AggregateGroup)
            .Add(document.PartitionKey)
            .Build();
    public static PartitionKey ForAggregate(PartitionKeys partitionKeys) =>
        new PartitionKeyBuilder()
            .Add(partitionKeys.RootPartitionKey)
            .Add(partitionKeys.Group)
            .Add(partitionKeys.ToPrimaryKeysString())
            .Build();

}