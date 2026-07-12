namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     How the repair scan classified one (event, tag) key.
/// </summary>
public enum CosmosTagRepairCategory
{
    /// <summary>The derived row already exists and matches the event exactly. Nothing to do.</summary>
    Present,

    /// <summary>No row indexes this (event, tag). This is the only category the repair writes.</summary>
    Missing,

    /// <summary>
    ///     A row written before the deterministic-id scheme indexes this (event, tag): it carries a random
    ///     row id and a wall-clock createdAt, but its semantic key and event-derived fields agree with the
    ///     event. The pair is already indexed, so no row is written — and the legacy row is never touched.
    /// </summary>
    LegacyPresent,

    /// <summary>
    ///     More than one legacy row indexes this (event, tag) — residue of a pre-SEK-G2 re-execution, which
    ///     created a fresh random id each time. Reported only: reducing them is destructive and belongs to
    ///     the separately authorized SEK-G4b, never to this service.
    /// </summary>
    Duplicate,

    /// <summary>
    ///     A row occupies this (event, tag) but disagrees with the event — a deterministic-id row whose
    ///     content differs, or a row whose immutable event-derived fields (sortableUniqueId, eventType,
    ///     tagGroup) do not match the source event. Reported, never overwritten.
    /// </summary>
    Corrupt,

    /// <summary>
    ///     More rows index this (event, tag) than the per-key cap allows the scan to examine, so it is
    ///     reported rather than classified. Raise <see cref="CosmosTagRepairOptions.MaxRowsPerKey" /> to look
    ///     deeper.
    /// </summary>
    Overflow
}

/// <summary>
///     What the repair scan found for one (event, tag) key.
/// </summary>
public record CosmosTagRepairFinding(
    CosmosTagRepairCategory Category,
    Guid EventId,
    string Tag,
    string SortableUniqueId,
    string? Detail = null);

/// <summary>
///     The auditable result of a repair run.
///     A dry run fills in exactly the same classification counts but leaves <see cref="Repaired" /> at zero
///     and writes nothing.
/// </summary>
public record CosmosTagRepairReport
{
    /// <summary>Event documents examined.</summary>
    public int EventsScanned { get; init; }

    /// <summary>(event, tag) keys examined — one event contributes one key per tag it carries.</summary>
    public int KeysScanned { get; init; }

    /// <summary>Keys whose derived row already existed and matched.</summary>
    public int Present { get; init; }

    /// <summary>Keys with no row indexing them.</summary>
    public int Missing { get; init; }

    /// <summary>Rows written. Always zero in a dry run.</summary>
    public int Repaired { get; init; }

    /// <summary>Keys already indexed by a pre-SEK-G2 row. Never written, never touched.</summary>
    public int LegacyPresent { get; init; }

    /// <summary>Keys indexed by more than one legacy row. Reported only — reduction is SEK-G4b.</summary>
    public int Duplicate { get; init; }

    /// <summary>Keys whose existing row disagrees with the event. Reported, never overwritten.</summary>
    public int Corrupt { get; init; }

    /// <summary>Keys with more rows than the per-key cap allowed the scan to examine.</summary>
    public int Overflow { get; init; }

    /// <summary>Request units the scan and repair consumed, as reported by Cosmos.</summary>
    public double RequestCharge { get; init; }

    /// <summary>True when the run mutated nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    ///     True when the scan stopped at its event budget or was cancelled with work left. Resume from
    ///     <see cref="Checkpoint" />.
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>
    ///     Opaque token to hand back to the next run to continue where this one stopped. Null once the range
    ///     is fully scanned.
    /// </summary>
    public string? Checkpoint { get; init; }

    /// <summary>
    ///     Findings for everything that was not simply <see cref="CosmosTagRepairCategory.Present" />, capped
    ///     at <see cref="CosmosTagRepairOptions.MaxFindings" />. The counts above are never capped.
    /// </summary>
    public IReadOnlyList<CosmosTagRepairFinding> Findings { get; init; } = Array.Empty<CosmosTagRepairFinding>();
}
