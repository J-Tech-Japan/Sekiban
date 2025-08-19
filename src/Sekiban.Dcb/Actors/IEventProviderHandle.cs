namespace Sekiban.Dcb.Actors;

/// <summary>
///     Handle for managing an event provider stream
/// </summary>
public interface IEventProviderHandle : IDisposable
{
    /// <summary>
    ///     Unique ID for this provider
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    ///     Current state of the provider
    /// </summary>
    EventProviderState State { get; }

    /// <summary>
    ///     Pause event streaming
    /// </summary>
    Task PauseAsync();

    /// <summary>
    ///     Resume event streaming
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    ///     Stop the provider and clean up resources (including subscription)
    /// </summary>
    Task StopAsync();

    /// <summary>
    ///     Stop the subscription but keep processing remaining events
    /// </summary>
    Task StopSubscriptionAsync();

    /// <summary>
    ///     Wait for the provider to catch up to live events
    /// </summary>
    Task<bool> WaitForCatchUpAsync(TimeSpan timeout);

    /// <summary>
    ///     Wait for current batch to complete
    /// </summary>
    Task WaitForCurrentBatchAsync();

    /// <summary>
    ///     Get statistics about events processed
    /// </summary>
    EventProviderStatistics GetStatistics();

    /// <summary>
    ///     Check if currently processing a batch
    /// </summary>
    bool IsProcessingBatch { get; }
}