namespace CosmosInfrastructure.DomainCommon.EventSourcings;

public class CosmosDocumentWriter : IDocumentPersistentWriter
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly AggregateEventPublisher _eventPublisher;

    public CosmosDocumentWriter(CosmosDbFactory cosmosDbFactory, AggregateEventPublisher eventPublisher)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _eventPublisher = eventPublisher;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : Document
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (document.DocumentType == DocumentType.AggregateEvent)
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.UpsertItemAsync(document, new PartitionKey(document.PartitionKey));
                });
        } else
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.UpsertItemAsync(document, new PartitionKey(document.PartitionKey));
                });
        }
    }

    public async Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent, Type aggregateType)
        where TAggregateEvent : AggregateEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateEvent,
            aggregateContainerGroup,
            async container =>
            {
                await container.UpsertItemAsync(aggregateEvent, new PartitionKey(aggregateEvent.PartitionKey));
            });
        await _eventPublisher.PublishAsync(aggregateEvent);
    }
}