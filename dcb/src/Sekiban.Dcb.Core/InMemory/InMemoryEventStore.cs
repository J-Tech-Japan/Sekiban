using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of IEventStore for testing and development
///     Stores events and tag streams only - no tag state management
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private sealed class ServiceState
    {
        public object Lock { get; } = new();
        public List<Event> EventOrder { get; } = new();
        public Dictionary<Guid, Event> Events { get; } = new();
        public Dictionary<string, List<TagStream>> TagStreams { get; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<string, ServiceState> _states = new(StringComparer.Ordinal);
    private readonly IServiceIdProvider _serviceIdProvider;

    public InMemoryEventStore(IServiceIdProvider? serviceIdProvider = null)
    {
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    private ServiceState GetState()
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        return _states.GetOrAdd(serviceId, _ => new ServiceState());
    }

    /// <summary>
    ///     Clear all events and tag streams. Used for test isolation.
    /// </summary>
    public void Clear()
    {
        var state = GetState();
        lock (state.Lock)
        {
            state.EventOrder.Clear();
            state.Events.Clear();
            state.TagStreams.Clear();
        }
    }

    public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var events = state.EventOrder.AsEnumerable();

            if (since != null)
            {
                events = events.Where(e => string.Compare(
                        e.SortableUniqueIdValue,
                        since.Value,
                        StringComparison.Ordinal) >
                    0);
            }

            return Task.FromResult(ResultBox.FromValue(events));
        }
    }

    public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var tagString = tag.GetTag();
            var events = state.EventOrder.Where(e => e.Tags.Contains(tagString));

            if (since != null)
            {
                events = events.Where(e => string.Compare(
                        e.SortableUniqueIdValue,
                        since.Value,
                        StringComparison.Ordinal) >
                    0);
            }

            return Task.FromResult(ResultBox.FromValue(events));
        }
    }

    public Task<ResultBox<Event>> ReadEventAsync(Guid eventId) =>
        ReadEventInternalAsync(eventId);

    private Task<ResultBox<Event>> ReadEventInternalAsync(Guid eventId)
    {
        var state = GetState();
        lock (state.Lock)
        {
            return state.Events.TryGetValue(eventId, out var evt)
                ? Task.FromResult(ResultBox.FromValue(evt))
                : Task.FromResult(ResultBox.Error<Event>(new Exception($"Event {eventId} not found")));
        }
    }

    public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        IEnumerable<Event> events)
    {
        var state = GetState();
        lock (state.Lock)
        {
            var eventList = events.ToList();
            var writtenEvents = new List<Event>();
            var tagWriteResults = new List<TagWriteResult>();
            var tagVersions = new Dictionary<string, long>();

            // Track current versions for all affected tags
            var affectedTags = new HashSet<string>();
            foreach (var evt in eventList)
            {
                foreach (var tagString in evt.Tags)
                {
                    affectedTags.Add(tagString);
                }
            }

            // Get current versions
            foreach (var tagString in affectedTags)
            {
                if (state.TagStreams.TryGetValue(tagString, out var streams))
                {
                    tagVersions[tagString] = streams.Count;
                }
                else
                {
                    tagVersions[tagString] = 0;
                }
            }

            // Write events
            foreach (var evt in eventList)
            {
                state.Events[evt.Id] = evt;
                state.EventOrder.Add(evt);
                writtenEvents.Add(evt);

                // Add tag streams for each tag in the event
                foreach (var tagString in evt.Tags)
                {
                    var tagStream = new TagStream(tagString, evt.Id, evt.SortableUniqueIdValue);

                    if (!state.TagStreams.ContainsKey(tagString))
                    {
                        state.TagStreams[tagString] = new List<TagStream>();
                    }

                    state.TagStreams[tagString].Add(tagStream);
                }
            }

            // Create tag write results
            foreach (var tagString in affectedTags)
            {
                var newVersion = state.TagStreams[tagString].Count;
                tagWriteResults.Add(new TagWriteResult(tagString, newVersion, DateTimeOffset.UtcNow));
            }

            return Task.FromResult(
                ResultBox.FromValue(
                    (Events: (IReadOnlyList<Event>)writtenEvents,
                        TagWrites: (IReadOnlyList<TagWriteResult>)tagWriteResults)));
        }
    }

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        var tagString = tag.GetTag();

        var state = GetState();
        lock (state.Lock)
        {
            if (state.TagStreams.TryGetValue(tagString, out var streams))
            {
                return Task.FromResult(ResultBox.FromValue(streams.AsEnumerable()));
            }

            return Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));
        }
    }

    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        var tagString = tag.GetTag();

        var state = GetState();
        lock (state.Lock)
        {
            if (state.TagStreams.TryGetValue(tagString, out var streams) && streams.Any())
            {
                var latestStream = streams.OrderBy(s => s.SortableUniqueId).Last();
                var version = streams.Count;

                // Parse tag string to get group
                var tagParts = tagString.Split(':');
                var tagGroup = tagParts.Length > 0 ? tagParts[0] : "";

                // Return a simple TagState based on the latest stream
                var tagState = new TagState(
                    new EmptyTagStatePayload(),
                    version,
                    latestStream.SortableUniqueId,
                    tagGroup,
                    tagString,
                    "InMemoryProjector",
                    string.Empty);

                return Task.FromResult(ResultBox.FromValue(tagState));
            }

            return Task.FromResult(ResultBox.Error<TagState>(new Exception($"Tag {tagString} not found")));
        }
    }

    public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        var tagString = tag.GetTag();

        var state = GetState();
        lock (state.Lock)
        {
            return Task.FromResult(ResultBox.FromValue(state.TagStreams.ContainsKey(tagString)));
        }
    }

    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
    {
        var state = GetState();
        lock (state.Lock)
        {
            if (since == null)
            {
                return Task.FromResult(ResultBox.FromValue((long)state.EventOrder.Count));
            }

            var count = state.EventOrder.Count(e => string.Compare(
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
            var tagInfos = state.TagStreams
                .Where(kvp => string.IsNullOrEmpty(tagGroup) || kvp.Key.StartsWith(tagGroup + ":"))
                .Select(kvp =>
                {
                    var tagString = kvp.Key;
                    var streams = kvp.Value;
                    var group = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;

                    return new TagInfo(
                        tagString,
                        group,
                        streams.Count,
                        streams.Min(s => s.SortableUniqueId),
                        streams.Max(s => s.SortableUniqueId),
                        null, // InMemory doesn't track timestamps
                        null);
                })
                .OrderBy(t => t.TagGroup)
                .ThenBy(t => t.Tag)
                .ToList();

            return Task.FromResult(ResultBox.FromValue(tagInfos.AsEnumerable()));
        }
    }
}
