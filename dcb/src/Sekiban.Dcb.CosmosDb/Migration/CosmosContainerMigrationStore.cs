using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using System.Net;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     The production migration store. Its one mutating operation is a single Cosmos transaction.
/// </summary>
internal sealed class CosmosContainerMigrationStore : ICosmosTagMigrationStore
{
    /// <summary>
    ///     Cosmos allows 100 operations in a transactional batch. One of ours is always the survivor, so a
    ///     key can carry at most 99 victims — and a key that needs more is refused rather than split, because
    ///     splitting is exactly the gap this design exists to close.
    /// </summary>
    public const int MaxBatchOperations = 100;

    /// <summary>Victims one transaction can carry.</summary>
    public const int MaxVictimsPerBatch = MaxBatchOperations - 1;

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

    public async Task<CosmosReduceOutcome> ExecuteReduceAsync(
        string partitionKey,
        CosmosTag survivor,
        bool survivorExpectedToExist,
        string? survivorEtag,
        IReadOnlyList<CosmosTagRowRef> victims,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentNullException.ThrowIfNull(victims);

        if (victims.Count > MaxVictimsPerBatch)
        {
            // Refused before mutating. Splitting across transactions would put a gap back between the
            // survivor's guarantee and the deletes, which is the whole thing this exists to avoid.
            return CosmosReduceOutcome.TooManyOperations;
        }

        var batch = _container.CreateTransactionalBatch(new PartitionKey(partitionKey));

        // Operation 0 — the survivor's guarantee, and the reason the deletes below are safe at all.
        if (survivorExpectedToExist)
        {
            if (string.IsNullOrEmpty(survivorEtag))
            {
                // We never saw the survivor's version, so we cannot condition on it — and an unconditioned
                // survivor is no guarantee.
                return CosmosReduceOutcome.SurvivorRejected;
            }

            // Replace-if-match: the transaction aborts unless the survivor is STILL at exactly the version
            // the plan pinned. The content written is the derived one, so this is a no-op on a healthy row
            // and a hard stop on one that has changed or vanished.
            batch.ReplaceItem(
                survivor.Id,
                survivor,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = survivorEtag });
        }
        else
        {
            // Create: the transaction aborts if a row has appeared at the canonical id since the plan.
            batch.CreateItem(survivor);
        }

        // Operations 1..n — each victim, conditioned on the exact version the plan pinned.
        foreach (var victim in victims)
        {
            if (string.IsNullOrEmpty(victim.ETag))
            {
                return CosmosReduceOutcome.VictimRejected;
            }

            batch.DeleteItem(
                victim.Id,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = victim.ETag });
        }

        using var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return CosmosReduceOutcome.Committed;
        }

        // Nothing committed. Which condition failed decides what the operator is told — and either way,
        // every victim is still exactly where it was.
        return ClassifyFailure(response);
    }

    /// <summary>
    ///     Reads the per-operation results to say WHY the transaction was refused. Operation 0 is always the
    ///     survivor; everything after it is a victim.
    /// </summary>
    private static CosmosReduceOutcome ClassifyFailure(TransactionalBatchResponse response)
    {
        if (response.Count > 0 && IsRejection(response[0].StatusCode))
        {
            return CosmosReduceOutcome.SurvivorRejected;
        }

        for (var index = 1; index < response.Count; index++)
        {
            if (IsRejection(response[index].StatusCode))
            {
                return CosmosReduceOutcome.VictimRejected;
            }
        }

        // Refused without naming a conditional culprit — a transient fault, or throttling exhausted. It is
        // still all-or-nothing and every victim is still present, so it is reported and re-planned rather
        // than retried blind.
        return CosmosReduceOutcome.VictimRejected;
    }

    private static bool IsRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.PreconditionFailed // the row moved
            or HttpStatusCode.Conflict                  // a row appeared where we expected none
            or HttpStatusCode.NotFound;                 // the row we conditioned on is gone
}
