using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using System.Text;
using System.Text.Json;
namespace Sekiban.Infrastructure.Cosmos.Documents;

public class CosmosDocumentWriter : IDocumentPersistentWriter
{
    private readonly IBlobAccessor _blobAccessor;
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly EventPublisher _eventPublisher;
    public CosmosDocumentWriter(CosmosDbFactory cosmosDbFactory, EventPublisher eventPublisher, IBlobAccessor blobAccessor)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _eventPublisher = eventPublisher;
        _blobAccessor = blobAccessor;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        switch (document.DocumentType)
        {
            case DocumentType.Event:
                await _cosmosDbFactory.CosmosActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        await container.CreateItemAsync(document, CosmosPartitionGenerator.ForDocument(document));
                    });
                break;
            case DocumentType.AggregateSnapshot:

                var snapshot = document as SnapshotDocument;
                if (snapshot == null)
                {
                    return;
                }
                await SaveSingleSnapshotAsync(snapshot, aggregateType, ShouldUseBlob(snapshot));
                break;
            default:
                await _cosmosDbFactory.CosmosActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        await container.CreateItemAsync(document, CosmosPartitionGenerator.ForDocument(document));
                    });
                break;
        }
    }
    public async Task SaveSingleSnapshotAsync(SnapshotDocument document, Type aggregateType, bool useBlob)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (useBlob)
        {
            var blobSnapshot = document with { Snapshot = null };
            var snapshotValue = document.Snapshot;
            var json = JsonSerializer.Serialize(snapshotValue, new JsonSerializerOptions());
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await _blobAccessor.SetBlobWithGZipAsync(SekibanBlobContainer.SingleProjectionState, blobSnapshot.FilenameForSnapshot(), memoryStream);
            await _cosmosDbFactory.CosmosActionAsync(
                blobSnapshot.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.CreateItemAsync(blobSnapshot, CosmosPartitionGenerator.ForDocument(document));
                });

        } else
        {
            await _cosmosDbFactory.CosmosActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.CreateItemAsync(document, CosmosPartitionGenerator.ForDocument(document));
                });
        }
    }
    public bool ShouldUseBlob(SnapshotDocument document)
    {
        var serializer = new SekibanCosmosSerializer();
        var stream = serializer.ToStream(document);
        return stream.Length > 1024 * 1024 * 2;
    }

    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, Type aggregateType) where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var taskList = events.Select(ev => container.UpsertItemAsync<dynamic>(ev, CosmosPartitionGenerator.ForDocument(ev))).ToList();
                await Task.WhenAll(taskList);
            });
        foreach (var ev in events)
        {
            await _eventPublisher.PublishAsync(ev);
        }
    }
}
