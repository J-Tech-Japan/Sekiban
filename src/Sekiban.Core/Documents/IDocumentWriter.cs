using Sekiban.Core.Events;
namespace Sekiban.Core.Documents;

public interface IDocumentWriter
{
    Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument;
    Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType) where TEvent : IEvent;
}
public interface IDocumentPersistentWriter : IDocumentWriter
{
}
public interface IDocumentTemporaryWriter : IDocumentWriter
{
}
