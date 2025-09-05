using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of <see cref="ISekibanExecutor"/> for Dcb.
///     Uses <see cref="InMemoryObjectAccessor"/> and a provided <see cref="IEventStore"/> (e.g. test InMemoryEventStore)
///     to execute commands, queries and tag state retrieval without external infrastructure.
///     Intended for lightweight tests / prototyping; not thread-safe for high concurrency scenarios.
/// </summary>
public class InMemoryDcbExecutor : ISekibanExecutor
{
    private readonly GeneralSekibanExecutor _inner;
    private readonly InMemoryObjectAccessor _accessor;
    private readonly IEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;

    /// <summary>
    ///     Creates executor with provided in-memory event store implementation
    /// </summary>
    public InMemoryDcbExecutor(DcbDomainTypes domainTypes, IEventStore eventStore)
    {
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _accessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _inner = new GeneralSekibanExecutor(_eventStore, _accessor, _domainTypes, null);
    }

    /// <summary>
    ///     Creates executor with built-in lightweight in-memory event store (no external dependencies)
    /// </summary>
    public InMemoryDcbExecutor(DcbDomainTypes domainTypes) : this(domainTypes, new InternalInMemoryEventStore()) { }

    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        _inner.ExecuteAsync(command, handlerFunc, cancellationToken);

    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        _inner.ExecuteAsync(command, cancellationToken);

    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId) => _inner.GetTagStateAsync(tagStateId);

    public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
        _inner.QueryAsync(queryCommon);

    public Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull => _inner.QueryAsync(queryCommon);

    /// <summary>
    ///     Minimal internal in-memory event store (single process, test support)
    /// </summary>
    private sealed class InternalInMemoryEventStore : IEventStore
    {
        private readonly List<Event> _events = new();
        private readonly object _lock = new();

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
        {
            lock (_lock)
            {
                var events = _events.AsEnumerable();
                if (since != null)
                {
                    events = events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
                }
                events = events.OrderBy(e => e.SortableUniqueIdValue);
                return Task.FromResult(ResultBox.FromValue(events));
            }
        }

    public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
        {
            lock (_lock)
            {
                var tagString = tag.GetTag();
                var events = _events.Where(e => e.Tags.Contains(tagString));
                if (since != null)
                {
                    events = events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
                }
                events = events.OrderBy(e => e.SortableUniqueIdValue);
                return Task.FromResult(ResultBox.FromValue(events.AsEnumerable()));
            }
        }

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
        {
            lock (_lock)
            {
                var evt = _events.FirstOrDefault(e => e.Id == eventId);
                return evt != null
                    ? Task.FromResult(ResultBox.FromValue(evt))
                    : Task.FromResult(ResultBox.Error<Event>(new KeyNotFoundException($"Event {eventId} not found")));
            }
        }

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(IEnumerable<Event> events)
        {
            lock (_lock)
            {
                var list = events.ToList();
                _events.AddRange(list);
                var tagWrites = new List<TagWriteResult>();
                var uniqueTags = list.SelectMany(e => e.Tags).Distinct();
                foreach (var tagString in uniqueTags)
                {
                    var tagEventCount = _events.Count(e => e.Tags.Contains(tagString));
                    var lastEvent = _events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).LastOrDefault();
                    if (lastEvent != null)
                    {
                        tagWrites.Add(new TagWriteResult(tagString, tagEventCount, DateTimeOffset.UtcNow));
                    }
                }
                return Task.FromResult(ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>((list, tagWrites)));
            }
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
        {
            lock (_lock)
            {
                var tagString = tag.GetTag();
                var tagEvents = _events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).ToList();
                if (!tagEvents.Any()) return Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));
                var streams = tagEvents.Select(e => new TagStream(tagString, e.Id, e.SortableUniqueIdValue));
                return Task.FromResult(ResultBox.FromValue(streams));
            }
        }

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
        {
            lock (_lock)
            {
                var tagString = tag.GetTag();
                var tagEvents = _events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).ToList();
                var tagStateId = new TagStateId(tag, "InMemoryProjector");
                if (!tagEvents.Any()) return Task.FromResult(ResultBox.FromValue(TagState.GetEmpty(tagStateId)));
                var lastEvent = tagEvents.Last();
                var state = new TagState(new EmptyTagStatePayload(), tagEvents.Count, lastEvent.SortableUniqueIdValue, tagStateId.TagGroup, tagStateId.TagContent, tagStateId.TagProjectorName);
                return Task.FromResult(ResultBox.FromValue(state));
            }
        }

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
        {
            lock (_lock)
            {
                var tagString = tag.GetTag();
                var exists = _events.Any(e => e.Tags.Contains(tagString));
                return Task.FromResult(ResultBox.FromValue(exists));
            }
        }
    }
}
