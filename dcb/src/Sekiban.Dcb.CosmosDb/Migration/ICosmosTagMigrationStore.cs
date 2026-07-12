using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     How an atomic reduce went. All-or-nothing: either the whole key was reduced, or nothing about it
///     changed.
/// </summary>
internal enum CosmosReduceOutcome
{
    /// <summary>The canonical row is in place and every victim is gone. One transaction, one outcome.</summary>
    Committed,

    /// <summary>
    ///     The canonical row was not what the plan required — it had been created, removed, or rewritten
    ///     since. The transaction was refused, so NOT ONE victim was deleted.
    /// </summary>
    SurvivorRejected,

    /// <summary>
    ///     A victim had moved since the plan pinned it. The transaction was refused, so NOT ONE victim was
    ///     deleted — not even the ones that had not moved.
    /// </summary>
    VictimRejected,

    /// <summary>
    ///     The key needs more operations than one Cosmos transaction can carry. Refused before mutating:
    ///     splitting it across transactions would reintroduce exactly the gap this exists to close.
    /// </summary>
    TooManyOperations
}

/// <summary>
///     The storage operations the destructive migration needs — and the only place in this provider where a
///     tag row can be removed at all.
///     Note what this seam CANNOT express: a delete on its own. There is no <c>DeleteRowAsync</c> here to
///     reach for, and no <c>CreateRowAsync</c> either. The single mutating operation is
///     <see cref="ExecuteReduceAsync" />, which ensures the canonical survivor and removes the victims inside
///     one transaction, or does neither.
///     That is deliberate. Proving the survivor with a read and then deleting is a check followed by a use,
///     and the world can move in between: the survivor can be removed after the proof and before the deletes,
///     and the deletes would still commit — leaving the key indexed by nothing. Re-reading more often does
///     not fix that; it only narrows the window. So the proof and the deletes are not two operations here.
///     They are one.
///     This is also a separate seam from <c>ICosmosTagRepairStore</c>, which has no mutation of any kind
///     beyond creating a missing row. The repair service and the automatic sweep are wired to that one.
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

    /// <summary>Reads one row by identity, or null. Used to build the backup, never to authorize a delete.</summary>
    Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken);

    /// <summary>
    ///     Reduces one (event, tag) key to its canonical row, atomically.
    ///     Every row involved lives in the same partition — that is what <c>pk = {serviceId}|{tag}</c> buys —
    ///     so this is one Cosmos transaction:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 the canonical row is created (when the plan found none) or conditioned on its exact
    ///                 version (when it found one), so a survivor that has appeared, vanished, or changed
    ///                 since the plan aborts the whole transaction;
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 each victim is deleted conditioned on the exact version the plan pinned, so a victim
    ///                 that has moved aborts the whole transaction.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     There is no partial outcome. Either the key ends up canonical, or nothing about it changed and the
    ///     caller is told which condition failed.
    /// </summary>
    Task<CosmosReduceOutcome> ExecuteReduceAsync(
        string partitionKey,
        CosmosTag survivor,
        bool survivorExpectedToExist,
        string? survivorEtag,
        IReadOnlyList<CosmosTagRowRef> victims,
        CancellationToken cancellationToken);
}
