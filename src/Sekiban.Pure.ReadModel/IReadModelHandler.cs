using Sekiban.Pure.Events;
namespace Sekiban.Pure.ReadModel;

/// <summary>
/// Interface for read model handlers
/// </summary>
public interface IReadModelHandler
{
    /// <summary>
    /// Handle event
    /// </summary>
    Task HandleEventAsync(IEvent @event);
}
