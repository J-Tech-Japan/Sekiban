using System.Collections.Concurrent;
using DcbLib.Common;
using DcbLib.Events;
using DcbLib.Storage;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.InMemory;

/// <summary>
/// In-memory implementation of IEventStore for testing and development
/// Stores events and tag streams only - no tag state management
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, Event> _events = new();
    private readonly ConcurrentDictionary<string, List<TagStream>> _tagStreams = new();
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
            var tagString = tag.GetTag();
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
    
    public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(IEnumerable<Event> events)
    {
        lock (_eventLock)
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
                if (_tagStreams.TryGetValue(tagString, out var streams))
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
                _events[evt.Id] = evt;
                _eventOrder.Add(evt);
                writtenEvents.Add(evt);
                
                // Add tag streams for each tag in the event
                foreach (var tagString in evt.Tags)
                {
                    var tagStream = new TagStream(tagString, evt.Id, evt.SortableUniqueIdValue);
                    
                    if (!_tagStreams.ContainsKey(tagString))
                    {
                        _tagStreams[tagString] = new List<TagStream>();
                    }
                    
                    _tagStreams[tagString].Add(tagStream);
                }
            }
            
            // Create tag write results
            foreach (var tagString in affectedTags)
            {
                var newVersion = _tagStreams[tagString].Count;
                tagWriteResults.Add(new TagWriteResult(
                    tagString,
                    newVersion,
                    DateTimeOffset.UtcNow
                ));
            }
            
            return Task.FromResult(ResultBox.FromValue((
                Events: (IReadOnlyList<Event>)writtenEvents,
                TagWrites: (IReadOnlyList<TagWriteResult>)tagWriteResults
            )));
        }
    }
    
    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        var tagString = tag.GetTag();
        
        lock (_eventLock)
        {
            if (_tagStreams.TryGetValue(tagString, out var streams))
            {
                return Task.FromResult(ResultBox.FromValue(streams.AsEnumerable()));
            }
            
            return Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));
        }
    }
    
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        var tagString = tag.GetTag();
        
        lock (_eventLock)
        {
            if (_tagStreams.TryGetValue(tagString, out var streams) && streams.Any())
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
                    "InMemoryProjector"
                );
                
                return Task.FromResult(ResultBox.FromValue(tagState));
            }
            
            return Task.FromResult(ResultBox.Error<TagState>(new Exception($"Tag {tagString} not found")));
        }
    }
    
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        var tagString = tag.GetTag();
        
        lock (_eventLock)
        {
            return Task.FromResult(ResultBox.FromValue(_tagStreams.ContainsKey(tagString)));
        }
    }
    
}