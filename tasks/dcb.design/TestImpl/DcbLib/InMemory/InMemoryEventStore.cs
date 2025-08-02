using System.Collections.Concurrent;
using DcbLib.Common;
using DcbLib.Events;
using DcbLib.Storage;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.InMemory;

/// <summary>
/// In-memory implementation of IEventStore for testing and development
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, Event> _events = new();
    private readonly ConcurrentDictionary<string, TagState> _tags = new();
    private readonly List<Event> _eventOrder = new();
    private readonly object _eventLock = new();
    
    public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        lock (_eventLock)
        {
            var events = _eventOrder.AsEnumerable();
            
            if (since != null)
            {
                events = events.Where(e => 
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            }
            
            return Task.FromResult(ResultBox.FromValue(events));
        }
    }
    
    public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        lock (_eventLock)
        {
            var tagString = $"{tag.GetTagGroup()}:{tag.GetTag()}";
            var events = _eventOrder.Where(e => e.Tags.Contains(tagString));
            
            if (since != null)
            {
                events = events.Where(e => 
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            }
            
            return Task.FromResult(ResultBox.FromValue(events));
        }
    }
    
    public Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        return _events.TryGetValue(eventId, out var evt)
            ? Task.FromResult(ResultBox.FromValue(evt))
            : Task.FromResult(ResultBox.Error<Event>(new Exception($"Event {eventId} not found")));
    }
    
    public Task<ResultBox<Guid>> WriteEventAsync(Event evt)
    {
        lock (_eventLock)
        {
            _events[evt.Id] = evt;
            _eventOrder.Add(evt);
            
            // Update tag states based on tags in the event
            foreach (var tagString in evt.Tags)
            {
                var tagKey = tagString;
                
                if (_tags.TryGetValue(tagKey, out var existingState))
                {
                    // Update existing tag state
                    _tags[tagKey] = existingState with 
                    { 
                        Version = existingState.Version + 1, 
                        LastSortedUniqueId = evt.SortableUniqueIdValue
                    };
                }
                else
                {
                    // Parse tag string to get group and content
                    var tagParts = tagString.Split(':');
                    if (tagParts.Length >= 2)
                    {
                        // Create new tag state
                        _tags[tagKey] = new TagState(
                            new EmptyTagStatePayload(), // Initial state
                            1,
                            evt.SortableUniqueIdValue,
                            tagParts[0], // Tag group
                            tagString,   // Full tag string
                            "InMemoryProjector"
                        );
                    }
                }
            }
            
            return Task.FromResult(ResultBox.FromValue(evt.Id));
        }
    }
    
    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        var tagString = $"{tag.GetTagGroup()}:{tag.GetTag()}";
        
        lock (_eventLock)
        {
            var tagStreams = _eventOrder
                .Where(e => e.Tags.Contains(tagString))
                .Select(e => new TagStream(tagString, e.Id, e.SortableUniqueIdValue))
                .ToList();
                
            return Task.FromResult(ResultBox.FromValue(tagStreams.AsEnumerable()));
        }
    }
    
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        var tagKey = $"{tag.GetTagGroup()}:{tag.GetTag()}";
        
        return _tags.TryGetValue(tagKey, out var state)
            ? Task.FromResult(ResultBox.FromValue(state))
            : Task.FromResult(ResultBox.Error<TagState>(new Exception($"Tag {tagKey} not found")));
    }
    
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        var tagKey = $"{tag.GetTagGroup()}:{tag.GetTag()}";
        return Task.FromResult(ResultBox.FromValue(_tags.ContainsKey(tagKey)));
    }
    
    public Task<ResultBox<TagWriteResult>> WriteTagAsync(ITag tag, TagState state)
    {
        var tagKey = $"{tag.GetTagGroup()}:{tag.GetTag()}";
        
        lock (_eventLock)
        {
            if (_tags.ContainsKey(tagKey))
            {
                return Task.FromResult(ResultBox.Error<TagWriteResult>(
                    new Exception($"Tag {tagKey} already exists")));
            }
            
            _tags[tagKey] = state;
            return Task.FromResult(ResultBox.FromValue(
                new TagWriteResult(tagKey, state.Version, DateTimeOffset.UtcNow)));
        }
    }
}