using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sekiban.Dcb.Common;

/// <summary>
///     Core-owned metrics for sortable-unique-id waits. Labels are fixed vocabulary values; target IDs are never
///     emitted as metric dimensions.
/// </summary>
public static class SortableUniqueIdWaitTelemetry
{
    public const string MeterName = "Sekiban.Dcb.Core";
    public const string TimeoutCounterName = "sekiban.dcb.sortable_unique_id_wait.timeouts";
    public const string DurationHistogramName = "sekiban.dcb.sortable_unique_id_wait.duration";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> WaitTimeouts = Meter.CreateCounter<long>(
        TimeoutCounterName,
        unit: "{wait}",
        description: "Sortable-unique-id waits that reached their adaptive timeout.");

    private static readonly Histogram<double> WaitDuration = Meter.CreateHistogram<double>(
        DurationHistogramName,
        unit: "ms",
        description: "Elapsed time of sortable-unique-id waits.");

    internal static void RecordWait(
        SortableUniqueIdWaitSurface surface,
        SortableUniqueIdWaitMode mode,
        SortableUniqueIdWaitOutcome outcome,
        TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "surface", surface.ToLabel() },
            { "mode", mode.ToLabel() },
            { "outcome", outcome.ToLabel() }
        };

        WaitDuration.Record(Math.Max(0, elapsed.TotalMilliseconds), tags);
        if (outcome == SortableUniqueIdWaitOutcome.TimedOut)
        {
            WaitTimeouts.Add(1, tags);
        }
    }
}

/// <summary>Bounded production entrypoint labels for wait metrics.</summary>
internal enum SortableUniqueIdWaitSurface
{
    OrleansWithResultSingle,
    OrleansWithResultList,
    OrleansWithoutResultSingle,
    OrleansWithoutResultList,
    InMemorySingle,
    InMemoryList
}

internal enum SortableUniqueIdWaitMode
{
    Legacy,
    Strict
}

internal enum SortableUniqueIdWaitOutcome
{
    Arrived,
    TimedOut
}

internal static class SortableUniqueIdWaitMetricLabels
{
    internal static string ToLabel(this SortableUniqueIdWaitSurface surface) => surface switch
    {
        SortableUniqueIdWaitSurface.OrleansWithResultSingle => "orleans_with_result_single",
        SortableUniqueIdWaitSurface.OrleansWithResultList => "orleans_with_result_list",
        SortableUniqueIdWaitSurface.OrleansWithoutResultSingle => "orleans_without_result_single",
        SortableUniqueIdWaitSurface.OrleansWithoutResultList => "orleans_without_result_list",
        SortableUniqueIdWaitSurface.InMemorySingle => "in_memory_single",
        SortableUniqueIdWaitSurface.InMemoryList => "in_memory_list",
        _ => "unknown"
    };

    internal static string ToLabel(this SortableUniqueIdWaitMode mode) => mode switch
    {
        SortableUniqueIdWaitMode.Strict => "strict",
        _ => "legacy"
    };

    internal static string ToLabel(this SortableUniqueIdWaitOutcome outcome) => outcome switch
    {
        SortableUniqueIdWaitOutcome.TimedOut => "timeout",
        _ => "arrived"
    };
}
