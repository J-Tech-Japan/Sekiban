using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.CosmosDb.Models;
using System.Net;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     The production events reader: a bounded, continuation-token-paged, cross-partition range scan over
///     the events container, scoped to one service id.
/// </summary>
internal sealed class CosmosContainerRepairEventSource : ICosmosRepairEventSource
{
    private readonly Container _container;
    private readonly string _serviceId;

    public CosmosContainerRepairEventSource(Container container, string serviceId)
    {
        _container = container;
        _serviceId = serviceId;
    }

    public async Task<CosmosRepairEventPage> ReadEventPageAsync(
        string? fromSortableUniqueIdExclusive,
        string? toSortableUniqueIdInclusive,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var sql = "SELECT * FROM c WHERE c.serviceId = @serviceId";
        if (fromSortableUniqueIdExclusive != null)
        {
            sql += " AND c.sortableUniqueId > @from";
        }

        if (toSortableUniqueIdInclusive != null)
        {
            sql += " AND c.sortableUniqueId <= @to";
        }

        sql += " ORDER BY c.sortableUniqueId";

        var query = new QueryDefinition(sql).WithParameter("@serviceId", _serviceId);
        if (fromSortableUniqueIdExclusive != null)
        {
            query = query.WithParameter("@from", fromSortableUniqueIdExclusive);
        }

        if (toSortableUniqueIdInclusive != null)
        {
            query = query.WithParameter("@to", toSortableUniqueIdInclusive);
        }

        using var iterator = _container.GetItemQueryIterator<CosmosEvent>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
        {
            return new CosmosRepairEventPage(Array.Empty<CosmosEvent>(), null, 0);
        }

        var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        return new CosmosRepairEventPage(
            response.ToList(),
            response.ContinuationToken,
            response.RequestCharge);
    }
}

/// <summary>
///     The production tag-row store for repair. Every lookup is confined to one tag partition, so a repair
///     can only ever see the rows of the (service, tag) it is examining.
///     Note there is no delete or replace here — the type cannot express a destructive operation.
/// </summary>
internal sealed class CosmosContainerRepairStore : ICosmosTagRepairStore
{
    private readonly Container _container;

    public CosmosContainerRepairStore(Container container) => _container = container;

    public async Task<CosmosRepairRowLookup> ReadRowsForEventAsync(
        string partitionKey,
        Guid eventId,
        int maxRows,
        CancellationToken cancellationToken)
    {
        // The query is a superset prefilter, confined to the tag's partition. It is NOT what decides which
        // rows index this event — a row stored with a different Guid rendering must not be able to slip past
        // the server and be mistaken for a missing row. See CosmosRepairRowQuery.
        var query = CosmosRepairRowQuery.BuildCandidateQuery(partitionKey, eventId);

        var requestOptions = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(partitionKey),
            MaxItemCount = maxRows + 1 // one over the cap, so overflow is detectable
        };

        using var iterator = _container.GetItemQueryIterator<CosmosTag>(query, requestOptions: requestOptions);

        var rows = new List<CosmosTag>();
        var requestCharge = 0.0;

        while (iterator.HasMoreResults && rows.Count <= maxRows)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            requestCharge += response.RequestCharge;

            // The correctness gate: canonical Guid comparison, client-side. Format and casing cannot change
            // the answer, and anything the prefilter over-returned is rejected here.
            rows.AddRange(response.Where(row => CosmosRepairRowQuery.IsRowForEvent(row, eventId)));
        }

        if (rows.Count > maxRows)
        {
            return new CosmosRepairRowLookup(rows.Take(maxRows).ToList(), true, requestCharge);
        }

        return new CosmosRepairRowLookup(rows, false, requestCharge);
    }

    public async Task<(bool Created, double RequestCharge)> TryCreateRowAsync(
        string partitionKey,
        CosmosTag row,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container
                .CreateItemAsync(row, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (true, response.RequestCharge);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return (false, ex.RequestCharge);
        }
    }

    public async Task<CosmosTag?> TryReadRowAsync(
        string partitionKey,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container
                .ReadItemAsync<CosmosTag>(id, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
