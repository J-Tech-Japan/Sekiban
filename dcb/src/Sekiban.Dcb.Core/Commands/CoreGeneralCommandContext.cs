using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Core implementation of ICoreCommandContext and ICommandContextResultAccessor
///     Provides access to tag states during command processing and tracks accessed states
///     Throws on failure (shared by WithResult and WithoutResult packages)
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class CoreGeneralCommandContext : ICoreCommandContext, ICommandContextResultAccessor
{
    private readonly Dictionary<ITag, TagState> _accessedTagStates = new();
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly List<EventPayloadWithTags> _appendedEvents = new();
    private readonly DcbDomainTypes _domainTypes;

    public CoreGeneralCommandContext(IActorObjectAccessor actorAccessor, DcbDomainTypes domainTypes)
    {
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }

    public async Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload where TProjector : ITagProjector<TProjector>
    {
        // Validate tag before calling actor
        var validationErrors = TagValidator.ValidateTag(tag);
        if (validationErrors.Count > 0)
        {
            throw new TagValidationErrorsException(validationErrors);
        }
        // Get the TagStateActor
        var tagStateId = new TagStateId(tag, TProjector.ProjectorName);
        var tagStateActorId = tagStateId.GetTagStateId();

        var actorResult = await _actorAccessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);
        if (!actorResult.IsSuccess)
        {
            // If actor doesn't exist, return empty state with EmptyTagStatePayload
            // We can't cast EmptyTagStatePayload to TState, so we need to handle this differently
            var emptyPayload = new EmptyTagStatePayload();
            if (emptyPayload is TState)
            {
                return new TagStateTyped<TState>(
                    tag,
                    (TState)(ITagStatePayload)emptyPayload,
                    0,
                    DateTimeOffset.UtcNow);
            }
            // Return error indicating that the state is empty and cannot be cast to the requested type
            throw new InvalidCastException(
                $"Expected state payload of type {typeof(TState).Name} but got {nameof(EmptyTagStatePayload)}");
        }

        var actor = actorResult.GetValue();
        var serializableState = await actor.GetStateAsync();

        // Deserialize the payload
        var payload = _domainTypes.TagStatePayloadTypes.DeserializePayload(
            serializableState.ResolvedPayloadName,
            serializableState.Payload);

        // Track the accessed state as TagState
        var tagState = new TagState(
            payload,
            serializableState.Version,
            serializableState.LastSortedUniqueId,
            serializableState.TagGroup,
            serializableState.TagContent,
            serializableState.TagProjector,
            serializableState.ProjectorVersion);
        _accessedTagStates[tag] = tagState;

        // Check if the payload is of the expected type
        if (payload is TState typedPayload)
        {
            return new TagStateTyped<TState>(
                tag,
                typedPayload,
                serializableState.Version,
                DateTimeOffset.UtcNow // TODO: We might need to track actual last modified time
                );
        }

        // If payload type doesn't match, return error
        throw new InvalidCastException(
            $"Expected state payload of type {typeof(TState).Name} but got {payload.GetType().Name}");
    }

    public async Task<TagState> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector>
    {
        // Validate tag before calling actor
        var validationErrors = TagValidator.ValidateTag(tag);
        if (validationErrors.Count > 0)
        {
            throw new TagValidationErrorsException(validationErrors);
        }
        // Get the TagStateActor
        var tagStateId = new TagStateId(tag, TProjector.ProjectorName);
        var tagStateActorId = tagStateId.GetTagStateId();

        var actorResult = await _actorAccessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);
        if (!actorResult.IsSuccess)
        {
            // If actor doesn't exist, return empty state
            return new TagState(
                new EmptyTagStatePayload(),
                0,
                "",
                tag.GetTagGroup(),
                tag.GetTag().Substring(tag.GetTagGroup().Length + 1),
                TProjector.ProjectorName,
                TProjector.ProjectorVersion);
        }

        var actor = actorResult.GetValue();
        var serializableState = await actor.GetStateAsync();

        // Deserialize the payload
        var payload = _domainTypes.TagStatePayloadTypes.DeserializePayload(
            serializableState.ResolvedPayloadName,
            serializableState.Payload);

        // Create TagState from SerializableTagState
        var tagState = new TagState(
            payload,
            serializableState.Version,
            serializableState.LastSortedUniqueId,
            serializableState.TagGroup,
            serializableState.TagContent,
            serializableState.TagProjector,
            serializableState.ProjectorVersion);

        // Track the accessed state
        _accessedTagStates[tag] = tagState;

        return tagState;
    }

    public async Task<bool> TagExistsAsync(ITag tag)
    {
        // Validate tag before calling actor
        var validationErrors = TagValidator.ValidateTag(tag);
        if (validationErrors.Count > 0)
        {
            throw new TagValidationErrorsException(validationErrors);
        }
        // Get the TagConsistentActor for this tag
        var tagConsistentActorId = tag.GetTag();

        var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        if (!actorResult.IsSuccess)
        {
            // Actor doesn't exist means tag doesn't exist (not an error, just no data)
            return false;
        }

        var actor = actorResult.GetValue();
        var latestSortableUniqueId = (await actor.GetLatestSortableUniqueIdAsync()).GetValueOrRethrow();

        // If there's a latest sortable unique ID, the tag EXISTS.
        if (!string.IsNullOrEmpty(latestSortableUniqueId))
        {
            // SEK-G19: track the existing tag's version as accessed state (if not already tracked by a GetStateAsync),
            // so a later write that REFERENCES this existing tag reserves on its CURRENT version (an exact-token match,
            // an update) rather than defaulting to expect-empty. Without this, a tag that a command only checks for
            // EXISTENCE would reserve with an empty expected version and — now that empty means "expect the tag to be
            // empty" — be wrongly rejected against its own committed state. A tag that does NOT exist stays untracked, so
            // a first write still reserves expect-empty and a second first-write conflicts (the #1085 fix is preserved).
            if (!_accessedTagStates.ContainsKey(tag))
            {
                TrackObservedVersion(tag, latestSortableUniqueId);
            }
            return true;
        }

        TrackObservedVersion(tag, string.Empty);
        return false;
    }

    public async Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag)
    {
        // Validate tag before calling actor
        var validationErrors = TagValidator.ValidateTag(tag);
        if (validationErrors.Count > 0)
        {
            throw new TagValidationErrorsException(validationErrors);
        }
        // Get the TagConsistentActor for this tag
        var tagConsistentActorId = tag.GetTag();

        var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        if (!actorResult.IsSuccess)
        {
            // If actor doesn't exist, return empty string (not an error, just no data)
            return "";
        }

        var actor = actorResult.GetValue();
        // For now, we'll skip tracking in GetTagLatestSortableUniqueIdAsync to avoid double-fetching
        // The state will be tracked when GetStateAsync is called
        return (await actor.GetLatestSortableUniqueIdAsync()).GetValueOrRethrow();
    }

    public Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags)
    {
        if (ev is null)
        {
            return Task.FromException<EventOrNone>(new ArgumentNullException(nameof(ev)));
        }

        return AppendEvent(new EventPayloadWithTags(ev, tags?.ToList() ?? new List<ITag>()));
    }

    public Task<EventOrNone> AppendEvent(EventPayloadWithTags ev)
    {
        try
        {
            if (ev == null)
            {
                return Task.FromException<EventOrNone>(new ArgumentNullException(nameof(ev)));
            }

            // Validate all tags in the event before appending
            var validationErrors = TagValidator.ValidateTags(ev.Tags);
            if (validationErrors.Count > 0)
            {
                return Task.FromException<EventOrNone>(new TagValidationErrorsException(validationErrors));
            }

            // Store the event for later processing by CommandExecutor
            _appendedEvents.Add(ev);

            return Task.FromResult(EventOrNone.FromValue(ev));
        }
        catch (Exception ex)
        {
            return Task.FromException<EventOrNone>(ex);
        }
    }

    /// <summary>
    ///     Gets all events that have been appended during command processing
    ///     This would be used by CommandExecutor to retrieve events for persistence
    /// </summary>
    public IReadOnlyList<EventPayloadWithTags> GetAppendedEvents() => _appendedEvents.AsReadOnly();

    /// <summary>
    ///     Gets the list of tags and their states that were accessed during command execution
    /// </summary>
    public IReadOnlyDictionary<ITag, TagState> GetAccessedTagStates() => _accessedTagStates.AsReadOnly();

    /// <summary>
    ///     Clears all tracked state accesses and appended events
    ///     This would be used by CommandExecutor after processing
    /// </summary>
    public void ClearResults()
    {
        _appendedEvents.Clear();
        _accessedTagStates.Clear();
    }

    /// <summary>
    ///     Clears all appended events
    ///     This might be used by CommandExecutor after successful persistence
    /// </summary>
    [Obsolete("Use ClearResults() instead")]
    public void ClearAppendedEvents() => _appendedEvents.Clear();

    private void TrackObservedVersion(ITag tag, string lastSortableUniqueId)
    {
        if (_accessedTagStates.ContainsKey(tag))
        {
            return;
        }

        var tagContent = tag.GetTag().Substring(tag.GetTagGroup().Length + 1);
        _accessedTagStates[tag] = new TagState(
            new EmptyTagStatePayload(),
            0,
            lastSortableUniqueId,
            tag.GetTagGroup(),
            tagContent,
            string.Empty,
            string.Empty);
    }
}
