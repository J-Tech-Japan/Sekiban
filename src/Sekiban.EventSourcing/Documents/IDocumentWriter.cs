using Sekiban.EventSourcing.AggregateEvents;
namespace Sekiban.EventSourcing.Documents;

public interface IDocumentWriter
{
    Task SaveAsync<TDocument>(TDocument document) where TDocument : Document;
    Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent) where TAggregateEvent : AggregateEvent;
}
