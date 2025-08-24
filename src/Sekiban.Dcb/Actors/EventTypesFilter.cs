using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter events by event types
/// </summary>
public record EventTypesFilter(HashSet<string> EventTypes) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        EventTypes.Contains(evt.EventType);

    public IEnumerable<ITag>? GetTagFilters() => null;
    public IEnumerable<string> GetEventTypeFilters() => EventTypes;
}
