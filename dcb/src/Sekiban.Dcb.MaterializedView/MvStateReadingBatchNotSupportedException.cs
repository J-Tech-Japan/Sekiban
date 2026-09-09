namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     A materialized-view projector attempted state reading while a verified execution batch contained more than
///     one event. Querying mutable state for a whole batch cannot be authorized safely because the read result would
///     be reused across event preparation; callers must use a single-event batch or a query-free projector.
/// </summary>
public sealed class MvStateReadingBatchNotSupportedException : NotSupportedException
{
    public const string CatchUpErrorCode = "state-reading-batch-not-supported";
    public const string CatchUpErrorMessage =
        "State-reading materialized-view projectors are not supported for multi-event execution batches.";

    public MvStateReadingBatchNotSupportedException(int eventCount)
        : base($"{CatchUpErrorMessage} Observed event count: {eventCount}.")
    {
        if (eventCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCount), eventCount, "The guarded batch must contain at least two events.");
        }

        EventCount = eventCount;
    }

    public int EventCount { get; }
}
