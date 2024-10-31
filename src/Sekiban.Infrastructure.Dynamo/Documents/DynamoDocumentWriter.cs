using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
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
public class DynamoDocumentWriter(DynamoDbFactory dbFactory, IBlobAccessor blobAccessor)
    : IDocumentPersistentWriter, IEventPersistentWriter
{


    public async Task SaveItemAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        switch (document.DocumentType)
        {
            case DocumentType.Event:
                await dbFactory.DynamoActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(document) ??
                            throw new SekibanInvalidDocumentTypeException();
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
                await SaveSingleSnapshotAsync(snapshot, writeDocumentStream, ShouldUseBlob(snapshot));
                break;
            default:
                await dbFactory.DynamoActionAsync(
                    document.DocumentType,
                    aggregateContainerGroup,
                    async container =>
                    {
                        var json = SekibanJsonHelper.Serialize(document) ??
                            throw new SekibanInvalidDocumentTypeException();
                        var newItem = Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException();
                        await container.PutItemAsync(newItem);
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
            await dbFactory.DynamoActionAsync(
                blobSnapshot.DocumentType,
                aggregateContainerGroup,
                async container =>
                {
                    var newJson = SekibanJsonHelper.Serialize(blobSnapshot) ??
                        throw new SekibanInvalidDocumentTypeException();
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
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent => dbFactory.DynamoActionAsync(
        DocumentType.Event,
        writeDocumentStream.GetAggregateContainerGroup(),
        async container =>
        {
            var batchWriter = container.CreateBatchWrite();
            foreach (var newItem in events
                .ToList()
                .Select(ev => SekibanJsonHelper.Serialize(ev) ?? throw new SekibanInvalidDocumentTypeException())
                .Select(json => Document.FromJson(json) ?? throw new SekibanInvalidDocumentTypeException()))
            {
                batchWriter.AddDocumentToPut(newItem);
            }
            await batchWriter.ExecuteAsync();
        });
}
