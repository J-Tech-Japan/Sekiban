namespace Sekiban.Dcb.Actors;

/// <summary>
///     Resolves which stream a projector should subscribe to
/// </summary>
public interface IEventSubscriptionResolver
{
    /// <summary>
    ///     Resolves which stream a projector should subscribe to
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <returns>The stream descriptor for the projector</returns>
    ISekibanStream Resolve(string projectorName);
}
