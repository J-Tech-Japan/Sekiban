using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     Result of attempting to create a batch of tag rows in one partition.
/// </summary>
internal enum CosmosTagBatchOutcome
{
    /// <summary>All rows in the batch were created.</summary>
    Created,

    /// <summary>At least one row already existed, so the (all-or-nothing) batch created none of them.</summary>
    Conflict
}

/// <summary>
///     The storage operations the tag-write stage needs, narrowed to a seam.
///     The production implementation talks to a Cosmos <c>Container</c>; tests substitute an in-memory
///     implementation so the stage's idempotency and corruption-detection logic can be exercised
///     deterministically — including failing after a given number of batches — without a Cosmos endpoint.
/// </summary>
internal interface ICosmosTagRowStore
{
    /// <summary>
    ///     Creates every row of one partition in a single all-or-nothing batch.
    ///     Returns <see cref="CosmosTagBatchOutcome.Conflict" /> if any row already exists.
    /// </summary>
    Task<CosmosTagBatchOutcome> CreateBatchAsync(string partitionKey, IReadOnlyList<CosmosTag> rows);

    /// <summary>
    ///     Creates a single row. Returns false if a row already exists at that identity.
    /// </summary>
    Task<bool> TryCreateRowAsync(string partitionKey, CosmosTag row);

    /// <summary>
    ///     Reads a single row, or null when it does not exist.
    /// </summary>
    Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id);
}

/// <summary>
///     Deterministic fault-injection hook for the tag-write stage. Called once before each batch is sent,
///     with the zero-based index of that batch, so a test can fail the stage after exactly N batches
///     without relying on timing. Never registered in production.
/// </summary>
internal interface ICosmosTagWriteFaultInjector
{
    /// <summary>
    ///     Called before batch <paramref name="batchIndex" /> is written. Throw to fail the stage there.
    /// </summary>
    Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows);
}
