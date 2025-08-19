using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter events by tags
/// </summary>
public record EventTagFilter(HashSet<string> RequiredTags) : IEventFilter
{
    public bool ShouldInclude(Event evt) => RequiredTags.All(tag => evt.Tags.Contains(tag));
}