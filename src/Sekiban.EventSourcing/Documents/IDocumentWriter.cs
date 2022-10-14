namespace Sekiban.EventSourcing.Documents;

public interface IDocumentWriter
{
    Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument;
    Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent, Type aggregateType) where TAggregateEvent : IAggregateEvent;
}
public interface IDocumentPersistentWriter : IDocumentWriter
{
}
public interface IDocumentTemporaryWriter : IDocumentWriter
{
}
