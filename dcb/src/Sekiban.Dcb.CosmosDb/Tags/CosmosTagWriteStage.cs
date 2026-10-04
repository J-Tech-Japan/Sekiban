using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     One (event, tag) pair to index, reduced to the fields a tag row derives from.
///     Both the typed and the pre-serialized write paths project onto this.
/// </summary>
internal readonly record struct CosmosTagRowSource(
    string Tag,
    Guid EventId,
    string SortableUniqueId,
    string EventType);

/// <summary>
///     Writes tag rows idempotently.
///     Rows are derived deterministically (see <see cref="CosmosTagIdentity" />), so re-executing this stage
///     for the same events is always safe: a row that already exists with the derived content counts as
///     success, and no row is ever overwritten. A row that exists with different content means the tags
///     container disagrees with the events container, which surfaces as
///     <see cref="CosmosTagIndexCorruptionException" /> rather than being papered over with an upsert.
///     This is the primitive that roll-forward retry and tag-index repair build on.
/// </summary>
internal static class CosmosTagWriteStage
{
    public static async Task<List<TagWriteResult>> WriteAsync(
        IReadOnlyList<CosmosTagRowSource> sources,
        ICosmosTagRowStore store,
        CosmosDbEventStoreOptions options,
        string serviceId,
        ICosmosTagWriteFaultInjector? faultInjector = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TagWriteResult>();
        if (sources.Count == 0)
        {
            return results;
        }

        var maxBatchOperations = options.MaxBatchOperations > 0 ? options.MaxBatchOperations : 1;
        var batchIndex = 0;

        // An (event, tag) pair maps to exactly one row, so a tag repeated within one event is one row, not two.
        var partitions = sources
            .Distinct()
            .GroupBy(source => CosmosTagIdentity.DerivePartitionKey(serviceId, source.Tag));

        foreach (var partition in partitions)
        {
            var partitionKey = partition.Key;
            var rows = partition
                .Select(source => CosmosTagIdentity.DeriveRow(
                    serviceId,
                    source.Tag,
                    source.EventId,
                    source.SortableUniqueId,
                    source.EventType))
                .ToList();

            for (var offset = 0; offset < rows.Count; offset += maxBatchOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = rows.Skip(offset).Take(maxBatchOperations).ToList();

                if (faultInjector != null)
                {
                    await faultInjector.OnBeforeBatchAsync(batchIndex, partitionKey, chunk).ConfigureAwait(false);
                }

                batchIndex++;

                var created = false;
                if (options.UseTransactionalBatchForTags)
                {
                    var outcome = await store.CreateBatchAsync(partitionKey, chunk, cancellationToken).ConfigureAwait(false);
                    created = outcome == CosmosTagBatchOutcome.Created;
                }

                // A conflicting batch creates nothing (it is all-or-nothing), so settle the chunk row by row.
                if (!created)
                {
                    foreach (var row in chunk)
                    {
                        await EnsureRowAsync(store, partitionKey, row, serviceId, cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (var row in chunk)
                {
                    results.Add(new TagWriteResult(row.Tag, 1, DateTimeOffset.UtcNow));
                }
            }
        }

        return results;
    }

    /// <summary>
    ///     Ensures a single row exists with exactly the derived content — creating it, accepting an identical
    ///     existing row, or reporting corruption. Never overwrites.
    /// </summary>
    private static async Task EnsureRowAsync(
        ICosmosTagRowStore store,
        string partitionKey,
        CosmosTag row,
        string serviceId,
        CancellationToken cancellationToken)
    {
        if (await store.TryCreateRowAsync(partitionKey, row, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var existing = await store.TryReadRowAsync(partitionKey, row.Id, cancellationToken).ConfigureAwait(false);
        if (existing == null)
        {
            // Created and then removed between our create and our read: one more create settles it.
            if (await store.TryCreateRowAsync(partitionKey, row, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            existing = await store.TryReadRowAsync(partitionKey, row.Id, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                throw new InvalidOperationException(
                    $"Tag row pk='{partitionKey}', id='{row.Id}' could neither be created nor read back. " +
                    "The tags container is being modified concurrently by another writer.");
            }
        }

        if (CosmosTagIdentity.ContentEquals(row, existing))
        {
            // Already indexed by an earlier attempt of this same write — idempotent success.
            return;
        }

        throw new CosmosTagIndexCorruptionException(
            serviceId,
            row.Tag,
            partitionKey,
            row.Id,
            CosmosTagIdentity.Describe(row),
            CosmosTagIdentity.Describe(existing));
    }
}
