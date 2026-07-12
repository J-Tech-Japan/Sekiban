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

    /// <summary>Records a partially-failed multi-event write.</summary>
    internal static void RecordPartialEventWrite() => PartialEventWrites.Add(1);

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

/// <summary>Bounded set of terminal outcomes for a retried tag write, used as a metric label.</summary>
internal enum TagWriteRetryOutcome
{
    /// <summary>A retry succeeded.</summary>
    Recovered,

    /// <summary>Attempts or the deadline ran out.</summary>
    Exhausted
}
