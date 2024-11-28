using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Snapshot;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentWriter : IDocumentPersistentWriter, IEventPersistentWriter
{
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream) where TEvent : IEvent
    {
        throw new NotImplementedException();
    }

    public Task SaveItemAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream) where TDocument : IDocument
    {
        throw new NotImplementedException();
    }

    public Task SaveSingleSnapshotAsync(SnapshotDocument document, IWriteDocumentStream writeDocumentStream, bool useBlob)
    {
        throw new NotImplementedException();
    }

    public bool ShouldUseBlob(SnapshotDocument document)
    {
        throw new NotImplementedException();
    }
}
