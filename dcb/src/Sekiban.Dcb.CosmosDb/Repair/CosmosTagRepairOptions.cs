namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Bounds and mode for one repair run.
/// </summary>
public class CosmosTagRepairOptions
{
    /// <summary>
    ///     Scan events with a sortableUniqueId strictly greater than this. Null starts from the beginning.
    /// </summary>
    public string? FromSortableUniqueIdExclusive { get; set; }

    /// <summary>
    ///     Scan events with a sortableUniqueId less than or equal to this. Null scans to the end.
    ///     Pin this to a value at or below the last event you know to be settled: an event written while the
    ///     scan is running is repaired by the write path itself, not by this service.
    /// </summary>
    public string? ToSortableUniqueIdInclusive { get; set; }

    /// <summary>
    ///     Classify and report, but write nothing. Default: true — a repair run must be asked for explicitly.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    ///     Event documents to read per page of the cross-partition scan. Default: 100.
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    ///     Event documents to examine before the run stops and hands back a checkpoint. Keeps one run's RU
    ///     cost and duration bounded. Default: 10,000.
    /// </summary>
    public int MaxEventsToScan { get; set; } = 10_000;

    /// <summary>
    ///     (event, tag) keys classified concurrently. Each key costs one partition-confined query, so this is
    ///     the main RU-rate dial. Default: 4.
    /// </summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    ///     Rows the scan will examine for a single (event, tag) key before reporting
    ///     <see cref="CosmosTagRepairCategory.Overflow" /> instead of classifying. Bounds the blast radius of
    ///     a pathological partition. Default: 16.
    /// </summary>
    public int MaxRowsPerKey { get; set; } = 16;

    /// <summary>
    ///     Findings kept in the report. Counts are never capped; this only bounds the detail list.
    ///     Default: 1,000.
    /// </summary>
    public int MaxFindings { get; set; } = 1_000;

    /// <summary>
    ///     Attempts for a throttled (429) Cosmos call before the run gives up. The server's Retry-After is
    ///     honored in full. Default: 5.
    /// </summary>
    public int MaxThrottleRetries { get; set; } = 5;

    /// <summary>
    ///     Opaque checkpoint from a previous run's report. Resumes exactly where that run stopped.
    /// </summary>
    public string? Checkpoint { get; set; }
}
