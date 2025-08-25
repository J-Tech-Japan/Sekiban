using Sekiban.Pure.Events;
namespace Sekiban.Pure.ReadModel;

/// <summary>
///     Interface for event context provider
/// </summary>
public interface IEventContextProvider
{
    /// <summary>
    ///     Get current event context
    /// </summary>
    EventContext GetCurrentEventContext();

    /// <summary>
    ///     Set current event context
    /// </summary>
    void SetCurrentEventContext(IEvent @event);

    /// <summary>
    ///     Clear current event context
    /// </summary>
    void ClearCurrentEventContext();
}
