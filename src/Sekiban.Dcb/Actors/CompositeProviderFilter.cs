using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Composite filter combining multiple filters with AND logic
/// </summary>
public record CompositeProviderFilter(List<IEventProviderFilter> Filters) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        Filters.All(f => f.ShouldInclude(evt, tags));

    public IEnumerable<ITag>? GetTagFilters() =>
        Filters.SelectMany(f => f.GetTagFilters() ?? Enumerable.Empty<ITag>()).Distinct();

    public IEnumerable<string>? GetEventTypeFilters() =>
        Filters.SelectMany(f => f.GetEventTypeFilters() ?? Enumerable.Empty<string>()).Distinct();
}
