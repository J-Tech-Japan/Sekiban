using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentWriter(IndexedDbFactory dbFactory) : IDocumentPersistentWriter, IEventPersistentWriter
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
                        throw new NotImplementedException();

                    case (DocumentType.MultiProjectionSnapshot, _, MultiProjectionSnapshotDocument snapshot):
                        throw new NotImplementedException();

                    default:
                        throw new NotImplementedException();
                }
            }
        );
    }

    public Task SaveSingleSnapshotAsync(SnapshotDocument document, IWriteDocumentStream writeDocumentStream, bool useBlob)
    {
        throw new NotImplementedException();
    }

    public bool ShouldUseBlob(SnapshotDocument document)
    {
        var stream = SekibanJsonHelper.Serialize(document);
        // TODO: adjust threshold
        return stream is not null && stream.Length > 1024 * 1024 * 1;
    }
}