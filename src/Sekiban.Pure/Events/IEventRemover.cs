namespace Sekiban.Pure.Events;

/// <summary>
/// Interface for removing events from the event store
/// </summary>
public interface IEventRemover
{
    /// <summary>
    /// Removes all events from the event store
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task RemoveAllEvents();
}
