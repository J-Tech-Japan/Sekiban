using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using System.Text;
using System.Text.Json;
namespace Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;

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
                        await container.CreateItemAsync(document, new PartitionKey(document.PartitionKey));
                    });
                break;
            case DocumentType.AggregateSnapshot:

                var snapshot = document as SnapshotDocument;
                if (snapshot == null)
                {
                    return;
                }
                var serializer = new SekibanCosmosSerializer();
                var stream = serializer.ToStream(snapshot);
                if (stream.Length > 9500)
                {
                    var blobSnapshot = snapshot with { Snapshot = null };
                    var snapshotValue = snapshot.Snapshot;
                    var json = JsonSerializer.Serialize(snapshotValue, new JsonSerializerOptions());
                    var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                    await _blobAccessor.SetBlobWithGZipAsync(
                        SekibanBlobContainer.SingleProjectionState,
                        blobSnapshot.FilenameForSnapshot(),
                        memoryStream);
                    await _cosmosDbFactory.CosmosActionAsync(
                        blobSnapshot.DocumentType,
                        aggregateContainerGroup,
                        async container =>
                        {
                            await container.CreateItemAsync(blobSnapshot, new PartitionKey(blobSnapshot.PartitionKey));
                        });

                }
                else
                {
                    await _cosmosDbFactory.CosmosActionAsync(
                        document.DocumentType,
                        aggregateContainerGroup,
                        async container =>
                        {
                            await container.CreateItemAsync(document, new PartitionKey(document.PartitionKey));
                        });
                }
                break;
            default:
                await _cosmosDbFactory.CosmosActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        await container.CreateItemAsync(document, new PartitionKey(document.PartitionKey));
                    });
                break;
        }
    }

    public async Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType)
        where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container => { await container.UpsertItemAsync<dynamic>(ev, new PartitionKey(ev.PartitionKey)); });
        await _eventPublisher.PublishAsync(ev);
    }
}
