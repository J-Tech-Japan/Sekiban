using System.Diagnostics.Metrics;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Metrics for the Cosmos write path.
///     Cardinality discipline: every label below is drawn from a small fixed set. Raw event ids and tag
///     strings are unbounded, so they are never used as metric labels — they appear only in the structured
///     logs emitted alongside these counters, and in the structured exceptions
///     (<see cref="Tags.CosmosTagWriteExhaustedException" />, <see cref="CosmosPartialEventWriteException" />,
///     <see cref="Tags.CosmosTagIndexCorruptionException" />).
/// </summary>
public static class CosmosDbTelemetry
{
    /// <summary>
    ///     Meter name to subscribe to, e.g. via OpenTelemetry's AddMeter.
    /// </summary>
    public const string MeterName = "Sekiban.Dcb.CosmosDb";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> TagWriteFailures = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_write.failures",
        description: "Tag-write attempts that failed. Label 'reason' is one of: transient, corruption.");

    private static readonly Counter<long> TagWriteRetries = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_write.retries",
        description: "Tag-write retry attempts performed under the roll-forward policy.");

    private static readonly Counter<long> TagWriteRetryOutcomes = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_write.retry_outcomes",
        description: "Terminal outcome of a retried tag write. Label 'outcome' is one of: recovered, exhausted.");

    private static readonly Counter<long> PartialEventWrites = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.event_write.partial_failures",
        description: "Multi-event writes that failed partway through the parallel event-create phase.");

    /// <summary>Records a failed tag-write attempt. <paramref name="reason" /> must be a bounded label.</summary>
    internal static void RecordTagWriteFailure(TagWriteFailureReason reason) =>
        TagWriteFailures.Add(1, new KeyValuePair<string, object?>("reason", ToLabel(reason)));

    /// <summary>Records that a tag write is about to be retried.</summary>
    internal static void RecordTagWriteRetry() => TagWriteRetries.Add(1);

    /// <summary>Records how a retried tag write finally ended.</summary>
    internal static void RecordTagWriteRetryOutcome(TagWriteRetryOutcome outcome) =>
        TagWriteRetryOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", ToLabel(outcome)));

    private static readonly Counter<long> SweepRuns = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_sweep.runs",
        description: "Tag-sweep runs. Label 'outcome' is one of: completed, budget_exhausted, failed.");

    private static readonly Counter<long> SweepRepairedRows = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_sweep.repaired_rows",
        description: "Missing tag rows backfilled by the sweep.");

    private static readonly Counter<long> SweepCorruptKeys = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_sweep.corrupt_keys",
        description: "Keys the sweep found corrupt. Reported only — the sweep never rewrites or removes a row.");

    private static readonly Counter<long> SweepOverflowKeys = Meter.CreateCounter<long>(
        "sekiban.dcb.cosmos.tag_sweep.overflow_keys",
        description: "Keys with more rows than the sweep's per-key cap allowed it to classify.");

    /// <summary>Records a partially-failed multi-event write.</summary>
    internal static void RecordPartialEventWrite() => PartialEventWrites.Add(1);

    /// <summary>Records how a sweep run ended. <paramref name="outcome" /> must be a bounded label.</summary>
    internal static void RecordSweepRun(SweepRunOutcome outcome) =>
        SweepRuns.Add(1, new KeyValuePair<string, object?>("outcome", ToLabel(outcome)));

    /// <summary>Records rows the sweep backfilled.</summary>
    internal static void RecordSweepRepairedRows(int repaired)
    {
        if (repaired > 0)
        {
            SweepRepairedRows.Add(repaired);
        }
    }

    /// <summary>Records what the sweep found but is not allowed to act on.</summary>
    internal static void RecordSweepAttention(int corrupt, int overflow)
    {
        if (corrupt > 0)
        {
            SweepCorruptKeys.Add(corrupt);
        }

        if (overflow > 0)
        {
            SweepOverflowKeys.Add(overflow);
        }
    }

    private static string ToLabel(SweepRunOutcome outcome) =>
        outcome switch
        {
            SweepRunOutcome.Completed => "completed",
            SweepRunOutcome.BudgetExhausted => "budget_exhausted",
            _ => "failed"
        };

    private static string ToLabel(TagWriteFailureReason reason) =>
        reason switch
        {
            TagWriteFailureReason.Corruption => "corruption",
            _ => "transient"
        };

    private static string ToLabel(TagWriteRetryOutcome outcome) =>
        outcome switch
        {
            TagWriteRetryOutcome.Recovered => "recovered",
            _ => "exhausted"
        };
}

/// <summary>Bounded set of tag-write failure reasons used as a metric label.</summary>
internal enum TagWriteFailureReason
{
    /// <summary>The attempt failed for a reason a retry could plausibly clear.</summary>
    Transient,

    /// <summary>An existing tag row disagreed with the event. Never retried.</summary>
    Corruption
}

/// <summary>Bounded set of terminal outcomes for a sweep run, used as a metric label.</summary>
internal enum SweepRunOutcome
{
    /// <summary>The run finished its budget of events.</summary>
    Completed,

    /// <summary>The run's wall-clock budget elapsed; it resumes from its checkpoint next turn.</summary>
    BudgetExhausted,

    /// <summary>The run threw. Logged; the host is unaffected and the next run retries.</summary>
    Failed
}

/// <summary>Bounded set of terminal outcomes for a retried tag write, used as a metric label.</summary>
internal enum TagWriteRetryOutcome
{
    /// <summary>A retry succeeded.</summary>
    Recovered,

    /// <summary>Attempts or the deadline ran out.</summary>
    Exhausted
}
