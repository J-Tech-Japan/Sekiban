using System.Collections.Concurrent;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     Minimal in-memory event store (single process, test support) that the obsolete parameterless-store
///     <c>InMemoryDcbExecutor</c> constructors of both facades create. Events are kept in their serialized form, so
///     payloads round-trip as bytes the way a real store returns them. Payloads and tag lists are copied on write and
///     on every read, so changing an event a caller wrote or read never changes the stored history.
/// </summary>
internal sealed class InMemoryExecutorEventStore : IEventStore, IStreamingTaggedSerializableEventStore,
    ITaggedStreamCapabilityProvider
{
    private sealed class ServiceState
    {
        public object Lock { get; } = new();
        public List<SerializableEvent> Events { get; } = new();
        public string MaxSortableUniqueId { get; set; } = string.Empty;
    }

    private readonly ConcurrentDictionary<string, ServiceState> _states = new(StringComparer.Ordinal);
    private readonly IServiceIdProvider _serviceIdProvider;

    public InMemoryExecutorEventStore(IServiceIdProvider? serviceIdProvider = null)
    {
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
        TaggedStreamCapabilityDescriptor.Native("InMemoryExecutor");

    private ServiceState GetState()
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        return _states.GetOrAdd(serviceId, _ => new ServiceState());
    }

    private static SerializableEvent Copy(SerializableEvent ev) =>
        ev with { Payload = (byte[])ev.Payload.Clone(), Tags = new List<string>(ev.Tags) };

    private static List<SerializableEvent> EventsWithTag(ServiceState state, string tagString) =>
        state.Events
            .Where(e => e.Tags.Contains(tagString))
            .OrderBy(e => e.SortableUniqueIdValue)
            .ToList();

    private static IEnumerable<SerializableEvent> After(IEnumerable<SerializableEvent> events, SortableUniqueId? since) =>
        since is null
            ? events
            : events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);

    public Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var stored = state.Events.FirstOrDefault(e => e.Id == eventId);
            return Task.FromResult(stored is null ? null : Copy(stored));
        }
    }

    public Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var tagString = tag.GetTag();
            var streams = EventsWithTag(state, tagString)
                .Select(e => new TagStream(tagString, e.Id, e.SortableUniqueIdValue));
            return Task.FromResult(streams);
        }
    }

    public Task<TagState> GetLatestTagAsync(ITag tag)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var tagEvents = EventsWithTag(state, tag.GetTag());
            var tagStateId = new TagStateId(tag, "InMemoryProjector");
            if (tagEvents.Count == 0)
            {
                return Task.FromResult(TagState.GetEmpty(tagStateId));
            }

            return Task.FromResult(new TagState(
                new EmptyTagStatePayload(),
                tagEvents.Count,
                tagEvents[^1].SortableUniqueIdValue,
                tagStateId.TagGroup,
                tagStateId.TagContent,
                tagStateId.TagProjectorName));
        }
    }

    public Task<bool> TagExistsAsync(ITag tag)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var tagString = tag.GetTag();
            return Task.FromResult(state.Events.Any(e => e.Tags.Contains(tagString)));
        }
    }

    public Task<long> GetEventCountAsync(SortableUniqueId? since = null)
    {
        var state = GetState();
        lock (state.Lock)
        {
            return Task.FromResult((long)After(state.Events, since).Count());
        }
    }

    public Task<string> GetLatestSortableUniqueIdAsync()
    {
        var state = GetState();
        lock (state.Lock)
        {
            return Task.FromResult(state.MaxSortableUniqueId);
        }
    }

    public Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var tagInfos = state.Events
                .SelectMany(e => e.Tags)
                .Distinct()
                .Where(t => string.IsNullOrEmpty(tagGroup) || t.StartsWith(tagGroup + ":"))
                .Select(tagString =>
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

            return Task.FromResult(tagInfos.AsEnumerable());
        }
    }

    public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
        ReadAllSerializableEventsAsync(since, null);

    public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var events = After(state.Events, since).OrderBy(e => e.SortableUniqueIdValue).AsEnumerable();
            if (maxCount.HasValue)
            {
                events = events.Take(maxCount.Value);
            }

            return Task.FromResult<IEnumerable<SerializableEvent>>(events.Select(Copy).ToList());
        }
    }

    public Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        var state = GetState();
        lock (state.Lock)
        {
            return Task.FromResult<IEnumerable<SerializableEvent>>(
                After(EventsWithTag(state, tag.GetTag()), since).Select(Copy).ToList());
        }
    }

    public Task<SerializableEventStreamReadResult> StreamSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default)
    {
        var state = GetState();
        return TaggedSerializableEventStreamHelper.StreamSnapshotByTagAsync(
            state.Lock,
            state.Events,
            static @event => @event.Tags,
            static @event => @event.SortableUniqueIdValue,
            Copy,
            tag,
            since,
            until,
            onEvent,
            cancellationToken);
    }

    public Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events)
    {
        var state = GetState();
        lock (state.Lock)
        {
            return Task.FromResult(InMemorySerializableEventWriter.Write(events, new EventStoreAdapter(state)));
        }
    }

    private sealed class EventStoreAdapter : InMemorySerializableEventWriter.IAddableEventStore
    {
        private readonly ServiceState _state;
        public EventStoreAdapter(ServiceState state) => _state = state;

        public void AddSerializableEvent(SerializableEvent ev)
        {
            _state.Events.Add(Copy(ev));
            if (string.Compare(ev.SortableUniqueIdValue, _state.MaxSortableUniqueId, StringComparison.Ordinal) > 0)
            {
                _state.MaxSortableUniqueId = ev.SortableUniqueIdValue;
            }
        }

        public int CountEventsWithTag(string tagString) =>
            _state.Events.Count(e => e.Tags.Contains(tagString));
    }
}
