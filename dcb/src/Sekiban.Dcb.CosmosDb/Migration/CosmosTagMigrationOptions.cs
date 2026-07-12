using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     Where the rows a destructive run is about to delete are written before it deletes them.
///     A run without one is refused. Cosmos has no undo, so the backup IS the recovery path: the rows it
///     receives are complete documents, and re-creating them restores the index exactly as it was.
/// </summary>
public interface ICosmosTagMigrationBackupWriter
{
    /// <summary>
    ///     Persists every row the run is about to remove. Called once, before the first delete. If this
    ///     throws, nothing is deleted.
    /// </summary>
    Task WriteAsync(
        CosmosTagMigrationPlan plan,
        IReadOnlyList<CosmosTag> rowsToRemove,
        CancellationToken cancellationToken);
}

/// <summary>
///     Bounds for building a migration plan.
/// </summary>
public class CosmosTagMigrationPlanOptions
{
    /// <summary>Scan events with a sortableUniqueId strictly greater than this.</summary>
    public string? FromSortableUniqueIdExclusive { get; set; }

    /// <summary>Scan events with a sortableUniqueId less than or equal to this.</summary>
    public string? ToSortableUniqueIdInclusive { get; set; }

    /// <summary>Event documents read per page of the scan. Default: 100.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Event documents examined before the plan stops and hands back a checkpoint. Default: 10,000.</summary>
    public int MaxEventsToScan { get; set; } = 10_000;

    /// <summary>(event, tag) keys examined concurrently. Default: 4.</summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    ///     Rows examined for one key before the plan reports overflow and leaves the key alone.
    ///     Default: 16.
    /// </summary>
    public int MaxRowsPerKey { get; set; } = 16;

    /// <summary>Attempts for a throttled (429) call. The server's Retry-After is honored in full. Default: 5.</summary>
    public int MaxThrottleRetries { get; set; } = 5;

    /// <summary>Opaque checkpoint from a previous plan's artifact.</summary>
    public string? Checkpoint { get; set; }
}

/// <summary>
///     Authorization and safety gear for actually applying a plan.
///     Every field here is a lock the operator has to open on purpose. None of them has a permissive default,
///     and there is no constructor that fills them in for you.
/// </summary>
public class CosmosTagMigrationApplyOptions
{
    /// <summary>
    ///     Must be set to true, explicitly, or nothing is deleted. Default: false.
    ///     This is the whole authorization gate: a caller that has not said "yes, delete rows" gets a
    ///     <see cref="CosmosTagMigrationNotAuthorizedException" /> before a single document is touched.
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>
    ///     Where the rows about to be deleted are backed up. Required — a run without one is refused.
    /// </summary>
    public ICosmosTagMigrationBackupWriter? BackupWriter { get; set; }

    /// <summary>
    ///     Attempts for a delete whose ETag no longer matches, i.e. the row changed under us. The row is
    ///     re-read and re-validated; it is never force-deleted. Default: 2.
    /// </summary>
    public int MaxEtagRaceRetries { get; set; } = 2;

    /// <summary>Attempts for a throttled (429) call. Default: 5.</summary>
    public int MaxThrottleRetries { get; set; } = 5;

    /// <summary>Keys applied concurrently. Default: 4.</summary>
    public int MaxParallelism { get; set; } = 4;
}
