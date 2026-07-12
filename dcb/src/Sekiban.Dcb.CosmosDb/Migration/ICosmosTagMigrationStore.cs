using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>How an ETag-guarded delete went.</summary>
internal enum CosmosDeleteOutcome
{
    /// <summary>The row was deleted at the version the plan pinned.</summary>
    Deleted,

    /// <summary>The row had already gone. Nothing to do, and nothing wrong.</summary>
    AlreadyGone,

    /// <summary>
    ///     The row changed since the plan pinned it. The delete did NOT happen — the caller re-reads and
    ///     re-validates, or reports. It is never forced.
    /// </summary>
    EtagMismatch
}

/// <summary>
///     The storage operations the destructive migration needs — and the only place in this provider where a
///     tag-row delete can be expressed at all.
///     This is deliberately a separate seam from <c>ICosmosTagRepairStore</c>, which has no delete and never
///     will. The repair service and the automatic sweep are wired to that one; nothing reaches this one
///     except the migration service, which an operator has to invoke on purpose. The separation is what makes
///     "the sweep cannot delete a row" a fact about the types rather than a promise in a comment.
/// </summary>
internal interface ICosmosTagMigrationStore
{
    /// <summary>
    ///     Reads the rows in a tag partition that index an event, up to <paramref name="maxRows" />, with
    ///     their ETags. Confined to the partition; the event is matched by canonical Guid value, never by the
    ///     string form its id happens to be stored in.
    /// </summary>
    Task<(IReadOnlyList<CosmosTag> Rows, bool Overflowed)> ReadRowsForEventAsync(
        string partitionKey,
        Guid eventId,
        int maxRows,
        CancellationToken cancellationToken);

    /// <summary>Creates the canonical row. False if one already exists at that identity.</summary>
    Task<bool> TryCreateRowAsync(string partitionKey, CosmosTag row, CancellationToken cancellationToken);

    /// <summary>Reads one row by identity, or null.</summary>
    Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes a row, but only if it is still at <paramref name="etag" /> — the version the plan was
    ///     built against. A row that has changed since is reported back as
    ///     <see cref="CosmosDeleteOutcome.EtagMismatch" /> and left alone.
    /// </summary>
    Task<CosmosDeleteOutcome> TryDeleteRowAsync(
        string partitionKey,
        string id,
        string? etag,
        CancellationToken cancellationToken);
}
