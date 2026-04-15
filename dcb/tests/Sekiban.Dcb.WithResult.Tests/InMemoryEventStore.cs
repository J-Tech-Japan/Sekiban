using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Simple in-memory event store for testing
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly List<Event> _events = new();
    private readonly object _lock = new();

    public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
    {
        lock (_lock)
        {
            var events = _events.AsEnumerable();

            if (since != null)
            {
                events = events.Where(e => string.Compare(
                        e.SortableUniqueIdValue,
                        since.Value,
                        StringComparison.Ordinal) >
                    0);
            }

            events = events.OrderBy(e => e.SortableUniqueIdValue);
            if (maxCount.HasValue)
            {
                events = events.Take(maxCount.Value);
            }

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
                events = events.Where(e => string.Compare(
                        e.SortableUniqueIdValue,
                        since.Value,
                        StringComparison.Ordinal) >
                    0);
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
            if (evt != null)
                return Task.FromResult(ResultBox.FromValue(evt));

            return Task.FromResult(ResultBox.Error<Event>(new KeyNotFoundException($"Event {eventId} not found")));
        }
    }

    public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        IEnumerable<Event> events)
    {
        lock (_lock)
        {
            var eventList = events.ToList();
            _events.AddRange(eventList);

            // Create TagWriteResult for each unique tag
            var tagWrites = new List<TagWriteResult>();
            var uniqueTags = eventList.SelectMany(e => e.Tags).Distinct();

            foreach (var tagString in uniqueTags)
            {
                // Count how many events have this tag (version)
                var tagEventCount = _events.Count(e => e.Tags.Contains(tagString));
                var lastEvent = _events
                    .Where(e => e.Tags.Contains(tagString))
                    .OrderBy(e => e.SortableUniqueIdValue)
                    .LastOrDefault();

                if (lastEvent != null)
                {
                    tagWrites.Add(new TagWriteResult(tagString, tagEventCount, DateTimeOffset.UtcNow));
                }
            }

            return Task.FromResult(
                ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>((eventList, tagWrites)));
        }
    }

    public async Task<ResultBox<Event>> WriteEventAsync(Event evt)
    {
        var result = await WriteEventsAsync(new[] { evt });
        if (!result.IsSuccess)
        {
            return ResultBox.Error<Event>(result.GetException());
        }

        return ResultBox.FromValue(result.GetValue().Events[0]);
    }

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var tagEvents
                = _events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).ToList();

            if (!tagEvents.Any())
                return Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));

            // Create TagStream for each event with this tag
            var tagStreams = tagEvents.Select(e => new TagStream(tagString, e.Id, e.SortableUniqueIdValue));

            return Task.FromResult(ResultBox.FromValue(tagStreams));
        }
    }

    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var tagEvents
                = _events.Where(e => e.Tags.Contains(tagString)).OrderBy(e => e.SortableUniqueIdValue).ToList();

            var tagStateId = new TagStateId(tag, "TestProjector");

            if (!tagEvents.Any())
            {
                var emptyState = TagState.GetEmpty(tagStateId);
                return Task.FromResult(ResultBox.FromValue(emptyState));
            }

            var lastEvent = tagEvents.Last();
            var state = new TagState(
                new EmptyTagStatePayload(),
                tagEvents.Count, // Version should be the count of events
                lastEvent.SortableUniqueIdValue,
                tagStateId.TagGroup,
                tagStateId.TagContent,
                tagStateId.TagProjectorName);

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

    public Task AppendEventAsync(Event evt)
    {
        lock (_lock)
        {
            _events.Add(evt);
        }
        return Task.CompletedTask;
    }

    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
    {
        lock (_lock)
        {
            var events = _events.AsEnumerable();

            if (since != null)
            {
                events = events.Where(e => string.Compare(
                    e.SortableUniqueIdValue,
                    since.Value,
                    StringComparison.Ordinal) > 0);
            }

            return Task.FromResult(ResultBox.FromValue((long)events.Count()));
        }
    }

    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
    {
        lock (_lock)
        {
            var allTags = _events
                .SelectMany(e => e.Tags)
                .Distinct()
                .Where(t => string.IsNullOrEmpty(tagGroup) || t.StartsWith(tagGroup + ":"))
                .ToList();

            var tagInfos = allTags.Select(tagString =>
            {
                var group = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
                var tagEvents = _events.Where(e => e.Tags.Contains(tagString)).ToList();

                return new TagInfo(
                    tagString,
                    group,
                    tagEvents.Count,
                    tagEvents.Min(e => e.SortableUniqueIdValue),
                    tagEvents.Max(e => e.SortableUniqueIdValue),
                    null,
                    null);
            })
            .OrderBy(t => t.TagGroup)
            .ThenBy(t => t.Tag)
            .ToList();

            return Task.FromResult(ResultBox.FromValue(tagInfos.AsEnumerable()));
        }
    }

    public Task<ResultBox<string>> GetMaxTagInTagGroupAsync(string tagGroup)
    {
        lock (_lock)
        {
            var prefix = tagGroup + ":";
            var maxTag = _events
                .SelectMany(e => e.Tags)
                .Where(t => t.StartsWith(prefix, StringComparison.Ordinal))
                .Aggregate(
                    string.Empty,
                    (current, candidate) => StringComparer.Ordinal.Compare(candidate, current) > 0
                        ? candidate
                        : current);
            return Task.FromResult(ResultBox.FromValue(maxTag));
        }
    }

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
        ReadAllSerializableEventsAsync(since, null);

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        lock (_lock)
        {
            var events = _events.AsEnumerable();
            if (since != null)
            {
                events = events.Where(e => string.Compare(
                    e.SortableUniqueIdValue,
                    since.Value,
                    StringComparison.Ordinal) > 0);
            }

            events = events.OrderBy(e => e.SortableUniqueIdValue);
            if (maxCount.HasValue)
            {
                events = events.Take(maxCount.Value);
            }

            var serializable = events.Select(ToSerializableEvent).ToList();
            return Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>(serializable));
        }
    }

    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since = null)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var events = _events.Where(e => e.Tags.Contains(tagString));
            if (since != null)
            {
                events = events.Where(e => string.Compare(
                    e.SortableUniqueIdValue,
                    since.Value,
                    StringComparison.Ordinal) > 0);
            }

            var serializable = events
                .OrderBy(e => e.SortableUniqueIdValue)
                .Select(ToSerializableEvent)
                .ToList();
            return Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>(serializable));
        }
    }

    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
    {
        lock (_lock)
        {
            var evt = _events.FirstOrDefault(e => e.Id == eventId);
            if (evt is null)
            {
                return Task.FromResult(
                    ResultBox.Error<SerializableEvent>(new KeyNotFoundException($"Event {eventId} not found")));
            }

            return Task.FromResult(ResultBox.FromValue(ToSerializableEvent(evt)));
        }
    }

    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events)
    {
        return Task.FromResult(
            ResultBox.Error<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>(
                new NotSupportedException("InMemoryEventStore test double does not support WriteSerializableEventsAsync.")));
    }

    private static SerializableEvent ToSerializableEvent(Event ev)
    {
        var payloadJson = JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType());
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(payloadJson),
            ev.SortableUniqueIdValue,
            ev.Id,
            ev.EventMetadata,
            ev.Tags.ToList(),
            ev.EventType);
    }
}
