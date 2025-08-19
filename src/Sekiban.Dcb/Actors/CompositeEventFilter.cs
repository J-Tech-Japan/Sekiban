using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Composite filter that combines multiple filters with AND logic
/// </summary>
public record CompositeEventFilter(List<IEventFilter> Filters) : IEventFilter
{
    public bool ShouldInclude(Event evt) => Filters.All(f => f.ShouldInclude(evt));
}