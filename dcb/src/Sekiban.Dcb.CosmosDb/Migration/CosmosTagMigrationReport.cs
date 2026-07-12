namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     What happened to one (event, tag) key. Every destructive act the migration performs produces exactly
///     one of these, so the report is a complete record — not a summary of one.
/// </summary>
public record CosmosTagMigrationAuditEntry
{
    /// <summary>Event whose rows were reduced.</summary>
    public Guid EventId { get; init; }

    /// <summary>Tag whose partition held them.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Partition key of the rows.</summary>
    public string PartitionKey { get; init; } = string.Empty;

    /// <summary>Document id of the row that survived.</summary>
    public string SurvivorId { get; init; } = string.Empty;

    /// <summary>Whether the survivor had to be created, because only legacy rows indexed the key.</summary>
    public bool SurvivorCreated { get; init; }

    /// <summary>Document ids actually deleted.</summary>
    public IReadOnlyList<string> RemovedIds { get; init; } = Array.Empty<string>();

    /// <summary>How the key ended up.</summary>
    public CosmosTagMigrationOutcome Outcome { get; init; }

    /// <summary>Why, when the outcome is not <see cref="CosmosTagMigrationOutcome.Reduced" />.</summary>
    public string? Detail { get; init; }
}

/// <summary>How a key ended up in a destructive run.</summary>
public enum CosmosTagMigrationOutcome
{
    /// <summary>The key now has exactly one canonical row; the rest are gone.</summary>
    Reduced,

    /// <summary>
    ///     A removable row's CONTENT changed between the plan and the delete — not just its version. It is
    ///     no longer the row the operator reviewed, so it was left exactly as it is. Nothing was forced. A
    ///     fresh plan will pick the key up.
    /// </summary>
    LostRaceContentChanged,

    /// <summary>
    ///     The canonical row is not what it must be: it exists but does not match the content derived from
    ///     the event, or it could not be created and read back. Deleting the legacy rows would leave the key
    ///     wrongly indexed — or not indexed at all — so NOTHING was deleted.
    /// </summary>
    StaleSurvivor,

    /// <summary>
    ///     A removable row had already changed before the run even began, so the plan's authority over the
    ///     key had lapsed before the first delete. Left alone.
    /// </summary>
    Stale,

    /// <summary>A row disagreed with its event, or the cap hid rows. Never deleted, never rewritten.</summary>
    Skipped
}

/// <summary>
///     The result of a destructive run: an audit entry per key, and the counts that summarize them.
/// </summary>
public record CosmosTagMigrationReport
{
    /// <summary>Keys the plan proposed to reduce.</summary>
    public int KeysPlanned { get; init; }

    /// <summary>Keys reduced to a single canonical row.</summary>
    public int Reduced { get; init; }

    /// <summary>Rows deleted.</summary>
    public int RowsRemoved { get; init; }

    /// <summary>Canonical rows created because only legacy rows indexed the key.</summary>
    public int SurvivorsCreated { get; init; }

    /// <summary>Keys where a removable row's content changed mid-run. Nothing forced.</summary>
    public int LostRaces { get; init; }

    /// <summary>
    ///     Keys whose canonical row was missing-and-unrecreatable or did not match the event. Nothing was
    ///     deleted for these — deleting would have left the key wrongly indexed or unindexed.
    /// </summary>
    public int StaleSurvivors { get; init; }

    /// <summary>Keys the plan no longer describes.</summary>
    public int Stale { get; init; }

    /// <summary>Keys deliberately left alone (corrupt or over the cap).</summary>
    public int Skipped { get; init; }

    /// <summary>The complete audit trail — one entry per key the run touched or declined to touch.</summary>
    public IReadOnlyList<CosmosTagMigrationAuditEntry> Audit { get; init; } =
        Array.Empty<CosmosTagMigrationAuditEntry>();
}
