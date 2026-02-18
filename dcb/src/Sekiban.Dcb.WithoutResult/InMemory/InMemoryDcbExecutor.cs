using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of <see cref="ISekibanExecutor"/> for Dcb (WithoutResult - exception-based).
///     Uses <see cref="InMemoryObjectAccessor"/> and a provided <see cref="IEventStore"/> (e.g. test InMemoryEventStore)
///     to execute commands, queries and tag state retrieval without external infrastructure.
///     Intended for lightweight tests / prototyping; not thread-safe for high concurrency scenarios.
///     Throws exceptions on errors instead of returning ResultBox.
/// </summary>
public class InMemoryDcbExecutor : ISekibanExecutor, ISerializedSekibanDcbExecutor
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
    public InMemoryDcbExecutor(DcbDomainTypes domainTypes)
        : this(domainTypes, new InternalInMemoryEventStore(domainTypes)) { }

    Task<ExecutionResult> ICommandExecutor.ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(command, handlerFunc, cancellationToken);

    Task<ExecutionResult> ICommandExecutor.ExecuteCommandAsync(
        Func<ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken) =>
        _inner.ExecuteCommandAsync(handlerFunc, cancellationToken);

    Task<ExecutionResult> ICommandExecutor.ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(command, cancellationToken);

    Task<TagState> ISekibanExecutor.GetTagStateAsync(TagStateId tagStateId) =>
        _inner.GetTagStateAsync(tagStateId);

    Task<TResult> ISekibanExecutor.QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) =>
        _inner.QueryAsync(queryCommon);

    Task<ListQueryResult<TResult>> ISekibanExecutor.QueryAsync<TResult>(
        IListQueryCommon<TResult> queryCommon) =>
        _inner.QueryAsync(queryCommon);

    Task<ResultBox<SerializableTagState>> ISerializedSekibanDcbExecutor.GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _inner.GetSerializableTagStateAsync(tagStateId);

    Task<ResultBox<SerializedCommitResult>> ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken) =>
        _inner.CommitSerializableEventsAsync(request, cancellationToken);

    /// <summary>
    ///     Minimal internal in-memory event store (single process, test support)
    ///     This implementation serializes/deserializes events to simulate real storage behavior
    ///     and validate that event types are properly registered.
    ///     Throws exceptions on errors instead of returning ResultBox.
    /// </summary>
    private sealed class InternalInMemoryEventStore : IEventStore
    {
        private sealed class ServiceState
        {
            public object Lock { get; } = new();
            public List<SerializedEventData> Events { get; } = new();
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ServiceState> _states = new(StringComparer.Ordinal);
        private readonly DcbDomainTypes _domainTypes;
        private readonly IServiceIdProvider _serviceIdProvider;

        public InternalInMemoryEventStore(DcbDomainTypes domainTypes, IServiceIdProvider? serviceIdProvider = null)
        {
            _domainTypes = domainTypes;
            _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        }

        private ServiceState GetState()
        {
            var serviceId = _serviceIdProvider.GetCurrentServiceId();
            return _states.GetOrAdd(serviceId, _ => new ServiceState());
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

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
        {
            var state = GetState();
            lock (state.Lock)
            {
                var events = state.Events.AsEnumerable();
                if (since != null)
                {
                    events = events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
                }
                events = events.OrderBy(e => e.SortableUniqueIdValue);
                if (maxCount.HasValue)
                {
                    events = events.Take(maxCount.Value);
                }

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
            var state = GetState();
            lock (state.Lock)
            {
                var tagString = tag.GetTag();
                var events = state.Events.Where(e => e.Tags.Contains(tagString));
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
            var state = GetState();
            lock (state.Lock)
            {
                var serializedEvent = state.Events.FirstOrDefault(e => e.Id == eventId);
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
            var state = GetState();
            lock (state.Lock)
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

                state.Events.AddRange(serializedEvents);
                var tagWrites = new List<TagWriteResult>();
                var uniqueTags = list.SelectMany(e => e.Tags).Distinct();
                foreach (var tagString in uniqueTags)
                {
                    var tagEventCount = state.Events.Count(e => e.Tags.Contains(tagString));
                    var lastEvent = state.Events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).LastOrDefault();
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
            var state = GetState();
            lock (state.Lock)
            {
                var tagString = tag.GetTag();
                var tagEvents = state.Events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).ToList();
                if (!tagEvents.Any()) return Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));
                var streams = tagEvents.Select(e => new TagStream(tagString, e.Id, e.SortableUniqueIdValue));
                return Task.FromResult(ResultBox.FromValue(streams));
            }
        }

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
        {
            var state = GetState();
            lock (state.Lock)
            {
                var tagString = tag.GetTag();
                var tagEvents = state.Events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).ToList();
                var tagStateId = new TagStateId(tag, "InMemoryProjector");
                if (!tagEvents.Any()) return Task.FromResult(ResultBox.FromValue(TagState.GetEmpty(tagStateId)));
                var lastEvent = tagEvents.Last();
                var tagState = new TagState(
                    new EmptyTagStatePayload(),
                    tagEvents.Count,
                    lastEvent.SortableUniqueIdValue,
                    tagStateId.TagGroup,
                    tagStateId.TagContent,
                    tagStateId.TagProjectorName);
                return Task.FromResult(ResultBox.FromValue(tagState));
            }
        }

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
        {
            var state = GetState();
            lock (state.Lock)
            {
                var tagString = tag.GetTag();
                var exists = state.Events.Any(e => e.Tags.Contains(tagString));
                return Task.FromResult(ResultBox.FromValue(exists));
            }
        }

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
        {
            var state = GetState();
            lock (state.Lock)
            {
                if (since == null)
                {
                    return Task.FromResult(ResultBox.FromValue((long)state.Events.Count));
                }

                var count = state.Events.Count(e => string.Compare(
                    e.SortableUniqueIdValue,
                    since.Value,
                    StringComparison.Ordinal) > 0);

                return Task.FromResult(ResultBox.FromValue((long)count));
            }
        }

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
        {
            var state = GetState();
            lock (state.Lock)
            {
                var allTags = state.Events
                    .SelectMany(e => e.Tags)
                    .Distinct()
                    .Where(t => string.IsNullOrEmpty(tagGroup) || t.StartsWith(tagGroup + ":"))
                    .ToList();

                var tagInfos = allTags.Select(tagString =>
                {
                    var group = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
                    var tagEvents = state.Events.Where(e => e.Tags.Contains(tagString)).ToList();

                    return new TagInfo(
                        tagString,
                        group,
                        tagEvents.Count,
                        tagEvents.Min(e => e.SortableUniqueIdValue),
                        tagEvents.Max(e => e.SortableUniqueIdValue),
                        null, // InMemory doesn't track timestamps
                        null);
                })
                .OrderBy(t => t.TagGroup)
                .ThenBy(t => t.Tag)
                .ToList();

                return Task.FromResult(ResultBox.FromValue(tagInfos.AsEnumerable()));
            }
        }

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events)
        {
            var state = GetState();
            lock (state.Lock)
            {
                var adapter = new EventStoreAdapter(state);
                var result = InMemorySerializableEventWriter.Write(events, adapter);
                return Task.FromResult(result);
            }
        }

        private sealed class EventStoreAdapter : InMemorySerializableEventWriter.IAddableEventStore
        {
            private readonly ServiceState _state;
            public EventStoreAdapter(ServiceState state) => _state = state;

            public void AddSerializableEvent(SerializableEvent ev)
            {
                var serializedEvent = new SerializedEventData(
                    ev.Id,
                    ev.SortableUniqueIdValue,
                    ev.EventPayloadName,
                    Convert.ToBase64String(ev.Payload),
                    ev.EventMetadata,
                    ev.Tags);
                _state.Events.Add(serializedEvent);
            }

            public int CountEventsWithTag(string tagString) =>
                _state.Events.Count(e => e.Tags.Contains(tagString));
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
