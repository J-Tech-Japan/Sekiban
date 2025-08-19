namespace Sekiban.Dcb.Actors;

/// <summary>
///     Handle for managing an event feeder
/// </summary>
public interface IEventFeederHandle : IDisposable
{
    /// <summary>
    ///     Unique ID for this feeder
    /// </summary>
    string FeederId { get; }

    /// <summary>
    ///     Current state of the feeder
    /// </summary>
    EventFeederState State { get; }

    /// <summary>
    ///     Current position being processed
    /// </summary>
    string? CurrentPosition { get; }

    /// <summary>
    ///     Number of events processed
    /// </summary>
    long EventsProcessed { get; }

    /// <summary>
    ///     Whether the feeder is catching up on historical events
    /// </summary>
    bool IsCatchingUp { get; }

    /// <summary>
    ///     Pause the feeder
    /// </summary>
    Task PauseAsync();

    /// <summary>
    ///     Resume the feeder
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    ///     Stop the feeder and clean up resources
    /// </summary>
    Task StopAsync();

    /// <summary>
    ///     Wait for the feeder to catch up to real-time
    /// </summary>
    /// <param name="timeout">Maximum time to wait</param>
    /// <returns>True if caught up, false if timeout</returns>
    Task<bool> WaitForCatchUpAsync(TimeSpan timeout);
}