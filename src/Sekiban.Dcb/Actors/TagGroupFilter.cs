using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter events by tag group
/// </summary>
public record TagGroupFilter(string TagGroup) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        tags.Any(t => t.GetTagGroup() == TagGroup);

    public IEnumerable<ITag>? GetTagFilters() => null; // Will be filtered in ShouldInclude
    public IEnumerable<string>? GetEventTypeFilters() => null;
}
