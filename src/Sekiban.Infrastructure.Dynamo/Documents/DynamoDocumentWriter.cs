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
using Document = Amazon.DynamoDBv2.DocumentModel.Document;
namespace Sekiban.Infrastructure.Dynamo.Documents;

/// <summary>
///     Write documents to DynamoDB
/// </summary>
/// <param name="dbFactory"></param>
/// <param name="eventPublisher"></param>
/// <param name="blobAccessor"></param>
public class DynamoDocumentWriter(DynamoDbFactory dbFactory, EventPublisher eventPublisher, IBlobAccessor blobAccessor) : IDocumentPersistentWriter
{


    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        switch (document.DocumentType)
        {
            case DocumentType.Event:
                await dbFactory.DynamoActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(document) ?? throw new SekibanInvalidDocumentTypeException();
                        var newItem = Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
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
                await dbFactory.DynamoActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(document) ?? throw new SekibanInvalidDocumentTypeException();
                        var newItem = Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                        await container.PutItemAsync(newItem);
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
            await blobAccessor.SetBlobWithGZipAsync(SekibanBlobContainer.SingleProjectionState, blobSnapshot.FilenameForSnapshot(), memoryStream);
            await dbFactory.DynamoActionAsync(
                blobSnapshot.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    var newJson = SekibanJsonHelper.Serialize(blobSnapshot) ?? throw new SekibanInvalidDocumentTypeException();
                    var newItem = Document.FromJson(newJson) ?? throw new SekibanInvalidDocumentTypeException();
                    await container.PutItemAsync(newItem);
                });
        } else
        {
            await dbFactory.DynamoActionAsync(
                document.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    var json = SekibanJsonHelper.Serialize(document) ?? throw new SekibanInvalidDocumentTypeException();
                    var newItem = Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                    await container.PutItemAsync(newItem);
                });
        }
    }
    public bool ShouldUseBlob(SnapshotDocument document)
    {
        var stream = SekibanJsonHelper.Serialize(document);
        return stream is not null && stream.Length > 1024 * 300;
    }
    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, Type aggregateType) where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        var evs = events.ToList();
        await dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var batchWriter = container.CreateBatchWrite();
                foreach (var ev in evs)
                {
                    var json = SekibanJsonHelper.Serialize(ev) ?? throw new SekibanInvalidDocumentTypeException();
                    var newItem = Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                    batchWriter.AddDocumentToPut(newItem);
                }
                await batchWriter.ExecuteAsync();
            });
        foreach (var ev in evs)
        {
            await eventPublisher.PublishAsync(ev);
        }
    }
}
