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
            DocumentType.AggregateSnapshot => $"s_{AggregateType.Name}_{AggregateId}",
            _ => throw new JJInvalidDocumentTypeException()
        };
    }
}
