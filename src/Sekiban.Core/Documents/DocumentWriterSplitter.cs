using Sekiban.Core.Aggregate;
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
    private readonly HybridStoreManager _hybridStoreManager;

    public DocumentWriterSplitter(
        IDocumentPersistentWriter documentPersistentWriter,
        IDocumentTemporaryWriter documentTemporaryWriter,
        HybridStoreManager hybridStoreManager,
        IAggregateSettings aggregateSettings)
    {
        _documentPersistentWriter = documentPersistentWriter;
        _documentTemporaryWriter = documentTemporaryWriter;
        _hybridStoreManager = hybridStoreManager;
        _aggregateSettings = aggregateSettings;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryWriter.SaveAsync(document, aggregateType);
            return;
        }

        if (document.DocumentType == DocumentType.AggregateSnapshot)
        {
        }

        if (document is IEvent)
        {
        }

        await _documentPersistentWriter.SaveAsync(document, aggregateType);
    }

    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, Type aggregateType) where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryWriter.SaveAndPublishEvents(events, aggregateType);
            return;
        }
        var enumerable = events.ToList();
        foreach (var ev in enumerable)
        {
            await AddToHybridIfPossible(ev, aggregateType);
        }
        await _documentPersistentWriter.SaveAndPublishEvents(enumerable, aggregateType);
    }

    private async Task AddToHybridIfPossible(IEvent ev, Type aggregateType)
    {
        if (!_aggregateSettings.CanUseHybrid(aggregateType))
        {
            return;
        }
        if (!_hybridStoreManager.HasPartition(ev.PartitionKey))
        {
            _hybridStoreManager.AddPartitionKey(ev.PartitionKey, string.Empty, false);
        }
        await _documentTemporaryWriter.SaveAsync(ev, aggregateType);
    }
}
