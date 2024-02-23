using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Postgres.Databases;
namespace Sekiban.Infrastructure.Postgres.Documents;

public class PostgresDocumentRepository(
    PostgresDbFactory dbFactory,
    RegisteredEventTypes registeredEventTypes,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor) : IDocumentPersistentRepository
{
    public async Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var partitionKey = PartitionKeyGenerator.ForEvent(aggregateId, aggregatePayloadType, rootPartitionKey);

                switch (aggregateContainerGroup)
                {
                    case AggregateContainerGroup.Default:

                        var query = dbContext.Events.Where(m => m.PartitionKey == partitionKey) ?? throw new SekibanInvalidArgumentException();
                        query = string.IsNullOrEmpty(sinceSortableUniqueId)
                            ? query.OrderBy(m => m.SortableUniqueId)
                            : query.Where(m => string.Compare(m.SortableUniqueId, sinceSortableUniqueId) > 0)
                                .OrderByDescending(m => m.SortableUniqueId);
                        // take 1000 events each and run resultAction
                        var events = query.Take(1000).ToList();
                        while (events.Count > 0)
                        {
                            resultAction(events.Select(m => FromDbEvent(m, sinceSortableUniqueId)).Where(m => m is not null)!);
                            events = query.Take(1000).ToList();
                        }
                        break;

                    case AggregateContainerGroup.Dissolvable:
                        var queryDissolvable = dbContext.Events.Where(m => m.PartitionKey == partitionKey) ??
                            throw new SekibanInvalidArgumentException();
                        queryDissolvable = string.IsNullOrEmpty(sinceSortableUniqueId)
                            ? queryDissolvable.OrderBy(m => m.SortableUniqueId)
                            : queryDissolvable.Where(m => string.Compare(m.SortableUniqueId, sinceSortableUniqueId) > 0)
                                .OrderByDescending(m => m.SortableUniqueId);
                        // take 1000 events each and run resultAction
                        var dissolvableEvents = queryDissolvable.Take(1000).ToList();
                        while (dissolvableEvents.Count > 0)
                        {
                            resultAction(dissolvableEvents.Select(m => FromDbEvent(m, sinceSortableUniqueId)).Where(m => m is not null)!);
                            dissolvableEvents = queryDissolvable.Take(1000).ToList();
                        }
                        break;
                }
                await Task.CompletedTask;
            });
    }
    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        await GetAllEventsForAggregateIdAsync(
            aggregateId,
            aggregatePayloadType,
            partitionKey,
            sinceSortableUniqueId,
            rootPartitionKey,
            events =>
            {
                resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
            });
    }
    public Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction) =>
        throw new NotImplementedException();
    public async Task GetAllEventsForAggregateAsync(
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                switch (aggregateContainerGroup)
                {
                    case AggregateContainerGroup.Default:
                        var query = dbContext.Events.Where(m => m.AggregateType == aggregatePayloadType.Name);
                        if (rootPartitionKey != IMultiProjectionService.ProjectionAllRootPartitions)
                        {
                            query = query.Where(m => m.RootPartitionKey == rootPartitionKey);
                        }
                        query = string.IsNullOrEmpty(sinceSortableUniqueId)
                            ? query.OrderBy(m => m.SortableUniqueId)
                            : query.Where(m => string.Compare(m.SortableUniqueId, sinceSortableUniqueId) > 0)
                                .OrderByDescending(m => m.SortableUniqueId);
                        // take 1000 events each and run resultAction
                        var events = query.Take(1000).ToList();
                        while (events.Count > 0)
                        {
                            resultAction(events.Select(m => FromDbEvent(m, sinceSortableUniqueId)).Where(m => m is not null)!);
                            events = query.Take(1000).ToList();
                        }
                        break;

                    case AggregateContainerGroup.Dissolvable:


                        var queryDissolvable = dbContext.Events.Where(m => m.AggregateType == aggregatePayloadType.Name);
                        if (rootPartitionKey != IMultiProjectionService.ProjectionAllRootPartitions)
                        {
                            queryDissolvable = queryDissolvable.Where(m => m.RootPartitionKey == rootPartitionKey);
                        }
                        queryDissolvable = string.IsNullOrEmpty(sinceSortableUniqueId)
                            ? queryDissolvable.OrderBy(m => m.SortableUniqueId)
                            : queryDissolvable.Where(m => string.Compare(m.SortableUniqueId, sinceSortableUniqueId) > 0)
                                .OrderByDescending(m => m.SortableUniqueId);
                        // take 1000 events each and run resultAction
                        var dissolvableEvents = queryDissolvable.Take(1000).ToList();
                        while (dissolvableEvents.Count > 0)
                        {
                            resultAction(queryDissolvable.Select(m => FromDbEvent(m, sinceSortableUniqueId)).Where(m => m != null)!);
                            dissolvableEvents = queryDissolvable.Take(1000).ToList();
                        }

                        break;
                }
                await Task.CompletedTask;
            });
    }
    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionType);
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                switch (aggregateContainerGroup)
                {
                    case AggregateContainerGroup.Default:
                        var query = dbContext.Events.Where(m => targetAggregateNames.Count == 0 || targetAggregateNames.Contains(m.AggregateType));
                        if (sinceSortableUniqueId != null)
                        {
                            query = query.Where(m => string.Compare(m.SortableUniqueId, sinceSortableUniqueId) > 0);
                        }
                        if (!string.IsNullOrEmpty(rootPartitionKey))
                        {
                            query = query.Where(m => m.RootPartitionKey == rootPartitionKey);
                        }
                        query = query.OrderBy(m => m.SortableUniqueId);
                        // take 1000 events each and run resultAction
                        var events = query.Take(1000).ToList();
                        while (events.Count > 0)
                        {
                            resultAction(events.Select(m => FromDbEvent(m, sinceSortableUniqueId)).Where(m => m is not null)!);
                            events = query.Take(1000).ToList();
                        }
                        break;

                    case AggregateContainerGroup.Dissolvable:

                        var queryDissolvable = dbContext.DissolvableEvents.Where(
                            m => targetAggregateNames.Count == 0 || targetAggregateNames.Contains(m.AggregateType));
                        if (sinceSortableUniqueId != null)
                        {
                            queryDissolvable = queryDissolvable.Where(m => string.Compare(m.SortableUniqueId, sinceSortableUniqueId) > 0);
                        }
                        if (!string.IsNullOrEmpty(rootPartitionKey))
                        {
                            queryDissolvable = queryDissolvable.Where(m => m.RootPartitionKey == rootPartitionKey);
                        }
                        queryDissolvable = queryDissolvable.OrderBy(m => m.SortableUniqueId);
                        // take 1000 events each and run resultAction
                        var dissolvableEvents = queryDissolvable.Take(1000).ToList();
                        while (dissolvableEvents.Count > 0)
                        {
                            resultAction(dissolvableEvents.Select(m => FromDbEvent(m, sinceSortableUniqueId)).Where(m => m is not null)!);
                            dissolvableEvents = queryDissolvable.Take(1000).ToList();
                        }
                        break;
                }
                await Task.CompletedTask;
            });
    }
    public Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier) =>
        throw new NotImplementedException();
    public Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) =>
        throw new NotImplementedException();
    public async Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier) =>
        await Task.FromResult(false);
    // TODO: Need to implement

    public Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string partitionKey,
        string rootPartitionKey) =>
        throw new NotImplementedException();
    public Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey) =>
        throw new NotImplementedException();
    private IEvent? FromDbEvent(IDbEvent dbEvent, string? sinceSortableUniqueId)
    {
        if (string.IsNullOrEmpty(dbEvent.DocumentTypeName))
        {
            return null;
        }
        var type = registeredEventTypes.RegisteredTypes.FirstOrDefault(m => m.Name == dbEvent.DocumentTypeName);
        if (type is null)
        {
            return null;
        }
        if (SekibanJsonHelper.Deserialize(dbEvent.Payload, type) is not IEventPayloadCommon payload)
        {
            return null;
        }
        var callHistories = SekibanJsonHelper.Deserialize<List<CallHistory>>(dbEvent.CallHistories) ?? new List<CallHistory>();
        return Event.GenerateIEvent(
            dbEvent.Id,
            dbEvent.AggregateId,
            dbEvent.PartitionKey,
            dbEvent.DocumentType,
            dbEvent.DocumentTypeName,
            type,
            dbEvent.TimeStamp,
            dbEvent.SortableUniqueId,
            payload,
            dbEvent.AggregateType,
            dbEvent.Version,
            dbEvent.RootPartitionKey,
            callHistories);
    }
}
