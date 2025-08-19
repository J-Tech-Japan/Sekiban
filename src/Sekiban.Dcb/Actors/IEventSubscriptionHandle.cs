namespace Sekiban.Dcb.Actors;

/// <summary>
///     Handle for managing an event subscription
/// </summary>
public interface IEventSubscriptionHandle : IDisposable
{
    /// <summary>
    ///     Unique ID for this subscription
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    ///     Whether the subscription is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    ///     Get the current position/checkpoint of the subscription
    /// </summary>
    string? CurrentPosition { get; }

    /// <summary>
    ///     Pause the subscription
    /// </summary>
    Task PauseAsync();

    /// <summary>
    ///     Resume the subscription
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    ///     Unsubscribe and clean up resources
    /// </summary>
    Task UnsubscribeAsync();
}