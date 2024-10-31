using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Documents;

/// <summary>
///     Split document writer depends on the Aggregate Type
///     Aggregate Payload can be marked with attribute
///     [AggregateContainerGroup(AggregateContainerGroup.Dissolvable)] dissolvable container
///     [AggregateContainerGroup(AggregateContainerGroup.InMemory)] in memory container (reset after the restart)
/// </summary>
public class DocumentWriterSplitter : IDocumentWriter
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentWriter _documentPersistentWriter;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;

    public DocumentWriterSplitter(
        IDocumentPersistentWriter documentPersistentWriter,
        IDocumentTemporaryWriter documentTemporaryWriter,
        IAggregateSettings aggregateSettings)
    {
        _documentPersistentWriter = documentPersistentWriter;
        _documentTemporaryWriter = documentTemporaryWriter;
        _aggregateSettings = aggregateSettings;
    }

    public async Task SaveAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryWriter.SaveAsync(document, writeDocumentStream);
            return;
        }

        if (document.DocumentType == DocumentType.AggregateSnapshot)
        {
        }

        if (document is IEvent)
        {
        }

        await _documentPersistentWriter.SaveAsync(document, writeDocumentStream);
    }

    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryWriter.SaveAndPublishEvents(events, writeDocumentStream);
            return;
        }
        var enumerable = events.ToList();
        await _documentPersistentWriter.SaveAndPublishEvents(enumerable, writeDocumentStream);
    }
}
