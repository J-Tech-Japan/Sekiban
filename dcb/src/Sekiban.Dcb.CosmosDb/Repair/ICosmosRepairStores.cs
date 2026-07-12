using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     One page of the events scan.
/// </summary>
internal sealed record CosmosRepairEventPage(
    IReadOnlyList<CosmosEvent> Events,
    string? ContinuationToken,
    double RequestCharge);

/// <summary>
///     The bounded, resumable read over the events container the repair scan needs.
///     Behind a seam so the scan — paging, checkpointing, classification — can be driven without Cosmos.
/// </summary>
internal interface ICosmosRepairEventSource
{
    /// <summary>
    ///     Reads one page of events in (from, to] ordered by sortableUniqueId, resuming from
    ///     <paramref name="continuationToken" /> when given.
    /// </summary>
    Task<CosmosRepairEventPage> ReadEventPageAsync(
        string? fromSortableUniqueIdExclusive,
        string? toSortableUniqueIdInclusive,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);
}

/// <summary>
///     Rows found for one (event, tag) key, plus whether the per-key cap hid any.
/// </summary>
internal sealed record CosmosRepairRowLookup(
    IReadOnlyList<CosmosTag> Rows,
    bool Overflowed,
    double RequestCharge);

/// <summary>
///     The tag-container operations the repair needs: find every row indexing an (event, tag) — including
///     legacy rows sitting at a random id — and create a missing row. There is deliberately no delete or
///     replace here: G4 is non-destructive by construction, not by discipline.
/// </summary>
internal interface ICosmosTagRepairStore
{
    /// <summary>
    ///     Reads the rows in the tag's partition that carry <paramref name="eventId" />, up to
    ///     <paramref name="maxRows" />. Confined to <paramref name="partitionKey" />, so a different event's
    ///     rows are never even considered.
    /// </summary>
    Task<CosmosRepairRowLookup> ReadRowsForEventAsync(
        string partitionKey,
        Guid eventId,
        int maxRows,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a row. Returns false if one already exists at that identity — which is how a concurrent
    ///     writer landing between classification and repair is detected.
    /// </summary>
    Task<(bool Created, double RequestCharge)> TryCreateRowAsync(
        string partitionKey,
        CosmosTag row,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the row at an exact identity, or null.
    /// </summary>
    Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken);
}
