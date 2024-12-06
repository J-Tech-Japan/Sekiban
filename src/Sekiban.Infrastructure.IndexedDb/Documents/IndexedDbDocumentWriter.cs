using System.Text;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentWriter(IndexedDbFactory dbFactory, IBlobAccessor blobAccessor) : IDocumentPersistentWriter, IEventPersistentWriter
{
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream) where TEvent : IEvent =>
        dbFactory.DbActionAsync(async (dbContext) =>
        {
            foreach (var ev in events)
            {
                switch (writeDocumentStream.GetAggregateContainerGroup())
                {
                    case AggregateContainerGroup.Default:
                        await dbContext.WriteEventAsync(DbEvent.FromEvent(ev));
                        break;

                    case AggregateContainerGroup.Dissolvable:
                        await dbContext.WriteDissolvableEventAsync(DbEvent.FromEvent(ev));
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

        });

    public async Task SaveItemAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream) where TDocument : IDocument
    {
        var aggregateContainerGroup = writeDocumentStream.GetAggregateContainerGroup();

        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                switch (document.DocumentType, aggregateContainerGroup, document)
                {
                    case (DocumentType.Event, AggregateContainerGroup.Default, IEvent ev):
                        await dbContext.WriteEventAsync(DbEvent.FromEvent(ev));
                        break;

                    case (DocumentType.Event, AggregateContainerGroup.Dissolvable, IEvent ev):
                        await dbContext.WriteDissolvableEventAsync(DbEvent.FromEvent(ev));
                        break;

                    case (DocumentType.Command, _, ICommandDocumentCommon command):
                        await dbContext.WriteCommandAsync(DbCommand.FromCommand(command, aggregateContainerGroup));
                        break;

                    case (DocumentType.AggregateSnapshot, _, SnapshotDocument snapshot):
                        await SaveSingleSnapshotAsync(snapshot, writeDocumentStream, ShouldUseBlob(snapshot));
                        break;

                    case (DocumentType.MultiProjectionSnapshot, _, MultiProjectionSnapshotDocument snapshot):
                        await dbContext.WriteMultiProjectionSnapshotAsync(DbMultiProjectionSnapshot.FromSnapshot(snapshot, aggregateContainerGroup));
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }
        );
    }

    public async Task SaveSingleSnapshotAsync(SnapshotDocument document, IWriteDocumentStream writeDocumentStream, bool useBlob)
    {
        if (useBlob)
        {
            var json = SekibanJsonHelper.Serialize(document.Snapshot) ?? throw new SekibanInvalidDocumentTypeException();
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            await blobAccessor.SetBlobWithGZipAsync(
                SekibanBlobContainer.SingleProjectionState,
                document.FilenameForSnapshot(),
                memoryStream
            );

            document = document with { Snapshot = null };
        }

        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                await dbContext.WriteSingleProjectionSnapshotAsync(DbSingleProjectionSnapshot.FromSnapshot(document, writeDocumentStream.GetAggregateContainerGroup()));
            }
        );
    }

    public bool ShouldUseBlob(SnapshotDocument document)
    {
        var stream = SekibanJsonHelper.Serialize(document);
        // TODO: adjust threshold
        return stream is not null && stream.Length > 1024 * 1024 * 1;
    }
}
