using System.Text;
using System.Text.Json;
using DcbLib.Actors;
using DcbLib.Storage;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.InMemory;

/// <summary>
/// In-memory implementation of ITagStateActorCommon for testing
/// Computes tag state by reading events and projecting them
/// </summary>
public class InMemoryTagStateActor : ITagStateActorCommon
{
    private readonly TagStateId _tagStateId;
    private readonly IEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IActorObjectAccessor _actorAccessor;
    private TagState? _cachedState;
    private readonly object _stateLock = new();
    
    public InMemoryTagStateActor(string tagStateId, IEventStore eventStore, DcbDomainTypes domainTypes)
        : this(tagStateId, eventStore, domainTypes, null!)
    {
    }
    
    public InMemoryTagStateActor(string tagStateId, IEventStore eventStore, DcbDomainTypes domainTypes, IActorObjectAccessor actorAccessor)
    {
        if (string.IsNullOrWhiteSpace(tagStateId))
            throw new ArgumentNullException(nameof(tagStateId));
            
        _tagStateId = TagStateId.Parse(tagStateId);
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _actorAccessor = actorAccessor!;
    }
    
    public string GetTagStateActorId()
    {
        return _tagStateId.GetTagStateId();
    }
    
    public SerializableTagState GetState()
    {
        lock (_stateLock)
        {
            var tagState = GetTagState();
            
            if (tagState.Payload is EmptyTagStatePayload)
            {
                return new SerializableTagState(
                    Array.Empty<byte>(),
                    tagState.Version,
                    tagState.LastSortedUniqueId,
                    tagState.TagGroup,
                    tagState.TagContent,
                    tagState.TagProjector,
                    "EmptyTagStatePayload"
                );
            }
            
            var jsonOptions = _domainTypes.JsonSerializerOptions;
            var payloadJson = JsonSerializer.Serialize(tagState.Payload, tagState.Payload.GetType(), jsonOptions);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            
            return new SerializableTagState(
                payloadBytes,
                tagState.Version,
                tagState.LastSortedUniqueId,
                tagState.TagGroup,
                tagState.TagContent,
                tagState.TagProjector,
                tagState.Payload.GetType().Name
            );
        }
    }
    
    public TagState GetTagState()
    {
        lock (_stateLock)
        {
            // Return cached state if available
            if (_cachedState != null)
            {
                return _cachedState;
            }
            
            // Compute state from events
            _cachedState = ComputeStateFromEvents();
            return _cachedState;
        }
    }
    
    public void UpdateState(TagState newState)
    {
        lock (_stateLock)
        {
            // Validate that the identity hasn't changed
            if (newState.TagGroup != _tagStateId.TagGroup || 
                newState.TagContent != _tagStateId.TagContent ||
                newState.TagProjector != _tagStateId.TagProjectorName)
            {
                throw new InvalidOperationException(
                    $"Cannot change tag state identity. Expected {_tagStateId}, " +
                    $"but got {newState.TagGroup}:{newState.TagContent}:{newState.TagProjector}");
            }
            
            _cachedState = newState;
        }
    }
    
    private TagState ComputeStateFromEvents()
    {
        // Create the tag to query events
        var tag = CreateTag(_tagStateId.TagGroup, _tagStateId.TagContent);
        
        // Get the latest sortable unique ID from TagConsistentActor if available
        string? latestSortableUniqueId = null;
        if (_actorAccessor != null)
        {
            var tagConsistentActorId = $"{_tagStateId.TagGroup}:{_tagStateId.TagContent}";
            var tagConsistentActorResult = _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId).Result;
            if (tagConsistentActorResult.IsSuccess)
            {
                var tagConsistentActor = tagConsistentActorResult.GetValue();
                latestSortableUniqueId = tagConsistentActor.GetLatestSortableUniqueId();
            }
        }
        
        // Get the projector
        var projectorResult = _domainTypes.TagProjectorTypes.GetTagProjector(_tagStateId.TagProjectorName);
        if (!projectorResult.IsSuccess)
        {
            // Return empty state if projector not found
            return new TagState(
                new EmptyTagStatePayload(),
                0,
                "",
                _tagStateId.TagGroup,
                _tagStateId.TagContent,
                _tagStateId.TagProjectorName
            );
        }
        
        var projector = projectorResult.GetValue();
        
        // Read all events for this tag
        var eventsResult = _eventStore.ReadEventsByTagAsync(tag).Result;
        if (!eventsResult.IsSuccess)
        {
            // Return empty state if events cannot be read
            return new TagState(
                new EmptyTagStatePayload(),
                0,
                "",
                _tagStateId.TagGroup,
                _tagStateId.TagContent,
                _tagStateId.TagProjectorName
            );
        }
        
        var events = eventsResult.GetValue().ToList();
        
        // Filter events up to the latest sortable unique ID if provided
        if (!string.IsNullOrEmpty(latestSortableUniqueId))
        {
            events = events
                .Where(e => string.Compare(e.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) <= 0)
                .ToList();
        }
        
        // Project events to build state
        ITagStatePayload? currentState = null;
        var version = 0;
        var lastSortedUniqueId = "";
        
        foreach (var evt in events)
        {
            // Initialize state with EmptyTagStatePayload if needed
            if (currentState == null)
            {
                currentState = new EmptyTagStatePayload();
            }
            
            // Project the event
            currentState = projector.Project(currentState, evt.Payload);
            version++;
            
            // Keep track of the last sortable unique id
            if (!string.IsNullOrEmpty(evt.SortableUniqueIdValue))
            {
                lastSortedUniqueId = evt.SortableUniqueIdValue;
            }
        }
        
        // If no events were processed, ensure we have at least EmptyTagStatePayload
        if (currentState == null)
        {
            currentState = new EmptyTagStatePayload();
        }
        
        return new TagState(
            currentState,
            version,
            lastSortedUniqueId,
            _tagStateId.TagGroup,
            _tagStateId.TagContent,
            _tagStateId.TagProjectorName
        );
    }
    
    private ITag CreateTag(string tagGroup, string tagContent)
    {
        // Create a generic tag implementation for the actor
        return new GenericTag(tagGroup, tagContent);
    }
    
    private ITagStatePayload? CreateInitialState(string tagGroup)
    {
        // Return null to let the projector handle initial state
        return null;
    }
    
    /// <summary>
    /// Generic tag implementation for use within the actor
    /// </summary>
    private class GenericTag : ITag
    {
        private readonly string _tagGroup;
        private readonly string _tagContent;
        
        public GenericTag(string tagGroup, string tagContent)
        {
            _tagGroup = tagGroup;
            _tagContent = tagContent;
        }
        
        public bool IsConsistencyTag() => true; // Assume all tags are consistency tags for now
        public string GetTagGroup() => _tagGroup;
        public string GetTag() => $"{_tagGroup}:{_tagContent}";
    }
    
    /// <summary>
    /// Clears the cached state, forcing recomputation on next access
    /// </summary>
    public void ClearCache()
    {
        lock (_stateLock)
        {
            _cachedState = null;
        }
    }
}