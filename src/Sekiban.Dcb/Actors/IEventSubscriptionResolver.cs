using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Actors;

/// <summary>
/// Resolves which event subscription to use for a specific multi-projection
/// Similar to IStreamDestinationResolver but for subscriptions
/// </summary>
public interface IEventSubscriptionResolver
{
    /// <summary>
    /// Creates an event subscription for the specified projector
    /// </summary>
    /// <param name="projectorName">Name of the multi-projector</param>
    /// <param name="subscriptionFactory">Factory function to create the subscription with specific parameters</param>
    /// <returns>The configured event subscription</returns>
    IEventSubscription Resolve(
        string projectorName,
        Func<string, string, Guid, IEventSubscription> subscriptionFactory);
}