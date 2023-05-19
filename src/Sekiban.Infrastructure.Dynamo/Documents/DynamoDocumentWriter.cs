using Amazon.DynamoDBv2.DocumentModel;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using System.Text;
using System.Text.Json;
namespace Sekiban.Infrastructure.Dynamo.Documents;

public class DynamoDocumentWriter : IDocumentPersistentWriter
{
    private readonly DynamoDbFactory _dbFactory;
    private readonly EventPublisher _eventPublisher;
    private readonly IBlobAccessor _blobAccessor;
    public DynamoDocumentWriter(DynamoDbFactory dbFactory, EventPublisher eventPublisher, IBlobAccessor blobAccessor)
    {
        _dbFactory = dbFactory;
        _eventPublisher = eventPublisher;
        _blobAccessor = blobAccessor;
    }


    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        switch (document.DocumentType)
        {
            case DocumentType.Event:
                await _dbFactory.DynamoActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(document) ?? throw new SekibanInvalidDocumentTypeException();
                        var newItem = Amazon.DynamoDBv2.DocumentModel.Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                        await container.PutItemAsync(newItem);
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
                await _dbFactory.DynamoActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(document) ?? throw new SekibanInvalidDocumentTypeException();
                        var newItem = Amazon.DynamoDBv2.DocumentModel.Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                        await container.PutItemAsync(newItem);
                    });
                break;
        }
    }
    public async Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType) where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
                await _dbFactory.DynamoActionAsync(
                    DocumentType.Event,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(ev) ?? throw new SekibanInvalidDocumentTypeException();
                        var newItem = Amazon.DynamoDBv2.DocumentModel.Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                        await container.PutItemAsync(newItem);
                    });
                await _eventPublisher.PublishAsync(ev);
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
            await _blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.SingleProjectionState,
                blobSnapshot.FilenameForSnapshot(),
                memoryStream);
            await _dbFactory.DynamoActionAsync(
                blobSnapshot.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    var newJson = SekibanJsonHelper.Serialize(blobSnapshot) ?? throw new SekibanInvalidDocumentTypeException();
                    var newItem = Amazon.DynamoDBv2.DocumentModel.Document.FromJson(newJson) ?? throw new SekibanInvalidDocumentTypeException();
                    await container.PutItemAsync(newItem);
                });
        }
        else
        {
            await _dbFactory.DynamoActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    var json = SekibanJsonHelper.Serialize(document) ?? throw new SekibanInvalidDocumentTypeException();
                    var newItem = Amazon.DynamoDBv2.DocumentModel.Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                    await container.PutItemAsync(newItem);
                });
        }
    }
    public bool ShouldUseBlob(SnapshotDocument document) => false;
}
