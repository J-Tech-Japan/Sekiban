using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;

public class CosmosDocumentWriter : IDocumentPersistentWriter
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly EventPublisher _eventPublisher;

    public CosmosDocumentWriter(CosmosDbFactory cosmosDbFactory, EventPublisher eventPublisher)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _eventPublisher = eventPublisher;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (document.DocumentType == DocumentType.Event)
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.CreateItemAsync(document, new PartitionKey(document.PartitionKey));
                });
        }
        else
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.CreateItemAsync(document, new PartitionKey(document.PartitionKey));
                });
        }
    }

    public async Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType)
        where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container => { await container.UpsertItemAsync<dynamic>(ev, new PartitionKey(ev.PartitionKey)); });
        await _eventPublisher.PublishAsync(ev);
    }
}
