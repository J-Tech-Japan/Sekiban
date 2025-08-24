using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter events by type
/// </summary>
public record EventTypeFilter(HashSet<string> EventTypes) : IEventFilter
{
    public bool ShouldInclude(Event evt) => EventTypes.Contains(evt.EventType);
}
