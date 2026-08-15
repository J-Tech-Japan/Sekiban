namespace Sekiban.Dcb.Queries;

/// <summary>
///     Interface for queries that need to wait for a specific sortable unique ID to be processed
/// </summary>
public interface IWaitForSortableUniqueId
{
    /// <summary>
    ///     The sortable unique ID to wait for before executing the query
    /// </summary>
    string? WaitForSortableUniqueId { get; }
}

/// <summary>
///     Opts a query into fail-closed sortable-unique-id freshness. A query that implements this marker and whose
///     requested position is not observed before the adaptive wait expires fails with a
///     <see cref="SortableUniqueIdWaitTimeoutException" /> before its query is serialized or executed.
/// </summary>
/// <remarks>
///     This interface deliberately has no members. The inherited target is the complete contract; type membership is
///     the opt-in so adding strictness never changes the serialized query shape.
/// </remarks>
public interface IStrictWaitForSortableUniqueId : IWaitForSortableUniqueId;

/// <summary>
///     Typed failure raised when a strict sortable-unique-id wait cannot observe its target before the adaptive
///     timeout. It contains wait diagnostics only and never carries query content.
/// </summary>
public sealed class SortableUniqueIdWaitTimeoutException : TimeoutException
{
    public SortableUniqueIdWaitTimeoutException(
        string targetSortableUniqueId,
        TimeSpan timeout,
        TimeSpan elapsed,
        string? lastObservedSortableUniqueId,
        Exception? innerException = null)
        : base(
            $"The projection did not observe sortable unique ID '{targetSortableUniqueId}' within {timeout}. "
            + $"Elapsed: {elapsed}; last observed: {lastObservedSortableUniqueId ?? "<none>"}.",
            innerException)
    {
        TargetSortableUniqueId = targetSortableUniqueId ?? throw new ArgumentNullException(nameof(targetSortableUniqueId));
        Timeout = timeout;
        Elapsed = elapsed;
        LastObservedSortableUniqueId = lastObservedSortableUniqueId;
    }

    /// <summary>The sortable unique ID the strict query requested.</summary>
    public string TargetSortableUniqueId { get; }

    /// <summary>Alias for callers that use the shorter diagnostic name.</summary>
    public string Target => TargetSortableUniqueId;

    /// <summary>The adaptive timeout selected for this target.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>The monotonic elapsed time measured by the wait policy.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>The projection's unsafe/current position at timeout, or <see langword="null" /> when unavailable.</summary>
    public string? LastObservedSortableUniqueId { get; }

    /// <summary>Alias for callers that use the shorter diagnostic name.</summary>
    public string? LastObserved => LastObservedSortableUniqueId;
}
