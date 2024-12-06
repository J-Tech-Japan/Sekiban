using System.Text.Json;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentRepository(
    IndexedDbFactory dbFactory,
    RegisteredEventTypes registeredEventTypes,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor
) : IDocumentPersistentRepository, IEventPersistentRepository
{
    public async Task GetAllCommandStringsForAggregateIdAsync(Guid aggregateId, Type aggregatePayloadType, string? sinceSortableUniqueId, string rootPartitionKey, Action<IEnumerable<string>> resultAction) =>
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var dbCommands = await dbContext.GetCommandsAsync(DbCommandQuery.ForAggregateId(aggregateId, aggregatePayloadType, sinceSortableUniqueId, rootPartitionKey));

                var commands = dbCommands
                    .Select(x => x.ToCommand())
                    .Where(x => x is not null)
                    .Select(x => JsonSerializer.Serialize(x));

                resultAction(commands);
            });

    public async Task<ResultBox<bool>> GetEvents(EventRetrievalInfo eventRetrievalInfo, Action<IEnumerable<IEvent>> resultAction)
    {
        var dbEvents = await dbFactory.DbActionAsync((dbContext) =>
            eventRetrievalInfo.GetAggregateContainerGroup() switch
            {
                AggregateContainerGroup.Default => dbContext.GetEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)),
                AggregateContainerGroup.Dissolvable => dbContext.GetDissolvableEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)),
                _ => throw new NotImplementedException(),
            });

        var events = dbEvents
            .Select(x => x.ToEvent(registeredEventTypes))
            .OfType<IEvent>();

        resultAction(events);

        return true;
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, int version, string rootPartitionKey, string payloadVersionIdentifier)
    {
        var query = new DbSingleProjectionSnapshotQuery(aggregateId, aggregatePayloadType, projectionPayloadType, version, rootPartitionKey, payloadVersionIdentifier, true);

        return (await dbFactory.DbActionAsync(
            async dbContext =>
                await dbContext.GetSingleProjectionSnapshotsAsync(query)
        )).Length != 0;
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey, string payloadVersionIdentifier)
    {
        var query = new DbSingleProjectionSnapshotQuery(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey, payloadVersionIdentifier, true);

        var dbSnapshot = (
            await dbFactory.DbActionAsync(
                async dbContext =>
                    await dbContext.GetSingleProjectionSnapshotsAsync(query)
            )
        )
            .FirstOrDefault();

        if (dbSnapshot is null)
        {
            return null;
        }

        return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(dbSnapshot.ToSnapshot());
    }

    public Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(Type multiProjectionPayloadType, string payloadVersionIdentifier, string rootPartitionKey = "")
    {
        throw new NotImplementedException();
    }

    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey, string rootPartitionKey)
    {
        var query = new DbSingleProjectionSnapshotQuery(id, aggregateId, aggregatePayloadType, partitionKey, rootPartitionKey, true);

        var dbSnapshot = (
            await dbFactory.DbActionAsync(
                async dbContext =>
                    await dbContext.GetSingleProjectionSnapshotsAsync(query)
            )
        )
            .FirstOrDefault();

        if (dbSnapshot is null)
        {
            return null;
        }

        return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(dbSnapshot.ToSnapshot());
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        var query = new DbSingleProjectionSnapshotQuery(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey, false);

        var dbSnapshots = await dbFactory.DbActionAsync(
            async dbContext => await dbContext.GetSingleProjectionSnapshotsAsync(query));

        var snapshots = new List<SnapshotDocument>();

        foreach (var db in dbSnapshots)
        {
            var snapshot = db.ToSnapshot();
            var filled = await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);

            if (filled is null)
            {
                continue;
            }

            snapshots.Add(snapshot);
        }

        return snapshots;
    }
}
