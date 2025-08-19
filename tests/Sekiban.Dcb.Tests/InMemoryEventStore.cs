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
        // For testing, just return all events
        return ReadAllEventsAsync(since);
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
            
            // Return empty tag writes for testing
            var tagWrites = new List<TagWriteResult>();
            
            return Task.FromResult(ResultBoxes.ResultBox.FromValue<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>(
                (eventList, tagWrites)));
        }
    }

    public Task<ResultBoxes.ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        return Task.FromResult(ResultBoxes.ResultBox.FromValue(Enumerable.Empty<TagStream>()));
    }

    public Task<ResultBoxes.ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        var tagStateId = new TagStateId(tag, "TestProjector");
        var emptyState = TagState.GetEmpty(tagStateId);
        return Task.FromResult(ResultBoxes.ResultBox.FromValue(emptyState));
    }

    public Task<ResultBoxes.ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        return Task.FromResult(ResultBoxes.ResultBox.FromValue(false));
    }
}