namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Raised when a multi-event write failed partway through the parallel event-create phase.
///     Event documents have distributed partition keys and are created independently, so no transaction spans
///     them: the ones that were created are already durable and immediately visible to all-events readers,
///     while their siblings from the same call were never written. Nothing is deleted in response — deleting
///     a durable event that a multi-projection may already have read would contaminate it irreversibly — so
///     the failure is reported instead, naming both sets.
/// </summary>
public class CosmosPartialEventWriteException : Exception
{
    /// <summary>
    ///     Creates a partial event-write exception.
    /// </summary>
    public CosmosPartialEventWriteException()
    {
    }

    /// <summary>
    ///     Creates a partial event-write exception with a message.
    /// </summary>
    public CosmosPartialEventWriteException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a partial event-write exception with a message and inner exception.
    /// </summary>
    public CosmosPartialEventWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Creates a partial event-write exception naming the visible and the failed events.
    /// </summary>
    public CosmosPartialEventWriteException(
        IReadOnlyList<Guid> writtenEventIds,
        IReadOnlyList<Guid> failedEventIds,
        Exception innerException)
        : base(
            $"Event write partially failed: {writtenEventIds?.Count ?? 0} event(s) were created and are already " +
            $"visible to all-events readers, {failedEventIds?.Count ?? 0} event(s) from the same call were not " +
            "written. The created events were NOT deleted, because deleting events that consumers may already " +
            $"have observed would corrupt their state. Visible event ids: {string.Join(", ", writtenEventIds ?? Array.Empty<Guid>())}. " +
            $"Failed event ids: {string.Join(", ", failedEventIds ?? Array.Empty<Guid>())}.",
            innerException)
    {
        WrittenEventIds = writtenEventIds ?? Array.Empty<Guid>();
        FailedEventIds = failedEventIds ?? Array.Empty<Guid>();
    }

    /// <summary>
    ///     Events that were created and are already visible to all-events readers.
    /// </summary>
    public IReadOnlyList<Guid> WrittenEventIds { get; } = Array.Empty<Guid>();

    /// <summary>
    ///     Events from the same call that were never written.
    /// </summary>
    public IReadOnlyList<Guid> FailedEventIds { get; } = Array.Empty<Guid>();
}
