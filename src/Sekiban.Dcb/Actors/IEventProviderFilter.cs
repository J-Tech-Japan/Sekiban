using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter for events
/// </summary>
public interface IEventProviderFilter
{
    /// <summary>
    ///     Check if an event should be included
    /// </summary>
    bool ShouldInclude(Event evt, List<ITag> tags);

    /// <summary>
    ///     Get tags to filter by (for EventStore queries)
    /// </summary>
    IEnumerable<ITag>? GetTagFilters();

    /// <summary>
    ///     Get event types to filter by
    /// </summary>
    IEnumerable<string>? GetEventTypeFilters();
}
