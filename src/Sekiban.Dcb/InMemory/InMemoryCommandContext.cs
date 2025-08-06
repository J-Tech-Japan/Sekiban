using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using ResultBoxes;

namespace Sekiban.Dcb.InMemory;

/// <summary>
/// In-memory implementation of ICommandContext and ICommandContextResultAccessor
/// Provides access to tag states during command processing and tracks accessed states
/// </summary>
public class InMemoryCommandContext : ICommandContext, ICommandContextResultAccessor
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly List<EventPayloadWithTags> _appendedEvents = new();
    private readonly Dictionary<ITag, TagState> _accessedTagStates = new();
    
    public InMemoryCommandContext(IActorObjectAccessor actorAccessor, DcbDomainTypes domainTypes)
    {
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }
    
    public async Task<ResultBox<TagStateTyped<TState>>> GetStateAsync<TState, TProjector>(ITag tag) 
        where TState : ITagStatePayload
        where TProjector : ITagProjector, new()
    {
        try
        {
            // Get the TagStateActor
            var projector = new TProjector();
            var tagStateId = new TagStateId(tag, projector);
            var tagStateActorId = tagStateId.GetTagStateId();
            
            var actorResult = await _actorAccessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);
            if (!actorResult.IsSuccess)
            {
                // If actor doesn't exist, return empty state with EmptyTagStatePayload
                // We can't cast EmptyTagStatePayload to TState, so we need to handle this differently
                var emptyPayload = new EmptyTagStatePayload();
                if (emptyPayload is TState)
                {
                    return ResultBox.FromValue(new TagStateTyped<TState>(
                        tag,
                        (TState)(ITagStatePayload)emptyPayload,
                        0,
                        DateTimeOffset.UtcNow
                    ));
                }
                else
                {
                    // Return error indicating that the state is empty and cannot be cast to the requested type
                    return ResultBox.Error<TagStateTyped<TState>>(
                        new InvalidCastException($"Expected state payload of type {typeof(TState).Name} but got {nameof(EmptyTagStatePayload)}")
                    );
                }
            }
            
            var actor = actorResult.GetValue();
            var serializableState = await actor.GetStateAsync();
            
            // Deserialize the payload
            var payloadResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
                serializableState.TagPayloadName, 
                serializableState.Payload
            );
            
            if (!payloadResult.IsSuccess)
            {
                return ResultBox.Error<TagStateTyped<TState>>(payloadResult.GetException());
            }
            
            var payload = payloadResult.GetValue();
            
            // Track the accessed state as TagState
            var tagState = new TagState(
                payload,
                serializableState.Version,
                serializableState.LastSortedUniqueId,
                serializableState.TagGroup,
                serializableState.TagContent,
                serializableState.TagProjector
            );
            _accessedTagStates[tag] = tagState;
            
            // Check if the payload is of the expected type
            if (payload is TState typedPayload)
            {
                return ResultBox.FromValue(new TagStateTyped<TState>(
                    tag,
                    typedPayload,
                    serializableState.Version,
                    DateTimeOffset.UtcNow // TODO: We might need to track actual last modified time
                ));
            }
            
            // If payload type doesn't match, return error
            return ResultBox.Error<TagStateTyped<TState>>(
                new InvalidCastException($"Expected state payload of type {typeof(TState).Name} but got {payload.GetType().Name}")
            );
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagStateTyped<TState>>(ex);
        }
    }
    
    public async Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag) 
        where TProjector : ITagProjector, new()
    {
        try
        {
            // Get the TagStateActor
            var projector = new TProjector();
            var tagStateId = new TagStateId(tag, projector);
            var tagStateActorId = tagStateId.GetTagStateId();
            
            var actorResult = await _actorAccessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);
            if (!actorResult.IsSuccess)
            {
                // If actor doesn't exist, return empty state
                return ResultBox.FromValue(new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    "",
                    tag.GetTagGroup(),
                    tag.GetTag().Substring(tag.GetTagGroup().Length + 1),
                    projector.GetType().Name
                ));
            }
            
            var actor = actorResult.GetValue();
            var serializableState = await actor.GetStateAsync();
            
            // Deserialize the payload
            var payloadResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
                serializableState.TagPayloadName, 
                serializableState.Payload
            );
            
            if (!payloadResult.IsSuccess)
            {
                return ResultBox.Error<TagState>(payloadResult.GetException());
            }
            
            var payload = payloadResult.GetValue();
            
            // Create TagState from SerializableTagState
            var tagState = new TagState(
                payload,
                serializableState.Version,
                serializableState.LastSortedUniqueId,
                serializableState.TagGroup,
                serializableState.TagContent,
                serializableState.TagProjector
            );
            
            // Track the accessed state
            _accessedTagStates[tag] = tagState;
            
            return ResultBox.FromValue(tagState);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
    }
    
    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            // Get the TagConsistentActor for this tag
            var tagConsistentActorId = tag.GetTag();
            
            var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
            if (!actorResult.IsSuccess)
            {
                // Actor doesn't exist means tag doesn't exist (not an error, just no data)
                return ResultBox.FromValue(false);
            }
            
            var actor = actorResult.GetValue();
            var latestSortableUniqueIdResult = await actor.GetLatestSortableUniqueIdAsync();
            
            if (!latestSortableUniqueIdResult.IsSuccess)
            {
                return ResultBox.Error<bool>(latestSortableUniqueIdResult.GetException());
            }
            
            var latestSortableUniqueId = latestSortableUniqueIdResult.GetValue();
            
            // If there's a latest sortable unique ID, the tag exists
            if (!string.IsNullOrEmpty(latestSortableUniqueId))
            {
                // Since we're checking existence, we need to get the full state for tracking
                // We can't track without having the full TagState
                // For now, we'll skip tracking in TagExistsAsync to avoid double-fetching
                return ResultBox.FromValue(true);
            }
            
            return ResultBox.FromValue(false);
        }
        catch (Exception ex)
        {
            // Return error for actual exceptions
            return ResultBox.Error<bool>(ex);
        }
    }
    
    public async Task<ResultBox<string>> GetTagLatestSortableUniqueIdAsync(ITag tag)
    {
        try
        {
            // Get the TagConsistentActor for this tag
            var tagConsistentActorId = tag.GetTag();
            
            var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
            if (!actorResult.IsSuccess)
            {
                // If actor doesn't exist, return empty string (not an error, just no data)
                return ResultBox.FromValue("");
            }
            
            var actor = actorResult.GetValue();
            var latestSortableUniqueIdResult = await actor.GetLatestSortableUniqueIdAsync();
            
            if (!latestSortableUniqueIdResult.IsSuccess)
            {
                return ResultBox.Error<string>(latestSortableUniqueIdResult.GetException());
            }
            
            // For now, we'll skip tracking in GetTagLatestSortableUniqueIdAsync to avoid double-fetching
            // The state will be tracked when GetStateAsync is called
            
            return ResultBox.FromValue(latestSortableUniqueIdResult.GetValue());
        }
        catch (Exception ex)
        {
            // Return error for actual exceptions
            return ResultBox.Error<string>(ex);
        }
    }
    
    public ResultBox<EventOrNone> AppendEvent(EventPayloadWithTags ev)
    {
        try
        {
            if (ev == null)
            {
                return ResultBox.Error<EventOrNone>(new ArgumentNullException(nameof(ev)));
            }
            
            // Store the event for later processing by CommandExecutor
            _appendedEvents.Add(ev);
            
            return EventOrNone.Event(ev);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<EventOrNone>(ex);
        }
    }
    
    /// <summary>
    /// Gets all events that have been appended during command processing
    /// This would be used by CommandExecutor to retrieve events for persistence
    /// </summary>
    public IReadOnlyList<EventPayloadWithTags> GetAppendedEvents() => _appendedEvents.AsReadOnly();
    
    /// <summary>
    /// Gets the list of tags and their states that were accessed during command execution
    /// </summary>
    public IReadOnlyDictionary<ITag, TagState> GetAccessedTagStates() => _accessedTagStates.AsReadOnly();
    
    /// <summary>
    /// Clears all tracked state accesses and appended events
    /// This would be used by CommandExecutor after processing
    /// </summary>
    public void ClearResults()
    {
        _appendedEvents.Clear();
        _accessedTagStates.Clear();
    }
    
    /// <summary>
    /// Clears all appended events
    /// This might be used by CommandExecutor after successful persistence
    /// </summary>
    [Obsolete("Use ClearResults() instead")]
    public void ClearAppendedEvents() => _appendedEvents.Clear();
}