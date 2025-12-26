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
        var eventPublisher = new InMemoryMultiProjectionEventPublisher(_accessor);
        _inner = new GeneralSekibanExecutor(_eventStore, _accessor, _domainTypes, eventPublisher);
    }

    /// <summary>
    ///     Creates executor with built-in lightweight in-memory event store (no external dependencies)
    /// </summary>
    public InMemoryDcbExecutor(DcbDomainTypes domainTypes) : this(domainTypes, new InternalInMemoryEventStore(domainTypes)) { }

    Task<ResultBox<ExecutionResult>> ICommandExecutor.ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(command, handlerFunc, cancellationToken);

    Task<ResultBox<ExecutionResult>> ICommandExecutor.ExecuteCommandAsync(
        Func<ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken) =>
        _inner.ExecuteCommandAsync(handlerFunc, cancellationToken);

    Task<ResultBox<ExecutionResult>> ICommandExecutor.ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(command, cancellationToken);

    Task<ResultBox<TagState>> ISekibanExecutor.GetTagStateAsync(TagStateId tagStateId) =>
        _inner.GetTagStateAsync(tagStateId);

    Task<ResultBox<TResult>> ISekibanExecutor.QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) =>
        _inner.QueryAsync(queryCommon);

    Task<ResultBox<ListQueryResult<TResult>>> ISekibanExecutor.QueryAsync<TResult>(
        IListQueryCommon<TResult> queryCommon) =>
        _inner.QueryAsync(queryCommon);

    /// <summary>
    ///     Minimal internal in-memory event store (single process, test support)
    ///     This implementation serializes/deserializes events to simulate real storage behavior
    ///     and validate that event types are properly registered.
    /// </summary>
    private sealed class InternalInMemoryEventStore : IEventStore
    {
        private readonly List<SerializedEventData> _events = new();
        private readonly object _lock = new();
        private readonly DcbDomainTypes _domainTypes;

        public InternalInMemoryEventStore(DcbDomainTypes domainTypes)
        {
            _domainTypes = domainTypes;
        }

        /// <summary>
        ///     Stored event data with serialized payload
        /// </summary>
        private sealed record SerializedEventData(
            Guid Id,
            string SortableUniqueIdValue,
            string EventType,
            string SerializedPayload,
            EventMetadata EventMetadata,
            IReadOnlyList<string> Tags);

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

                // Deserialize events
                var result = new List<Event>();
                foreach (var serializedEvent in events)
                {
                    var deserializeResult = DeserializeEvent(serializedEvent);
                    if (!deserializeResult.IsSuccess)
                    {
                        return Task.FromResult(ResultBox.Error<IEnumerable<Event>>(deserializeResult.GetException()));
                    }
                    result.Add(deserializeResult.GetValue());
                }

                return Task.FromResult(ResultBox.FromValue<IEnumerable<Event>>(result));
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

                // Deserialize events
                var result = new List<Event>();
                foreach (var serializedEvent in events)
                {
                    var deserializeResult = DeserializeEvent(serializedEvent);
                    if (!deserializeResult.IsSuccess)
                    {
                        return Task.FromResult(ResultBox.Error<IEnumerable<Event>>(deserializeResult.GetException()));
                    }
                    result.Add(deserializeResult.GetValue());
                }

                return Task.FromResult(ResultBox.FromValue<IEnumerable<Event>>(result));
            }
        }

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
        {
            lock (_lock)
            {
                var serializedEvent = _events.FirstOrDefault(e => e.Id == eventId);
                if (serializedEvent == null)
                {
                    return Task.FromResult(ResultBox.Error<Event>(new KeyNotFoundException($"Event {eventId} not found")));
                }

                var deserializeResult = DeserializeEvent(serializedEvent);
                if (!deserializeResult.IsSuccess)
                {
                    return Task.FromResult(ResultBox.Error<Event>(deserializeResult.GetException()));
                }

                return Task.FromResult(ResultBox.FromValue(deserializeResult.GetValue()));
            }
        }

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(IEnumerable<Event> events)
        {
            lock (_lock)
            {
                var list = events.ToList();
                var serializedEvents = new List<SerializedEventData>();
                var deserializedEvents = new List<Event>();

                // Serialize and immediately deserialize to validate event types
                foreach (var ev in list)
                {
                    // Serialize the event payload
                    var serializedPayload = SerializeEvent(ev);

                    // Store the serialized version
                    var serializedEvent = new SerializedEventData(
                        ev.Id,
                        ev.SortableUniqueIdValue,
                        ev.EventType,
                        serializedPayload,
                        ev.EventMetadata,
                        ev.Tags);

                    serializedEvents.Add(serializedEvent);

                    // Deserialize immediately to validate (like real storage would do when reading)
                    var deserializeResult = DeserializeEvent(serializedEvent);
                    if (!deserializeResult.IsSuccess)
                    {
                        return Task.FromResult(
                            ResultBox.Error<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>(
                                deserializeResult.GetException()));
                    }
                    deserializedEvents.Add(deserializeResult.GetValue());
                }

                _events.AddRange(serializedEvents);
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
                return Task.FromResult(ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>((deserializedEvents, tagWrites)));
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

        private string SerializeEvent(Event ev)
        {
            return _domainTypes.EventTypes.SerializeEventPayload(ev.Payload);
        }

        private ResultBox<Event> DeserializeEvent(SerializedEventData serializedEvent)
        {
            try
            {
                var payload = _domainTypes.EventTypes.DeserializeEventPayload(
                    serializedEvent.EventType,
                    serializedEvent.SerializedPayload);

                if (payload == null)
                {
                    return ResultBox.Error<Event>(
                        new InvalidOperationException(
                            $"Failed to deserialize event payload of type {serializedEvent.EventType}. Make sure the event type is registered."));
                }

                var ev = new Event(
                    payload,
                    serializedEvent.SortableUniqueIdValue,
                    serializedEvent.EventType,
                    serializedEvent.Id,
                    serializedEvent.EventMetadata,
                    serializedEvent.Tags.ToList());

                return ResultBox.FromValue(ev);
            }
            catch (Exception ex)
            {
                return ResultBox.Error<Event>(ex);
            }
        }
    }
}
