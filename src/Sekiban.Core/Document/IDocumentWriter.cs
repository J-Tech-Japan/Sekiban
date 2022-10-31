using Sekiban.Core.Event;
namespace Sekiban.Core.Document;

public interface IDocumentWriter
{
    Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument;
    Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent, Type aggregateType) where TAggregateEvent : IEvent;
}
public interface IDocumentPersistentWriter : IDocumentWriter
{
}
public interface IDocumentTemporaryWriter : IDocumentWriter
{
}
