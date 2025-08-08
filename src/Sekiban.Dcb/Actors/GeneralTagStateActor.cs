using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General implementation of ITagStateActorCommon
///     Computes tag state by reading events and projecting them
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class GeneralTagStateActor : ITagStateActorCommon
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly TagStateOptions _options;
    private readonly ITagStatePersistent _statePersistent;
    private readonly TagStateId _tagStateId;

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IActorObjectAccessor actorAccessor) : this(
        tagStateId,
        eventStore,
        domainTypes,
        new TagStateOptions(),
        actorAccessor,
        new InMemoryTagStatePersistent())
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor) : this(
        tagStateId,
        eventStore,
        domainTypes,
        options,
        actorAccessor,
        new InMemoryTagStatePersistent())
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ITagStatePersistent statePersistent)
    {
        if (string.IsNullOrWhiteSpace(tagStateId))
            throw new ArgumentNullException(nameof(tagStateId));

        _tagStateId = TagStateId.Parse(tagStateId);
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _statePersistent = statePersistent ?? throw new ArgumentNullException(nameof(statePersistent));
    }

    public Task<string> GetTagStateActorIdAsync() => Task.FromResult(_tagStateId.GetTagStateId());

    public async Task<SerializableTagState> GetStateAsync()
    {
        var tagState = await GetTagStateAsync();

        if (tagState.Payload is EmptyTagStatePayload)
        {
            return new SerializableTagState(
                Array.Empty<byte>(),
                tagState.Version,
                tagState.LastSortedUniqueId,
                tagState.TagGroup,
                tagState.TagContent,
                tagState.TagProjector,
                "EmptyTagStatePayload",
                tagState.ProjectorVersion);
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
            tagState.Payload.GetType().Name,
            tagState.ProjectorVersion);
    }

    public async Task<TagState> GetTagStateAsync()
    {
        // First, check if we can use cached state
        string? currentLatestSortableUniqueId = null;
        var tagConsistentActorId = $"{_tagStateId.TagGroup}:{_tagStateId.TagContent}";
        var tagConsistentActorResult
            = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        if (tagConsistentActorResult.IsSuccess)
        {
            var tagConsistentActor = tagConsistentActorResult.GetValue();
            var latestSortableUniqueIdResult = await tagConsistentActor.GetLatestSortableUniqueIdAsync();
            if (latestSortableUniqueIdResult.IsSuccess)
            {
                currentLatestSortableUniqueId = latestSortableUniqueIdResult.GetValue();
            }
        }

        // Check if we have cached state and if it's still valid
        var cachedState = await _statePersistent.LoadStateAsync();
        if (cachedState != null)
        {
            // If cached state matches current state, return cached
            if (currentLatestSortableUniqueId == null ||
                cachedState.LastSortedUniqueId == currentLatestSortableUniqueId)
            {
                return cachedState;
            }
        }

        // Compute state from events (outside the lock to avoid deadlock)
        var computedState = await ComputeStateFromEventsAsync();

        // Update cache
        // Double-check in case another thread computed it
        cachedState = await _statePersistent.LoadStateAsync();
        if (cachedState != null &&
            (currentLatestSortableUniqueId == null || cachedState.LastSortedUniqueId == currentLatestSortableUniqueId))
        {
            return cachedState;
        }

        await _statePersistent.SaveStateAsync(computedState);
        return computedState;
    }

    public async Task UpdateStateAsync(TagState newState)
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

        await _statePersistent.SaveStateAsync(newState);
    }

    private async Task<TagState> ComputeStateFromEventsAsync()
    {
        // Create the tag to query events
        var tag = CreateTag(_tagStateId.TagGroup, _tagStateId.TagContent);

        // Get the latest sortable unique ID from TagConsistentActor
        string? latestSortableUniqueId = null;
        var tagConsistentActorId = $"{_tagStateId.TagGroup}:{_tagStateId.TagContent}";
        var tagConsistentActorResult
            = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        if (tagConsistentActorResult.IsSuccess)
        {
            var tagConsistentActor = tagConsistentActorResult.GetValue();
            var latestSortableUniqueIdResult = await tagConsistentActor.GetLatestSortableUniqueIdAsync();
            if (latestSortableUniqueIdResult.IsSuccess)
            {
                latestSortableUniqueId = latestSortableUniqueIdResult.GetValue();
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
                _tagStateId.TagProjectorName,
                string.Empty);
        }

        var projector = projectorResult.GetValue();

        // Read all events for this tag
        var eventsResult = await _eventStore.ReadEventsByTagAsync(tag);
        if (!eventsResult.IsSuccess)
        {
            // Return empty state if events cannot be read
            return new TagState(
                new EmptyTagStatePayload(),
                0,
                "",
                _tagStateId.TagGroup,
                _tagStateId.TagContent,
                _tagStateId.TagProjectorName,
                projector.GetProjectorVersion());
        }

        var events = eventsResult.GetValue().ToList();

        // If TagConsistentActor has no LastSortableUniqueId, return empty state
        if (string.IsNullOrEmpty(latestSortableUniqueId))
        {
            return new TagState(
                new EmptyTagStatePayload(),
                0,
                "",
                _tagStateId.TagGroup,
                _tagStateId.TagContent,
                _tagStateId.TagProjectorName,
                projector.GetProjectorVersion());
        }

        // Filter events up to the latest sortable unique ID if provided
        if (!string.IsNullOrEmpty(latestSortableUniqueId))
        {
            events = events
                .Where(e => string.Compare(e.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) <=
                    0)
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
            currentState = projector.Project(currentState, evt);
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
            _tagStateId.TagProjectorName,
            projector.GetProjectorVersion());
    }

    private ITag CreateTag(string tagGroup, string tagContent) =>
        // Create a generic tag implementation for the actor
        new GenericTag(tagGroup, tagContent);

    /// <summary>
    ///     Clears the cached state, forcing recomputation on next access
    /// </summary>
    public async Task ClearCacheAsync()
    {
        await _statePersistent.ClearStateAsync();
    }

    /// <summary>
    ///     Generic tag implementation for use within the actor
    /// </summary>
    private class GenericTag : ITag
    {
        private readonly string _tagContent;
        private readonly string _tagGroup;

        public GenericTag(string tagGroup, string tagContent)
        {
            _tagGroup = tagGroup;
            _tagContent = tagContent;
        }

        public bool IsConsistencyTag() => true; // Assume all tags are consistency tags for now
        public string GetTagGroup() => _tagGroup;
        public string GetTagContent() => _tagContent;
    }
}
