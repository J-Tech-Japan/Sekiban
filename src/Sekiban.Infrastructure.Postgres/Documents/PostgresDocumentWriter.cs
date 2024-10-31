using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Postgres.Databases;
using System.Text;
namespace Sekiban.Infrastructure.Postgres.Documents;

public class PostgresDocumentWriter(
    PostgresDbFactory dbFactory,
    EventPublisher eventPublisher,
    IBlobAccessor blobAccessor) : IDocumentPersistentWriter
{
    public async Task SaveAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                switch (document.DocumentType, aggregateContainerGroup, document)
                {
                    case (DocumentType.Event, AggregateContainerGroup.Default, IEvent ev):
                        dbContext.Events.Add(DbEvent.FromEvent(ev));
                        break;
                    case (DocumentType.Event, AggregateContainerGroup.Dissolvable, IEvent evDissolvable):
                        dbContext.DissolvableEvents.Add(DbDissolvableEvent.FromEvent(evDissolvable));
                        break;
                    case (DocumentType.Command, _, ICommandDocumentCommon cmd):
                        dbContext.Commands.Add(DbCommandDocument.FromCommandDocument(cmd, aggregateContainerGroup));
                        break;
                    case (DocumentType.AggregateSnapshot, _, SnapshotDocument snapshot):
                        await SaveSingleSnapshotAsync(snapshot, writeDocumentStream, ShouldUseBlob(snapshot));
                        break;
                    case (DocumentType.MultiProjectionSnapshot, _, MultiProjectionSnapshotDocument multiSnapshot):
                        dbContext.MultiProjectionSnapshots.Add(
                            DbMultiProjectionDocument.FromMultiProjectionSnapshotDocument(
                                multiSnapshot,
                                aggregateContainerGroup));
                        break;
                }
                await dbContext.SaveChangesAsync();
            });
    }
    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent
    {
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                foreach (var ev in events)
                {
                    switch (writeDocumentStream.GetAggregateContainerGroup())
                    {
                        case AggregateContainerGroup.Default:
                            dbContext.Events.Add(DbEvent.FromEvent(ev));
                            break;
                        case AggregateContainerGroup.Dissolvable:
                            dbContext.DissolvableEvents.Add(DbDissolvableEvent.FromEvent(ev));
                            break;
                    }
                }
                await dbContext.SaveChangesAsync();

            });
        foreach (var ev in events)
        {
            await eventPublisher.PublishAsync(ev);
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
            var json = SekibanJsonHelper.Serialize(document.Snapshot) as string ??
                throw new SekibanInvalidDocumentTypeException();
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.SingleProjectionState,
                blobSnapshot.FilenameForSnapshot(),
                memoryStream);
            await dbFactory.DbActionAsync(
                async dbContext =>
                {
                    var newItem = DbSingleProjectionSnapshotDocument.FromDocument(
                        blobSnapshot,
                        aggregateContainerGroup);
                    dbContext.SingleProjectionSnapshots.Add(newItem);
                    await dbContext.SaveChangesAsync();
                });
        } else
        {
            await dbFactory.DbActionAsync(
                async dbContext =>
                {
                    var newItem = DbSingleProjectionSnapshotDocument.FromDocument(document, aggregateContainerGroup);
                    dbContext.SingleProjectionSnapshots.Add(newItem);
                    await dbContext.SaveChangesAsync();
                });
        }

    }
    public bool ShouldUseBlob(SnapshotDocument document)
    {
        var serializer = SekibanJsonHelper.Serialize(document) ?? string.Empty;
        return serializer.Length > 1024 * 1024 * 2;
    }
}
