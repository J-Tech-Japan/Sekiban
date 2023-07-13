using Sekiban.Core.Events;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

public interface IDocumentWriter
{
    Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument;
    Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, Type aggregateType) where TEvent : IEvent;
}
public interface IDocumentPersistentWriter : IDocumentWriter
{
    Task SaveSingleSnapshotAsync(SnapshotDocument document, Type aggregateType, bool useBlob);
    bool ShouldUseBlob(SnapshotDocument document);
}
public interface IDocumentTemporaryWriter : IDocumentWriter
{
}
