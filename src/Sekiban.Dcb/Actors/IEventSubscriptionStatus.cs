namespace Sekiban.Dcb.Actors;

/// <summary>
///     Status information for an event subscription
/// </summary>
public interface IEventSubscriptionStatus
{
    /// <summary>
    ///     Subscription identifier
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    ///     Whether the subscription is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    ///     Whether the subscription is paused
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    ///     Current position in the event stream
    /// </summary>
    string? CurrentPosition { get; }

    /// <summary>
    ///     When the subscription was started
    /// </summary>
    DateTime? StartedAt { get; }

    /// <summary>
    ///     When the subscription was last paused (if applicable)
    /// </summary>
    DateTime? PausedAt { get; }

    /// <summary>
    ///     Total number of events received
    /// </summary>
    long EventsReceived { get; }

    /// <summary>
    ///     Total number of events processed (after filtering)
    /// </summary>
    long EventsProcessed { get; }

    /// <summary>
    ///     Number of errors encountered
    /// </summary>
    long ErrorCount { get; }

    /// <summary>
    ///     Last error message (if any)
    /// </summary>
    string? LastError { get; }

    /// <summary>
    ///     When the last error occurred
    /// </summary>
    DateTime? LastErrorAt { get; }

    /// <summary>
    ///     When the last event was received
    /// </summary>
    DateTime? LastEventReceivedAt { get; }

    /// <summary>
    ///     When the last event was processed
    /// </summary>
    DateTime? LastEventProcessedAt { get; }

    /// <summary>
    ///     Average processing time per event (in milliseconds)
    /// </summary>
    double? AverageProcessingTimeMs { get; }
}
