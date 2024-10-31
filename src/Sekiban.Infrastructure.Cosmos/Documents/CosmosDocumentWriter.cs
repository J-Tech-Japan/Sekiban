using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using System.Text;
using System.Text.Json;
namespace Sekiban.Infrastructure.Cosmos.Documents;

/// <summary>
///     Writes data on CosmosDB
/// </summary>
/// <param name="cosmosDbFactory"></param>
/// <param name="eventPublisher"></param>
/// <param name="blobAccessor"></param>
public class CosmosDocumentWriter(
    ICosmosDbFactory cosmosDbFactory,
    EventPublisher eventPublisher,
    IBlobAccessor blobAccessor) : IDocumentPersistentWriter
{

    public async Task SaveAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        switch (document.DocumentType)
        {
            case DocumentType.Event:
                await cosmosDbFactory.CosmosActionAsync(
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
                await SaveSingleSnapshotAsync(snapshot, writeDocumentStream, ShouldUseBlob(snapshot));
                break;
            default:
                await cosmosDbFactory.CosmosActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        await container.CreateItemAsync(document, CosmosPartitionGenerator.ForDocument(document));
                    });
                break;
        }
    }
    public async Task SaveSingleSnapshotAsync(
        SnapshotDocument document,
        IWriteDocumentStream writeDocumentStream,
        bool useBlob)
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        if (useBlob)
        {
            var blobSnapshot = document with { Snapshot = null };
            var snapshotValue = document.Snapshot;
            var json = JsonSerializer.Serialize(snapshotValue, new JsonSerializerOptions());
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.SingleProjectionState,
                blobSnapshot.FilenameForSnapshot(),
                memoryStream);
            await cosmosDbFactory.CosmosActionAsync(
                blobSnapshot.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    await container.CreateItemAsync(blobSnapshot, CosmosPartitionGenerator.ForDocument(document));
                });

        } else
        {
            await cosmosDbFactory.CosmosActionAsync(
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

    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        await cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var taskList = events
                    .Select(ev => container.UpsertItemAsync<dynamic>(ev, CosmosPartitionGenerator.ForDocument(ev)))
                    .ToList();
                await Task.WhenAll(taskList);
            });
        foreach (var ev in events)
        {
            await eventPublisher.PublishAsync(ev);
        }
    }
}
