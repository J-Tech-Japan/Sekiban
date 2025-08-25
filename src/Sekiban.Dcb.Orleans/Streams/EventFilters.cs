using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Common event filters for Orleans event subscriptions
/// </summary>
public static class EventFilters
{
    /// <summary>
    ///     Filter events by event type
    /// </summary>
    public class EventTypeFilter : IEventFilter
    {
        private readonly HashSet<string> _eventTypes;

        public EventTypeFilter(params string[] eventTypes) => _eventTypes = new HashSet<string>(eventTypes);

        public EventTypeFilter(IEnumerable<string> eventTypes) => _eventTypes = new HashSet<string>(eventTypes);

        public bool ShouldInclude(Event evt) => _eventTypes.Contains(evt.EventType);
    }

    /// <summary>
    ///     Filter events by tag
    /// </summary>
    public class TagFilter : IEventFilter
    {
        private readonly bool _requireAll;
        private readonly HashSet<string> _tags;

        public TagFilter(bool requireAll = false, params string[] tags)
        {
            _tags = new HashSet<string>(tags);
            _requireAll = requireAll;
        }

        public TagFilter(IEnumerable<string> tags, bool requireAll = false)
        {
            _tags = new HashSet<string>(tags);
            _requireAll = requireAll;
        }

        public bool ShouldInclude(Event evt)
        {
            if (_tags.Count == 0)
                return true;

            if (evt.Tags == null || evt.Tags.Count == 0)
                return false;

            if (_requireAll)
            {
                // Event must have all specified tags
                return _tags.All(tag => evt.Tags.Contains(tag));
            }
            // Event must have at least one of the specified tags
            return _tags.Any(tag => evt.Tags.Contains(tag));
        }
    }

    /// <summary>
    ///     Filter events by tag group
    /// </summary>
    public class TagGroupFilter : IEventFilter
    {
        private readonly HashSet<string> _tagGroups;

        public TagGroupFilter(params string[] tagGroups) => _tagGroups = new HashSet<string>(tagGroups);

        public TagGroupFilter(IEnumerable<string> tagGroups) => _tagGroups = new HashSet<string>(tagGroups);

        public bool ShouldInclude(Event evt)
        {
            if (_tagGroups.Count == 0)
                return true;

            if (evt.Tags == null || evt.Tags.Count == 0)
                return false;

            // Check if any tag belongs to the specified groups
            return evt.Tags.Any(tag =>
            {
                var parts = tag.Split(':');
                if (parts.Length > 0)
                {
                    return _tagGroups.Contains(parts[0]);
                }
                return false;
            });
        }
    }

    /// <summary>
    ///     Composite filter that combines multiple filters with AND logic
    /// </summary>
    public class CompositeAndFilter : IEventFilter
    {
        private readonly IEventFilter[] _filters;

        public CompositeAndFilter(params IEventFilter[] filters) =>
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));

        public bool ShouldInclude(Event evt)
        {
            return _filters.All(filter => filter.ShouldInclude(evt));
        }
    }

    /// <summary>
    ///     Composite filter that combines multiple filters with OR logic
    /// </summary>
    public class CompositeOrFilter : IEventFilter
    {
        private readonly IEventFilter[] _filters;

        public CompositeOrFilter(params IEventFilter[] filters) =>
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));

        public bool ShouldInclude(Event evt)
        {
            return _filters.Any(filter => filter.ShouldInclude(evt));
        }
    }

    /// <summary>
    ///     Filter that negates another filter
    /// </summary>
    public class NotFilter : IEventFilter
    {
        private readonly IEventFilter _innerFilter;

        public NotFilter(IEventFilter innerFilter) =>
            _innerFilter = innerFilter ?? throw new ArgumentNullException(nameof(innerFilter));

        public bool ShouldInclude(Event evt) => !_innerFilter.ShouldInclude(evt);
    }

    /// <summary>
    ///     Filter events by a custom predicate
    /// </summary>
    public class PredicateFilter : IEventFilter
    {
        private readonly Func<Event, bool> _predicate;

        public PredicateFilter(Func<Event, bool> predicate) =>
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

        public bool ShouldInclude(Event evt) => _predicate(evt);
    }

    /// <summary>
    ///     Filter that includes all events (no filtering)
    /// </summary>
    public class AllEventsFilter : IEventFilter
    {
        public bool ShouldInclude(Event evt) => true;
    }

    /// <summary>
    ///     Filter that excludes all events
    /// </summary>
    public class NoEventsFilter : IEventFilter
    {
        public bool ShouldInclude(Event evt) => false;
    }
}
