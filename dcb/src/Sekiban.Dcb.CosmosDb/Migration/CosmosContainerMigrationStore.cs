using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using System.Net;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     The production migration store — the only place in this provider that issues a tag-row delete.
///     Its delete is ETag-guarded: it names the exact version the plan was built against, so Cosmos refuses
///     it if the row has changed. There is no unguarded delete here to reach for.
/// </summary>
internal sealed class CosmosContainerMigrationStore : ICosmosTagMigrationStore
{
    private readonly Container _container;

    public CosmosContainerMigrationStore(Container container) => _container = container;

    public async Task<(IReadOnlyList<CosmosTag> Rows, bool Overflowed)> ReadRowsForEventAsync(
        string partitionKey,
        Guid eventId,
        int maxRows,
        CancellationToken cancellationToken)
    {
        // The same superset prefilter + client-side canonical Guid gate the repair uses: the string form an
        // event id happens to be stored in must never decide which rows are found.
        var query = CosmosRepairRowQuery.BuildCandidateQuery(partitionKey, eventId);

        var requestOptions = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(partitionKey),
            MaxItemCount = maxRows + 1
        };

        using var iterator = _container.GetItemQueryIterator<CosmosTag>(query, requestOptions: requestOptions);

        var rows = new List<CosmosTag>();
        while (iterator.HasMoreResults && rows.Count <= maxRows)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            rows.AddRange(response.Where(row => CosmosRepairRowQuery.IsRowForEvent(row, eventId)));
        }

        return rows.Count > maxRows
            ? (rows.Take(maxRows).ToList(), true)
            : (rows, false);
    }

    public async Task<bool> TryCreateRowAsync(
        string partitionKey,
        CosmosTag row,
        CancellationToken cancellationToken)
    {
        try
        {
            await _container
                .CreateItemAsync(row, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
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

    public async Task<CosmosDeleteOutcome> TryDeleteRowAsync(
        string partitionKey,
        string id,
        string? etag,
        CancellationToken cancellationToken)
    {
        // No ETag means we never saw the row's version, so we have no basis for removing it.
        if (string.IsNullOrEmpty(etag))
        {
            return CosmosDeleteOutcome.EtagMismatch;
        }

        try
        {
            await _container.DeleteItemAsync<CosmosTag>(
                id,
                new PartitionKey(partitionKey),
                new ItemRequestOptions { IfMatchEtag = etag },
                cancellationToken).ConfigureAwait(false);

            return CosmosDeleteOutcome.Deleted;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return CosmosDeleteOutcome.AlreadyGone;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // The row changed since the plan pinned it. Cosmos refused, and so do we.
            return CosmosDeleteOutcome.EtagMismatch;
        }
    }
}
