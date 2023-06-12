using Sekiban.Core.Documents;
using Sekiban.Core.Partition;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
namespace Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;

public class CosmosPartitionGenerator
{
    public static PartitionKey ForEvent(string rootPartitionKey, Type aggregateType, Guid aggregateId) =>
        new PartitionKeyBuilder().Add(rootPartitionKey)
            .Add(aggregateType.GetBaseAggregatePayloadTypeFromAggregate().Name)
            .Add(PartitionKeyGenerator.ForEvent(aggregateId, aggregateType, rootPartitionKey))
            .Build();
    public static PartitionKey ForCommand(string rootPartitionKey, Type aggregateType, Guid aggregateId) =>
        new PartitionKeyBuilder().Add(rootPartitionKey)
            .Add(aggregateType.GetBaseAggregatePayloadTypeFromAggregate().Name)
            .Add(PartitionKeyGenerator.ForCommand(aggregateId, aggregateType, rootPartitionKey))
            .Build();

    public static PartitionKey ForSingleProjectionSnapshot(string rootPartitionKey, Type aggregateType, Type projectionType, Guid aggregateId) =>
        new PartitionKeyBuilder().Add(rootPartitionKey)
            .Add(aggregateType.GetBaseAggregatePayloadTypeFromAggregate().Name)
            .Add(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType, projectionType, rootPartitionKey))
            .Build();

    public static PartitionKey ForMultiProjectionSnapshot(string rootPartitionKey, Type projectionType) =>
        new PartitionKeyBuilder().Add(rootPartitionKey)
            .Add(MultiProjectionSnapshotDocument.DocumentTypeNameFromProjectionType(projectionType))
            .Add(PartitionKeyGenerator.ForMultiProjectionSnapshot(projectionType, rootPartitionKey))
            .Build();

    public static PartitionKey ForDocument(IDocument document) =>
        new PartitionKeyBuilder().Add(document.RootPartitionKey).Add(document.AggregateType).Add(document.PartitionKey).Build();
}
