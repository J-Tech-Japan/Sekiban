using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.CosmosDb.Models;
using System.Net;
namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     The production <see cref="ICosmosTagRowStore" />: the tag-write stage's operations against a real
///     Cosmos tags container.
/// </summary>
internal sealed class CosmosContainerTagRowStore : ICosmosTagRowStore
{
    private readonly Container _container;

    public CosmosContainerTagRowStore(Container container) => _container = container;

    public async Task<CosmosTagBatchOutcome> CreateBatchAsync(
        string partitionKey, IReadOnlyList<CosmosTag> rows, CancellationToken cancellationToken = default)
    {
        var batch = _container.CreateTransactionalBatch(new PartitionKey(partitionKey));
        foreach (var row in rows)
        {
            batch.CreateItem(row);
        }

        using var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return CosmosTagBatchOutcome.Created;
        }

        // A batch is all-or-nothing: a row that already exists fails the whole batch and creates nothing.
        // That is the re-execution path, not an error — the caller settles the chunk row by row.
        if (ContainsConflict(response))
        {
            return CosmosTagBatchOutcome.Conflict;
        }

        throw new CosmosException(
            $"TransactionalBatch failed for tag partition '{partitionKey}' with status {response.StatusCode}",
            response.StatusCode,
            (int)response.StatusCode,
            response.ActivityId,
            response.RequestCharge);
    }

    public async Task<bool> TryCreateRowAsync(string partitionKey, CosmosTag row, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.CreateItemAsync(row, new PartitionKey(partitionKey), requestOptions: null, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container
                .ReadItemAsync<CosmosTag>(id, new PartitionKey(partitionKey), requestOptions: null, cancellationToken)
                .ConfigureAwait(false);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static bool ContainsConflict(TransactionalBatchResponse response)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return true;
        }

        // When one operation conflicts, the batch reports 424 Failed Dependency for the others, so the
        // conflict has to be found among the per-operation results.
        for (var i = 0; i < response.Count; i++)
        {
            if (response[i].StatusCode == HttpStatusCode.Conflict)
            {
                return true;
            }
        }

        return false;
    }
}
