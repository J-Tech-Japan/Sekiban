using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter events by specific tags
/// </summary>
public record SpecificTagsFilter(List<ITag> RequiredTags) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        RequiredTags.All(required => tags.Any(t => t.Equals(required)));

    public IEnumerable<ITag> GetTagFilters() => RequiredTags;
    public IEnumerable<string>? GetEventTypeFilters() => null;
}