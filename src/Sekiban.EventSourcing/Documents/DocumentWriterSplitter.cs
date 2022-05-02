namespace Sekiban.EventSourcing.Documents;

public class DocumentWriterSplitter : IDocumentWriter
{
    private readonly IDocumentPersistentWriter _documentPersistentWriter;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    public DocumentWriterSplitter(IDocumentPersistentWriter documentPersistentWriter, IDocumentTemporaryWriter documentTemporaryWriter)
    {
        _documentPersistentWriter = documentPersistentWriter;
        _documentTemporaryWriter = documentTemporaryWriter;
    }

    public  Task SaveAsync<TDocument>(TDocument document, Type aggregateType)
        where TDocument : Document
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            return _documentTemporaryWriter.SaveAsync(document, aggregateType);
        return _documentPersistentWriter.SaveAsync(document, aggregateType);
    }
    public Task SaveAndPublishAggregateEvent<TAggregateEvent>(
        TAggregateEvent aggregateEvent,
        Type aggregateType) where TAggregateEvent : AggregateEvent
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
            return _documentTemporaryWriter.SaveAndPublishAggregateEvent(aggregateEvent, aggregateType);
        return _documentPersistentWriter.SaveAndPublishAggregateEvent(aggregateEvent, aggregateType);
    }
}
