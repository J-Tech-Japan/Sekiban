using System.Text.Json;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
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
        var dbEvents = await dbFactory.DbActionAsync(async (dbContext) =>
            eventRetrievalInfo.GetAggregateContainerGroup() switch
            {
                AggregateContainerGroup.Default => await dbContext.GetEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)),
                AggregateContainerGroup.Dissolvable => await dbContext.GetDissolvableEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)),
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
        var query = DbSingleProjectionSnapshotQuery.ForTestExistence(aggregateId, aggregatePayloadType, projectionPayloadType, version, rootPartitionKey, payloadVersionIdentifier);

        return (await dbFactory.DbActionAsync(
            async dbContext =>
                await dbContext.GetSingleProjectionSnapshotsAsync(query)
        )).Length != 0;
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey, string payloadVersionIdentifier)
    {
        var query = DbSingleProjectionSnapshotQuery.ForGetLatest(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey, payloadVersionIdentifier);

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

    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(Type multiProjectionPayloadType, string payloadVersionIdentifier, string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
    {
        var query = DbMultiProjectionSnapshotQuery.ForGetLatest(multiProjectionPayloadType, payloadVersionIdentifier, rootPartitionKey);

        var dbSnapshot = (
            await dbFactory.DbActionAsync(
                async dbContext =>
                    await dbContext.GetMultiProjectionSnapshotsAsync(query)
            )
        )
            .FirstOrDefault();

        if (dbSnapshot is null)
        {
            return null;
        }

        return dbSnapshot.ToSnapshot();
    }

    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey, string rootPartitionKey)
    {
        var query = DbSingleProjectionSnapshotQuery.ForGetById(id, aggregateId, aggregatePayloadType, partitionKey, rootPartitionKey);

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
        var query = DbSingleProjectionSnapshotQuery.ForGetAll(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey);

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

            snapshots.Add(filled);
        }

        return snapshots;
    }
}
