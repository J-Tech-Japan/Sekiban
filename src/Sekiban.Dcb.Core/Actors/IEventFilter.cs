using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter for events
/// </summary>
public interface IEventFilter
{
    /// <summary>
    ///     Check if an event should be included
    /// </summary>
    bool ShouldInclude(Event evt);
}
