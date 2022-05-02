namespace Sekiban.EventSourcing.Partitions.AggregateIdPartitions;

public class AggregateIdPartitionKeyFactory : IPartitionKeyFactory
{
    public Guid AggregateId { get; init; }
    public Type AggregateType { get; init; }

    public AggregateIdPartitionKeyFactory(Guid aggregateId, Type aggregateType)
    {
        AggregateId = aggregateId;
        AggregateType = aggregateType;
    }

    public string GetPartitionKey(DocumentType documentType)
    {
        return documentType switch
        {
            DocumentType.AggregateCommand => $"c_{AggregateId}",
            DocumentType.AggregateEvent => $"{AggregateType.Name}_{AggregateId}",
            DocumentType.AggregateSnapshot => $"s_{AggregateId}",
            DocumentType.IntegratedEvent => $"i_{AggregateId}",
            DocumentType.SnapshotList => $"l_{AggregateId}",
            DocumentType.SnapshotListChunk => $"l_{AggregateId}",
            _ => throw new JJInvalidDocumentTypeException()
        };
    }
}
