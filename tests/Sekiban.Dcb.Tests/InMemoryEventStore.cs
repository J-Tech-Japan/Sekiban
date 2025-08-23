using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Simple in-memory event store for testing
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly List<Event> _events = new();
    private readonly object _lock = new();

    public Task AppendEventAsync(Event evt)
    {
        lock (_lock)
        {
            _events.Add(evt);
        }
        return Task.CompletedTask;
    }

    public Task<ResultBoxes.ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        lock (_lock)
        {
            var events = _events.AsEnumerable();
            
            if (since != null)
            {
                events = events.Where(e => 
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            }
            
            events = events.OrderBy(e => e.SortableUniqueIdValue);
            
            return Task.FromResult(ResultBoxes.ResultBox.FromValue(events));
        }
    }

    public Task<ResultBoxes.ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var events = _events.Where(e => e.Tags.Contains(tagString));
            
            if (since != null)
            {
                events = events.Where(e => 
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            }
            
            events = events.OrderBy(e => e.SortableUniqueIdValue);
            
            return Task.FromResult(ResultBoxes.ResultBox.FromValue(events.AsEnumerable()));
        }
    }

    public Task<ResultBoxes.ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        lock (_lock)
        {
            var evt = _events.FirstOrDefault(e => e.Id == eventId);
            if (evt != null)
                return Task.FromResult(ResultBoxes.ResultBox.FromValue(evt));
            
            return Task.FromResult(ResultBoxes.ResultBox.Error<Event>(
                new KeyNotFoundException($"Event {eventId} not found")));
        }
    }

    public Task<ResultBoxes.ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
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
                var lastEvent = _events.Where(e => e.Tags.Contains(tagString))
                                      .OrderBy(e => e.SortableUniqueIdValue)
                                      .LastOrDefault();
                
                if (lastEvent != null)
                {
                    tagWrites.Add(new TagWriteResult(
                        tagString,
                        tagEventCount,
                        DateTimeOffset.UtcNow));
                }
            }
            
            return Task.FromResult(ResultBoxes.ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>(
                (eventList, tagWrites)));
        }
    }

    public Task<ResultBoxes.ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var tagEvents = _events.Where(e => e.Tags.Contains(tagString))
                                  .OrderBy(e => e.SortableUniqueIdValue)
                                  .ToList();
            
            if (!tagEvents.Any())
                return Task.FromResult(ResultBoxes.ResultBox.FromValue(Enumerable.Empty<TagStream>()));
            
            // Create TagStream for each event with this tag
            var tagStreams = tagEvents.Select(e => new TagStream(tagString, e.Id, e.SortableUniqueIdValue));
            
            return Task.FromResult(ResultBoxes.ResultBox.FromValue(tagStreams));
        }
    }

    public Task<ResultBoxes.ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var tagEvents = _events.Where(e => e.Tags.Contains(tagString))
                                  .OrderBy(e => e.SortableUniqueIdValue)
                                  .ToList();
            
            var tagStateId = new TagStateId(tag, "TestProjector");
            
            if (!tagEvents.Any())
            {
                var emptyState = TagState.GetEmpty(tagStateId);
                return Task.FromResult(ResultBoxes.ResultBox.FromValue(emptyState));
            }
            
            var lastEvent = tagEvents.Last();
            var state = new TagState(
                new EmptyTagStatePayload(),
                tagEvents.Count,  // Version should be the count of events
                lastEvent.SortableUniqueIdValue,
                tagStateId.TagGroup,
                tagStateId.TagContent,
                tagStateId.TagProjectorName);
            
            return Task.FromResult(ResultBoxes.ResultBox.FromValue(state));
        }
    }

    public Task<ResultBoxes.ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        lock (_lock)
        {
            var tagString = tag.GetTag();
            var exists = _events.Any(e => e.Tags.Contains(tagString));
            return Task.FromResult(ResultBoxes.ResultBox.FromValue(exists));
        }
    }
}