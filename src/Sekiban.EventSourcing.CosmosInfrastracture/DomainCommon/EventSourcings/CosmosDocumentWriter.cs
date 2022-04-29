namespace CosmosInfrastructure.DomainCommon.EventSourcings;

public class CosmosDocumentWriter : IDocumentWriter
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly AggregateEventPublisher _eventPublisher;

    public CosmosDocumentWriter(
        CosmosDbFactory cosmosDbFactory,
        AggregateEventPublisher eventPublisher)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _eventPublisher = eventPublisher;
    }

    public async Task SaveAsync<TDocument>(TDocument document)
        where TDocument : Document
    {
        if (document.DocumentType == DocumentType.AggregateEvent)
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                async container =>
                {
                    await container.UpsertItemAsync(
                        document,
                        new PartitionKey(document.PartitionKey));
                });
        }
        else
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                async container =>
                {
                    await container.UpsertItemAsync(
                        document,
                        new PartitionKey(document.PartitionKey));
                });
        }
    }

    public async Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent)
        where TAggregateEvent : AggregateEvent
    {
        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateEvent,
            async container =>
            {
                await container.UpsertItemAsync(
                    aggregateEvent,
                    new PartitionKey(aggregateEvent.PartitionKey));
            });
        await _eventPublisher.PublishAsync(aggregateEvent);
    }
}
