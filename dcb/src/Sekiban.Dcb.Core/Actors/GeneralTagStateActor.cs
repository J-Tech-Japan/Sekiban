using ResultBoxes;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General implementation of ITagStateActorCommon
///     Computes tag state by reading events and projecting them
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class GeneralTagStateActor : ITagStateActor
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly TagStateOptions _options;
    private readonly ITagStatePersistent _statePersistent;
    private readonly TagStateId _tagStateId;
    private readonly ILogger<GeneralTagStateActor> _logger;

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IActorObjectAccessor actorAccessor,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        domainTypes,
        new TagStateOptions(),
        actorAccessor,
        new InMemoryTagStatePersistent(),
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        domainTypes,
        options,
        actorAccessor,
        new InMemoryTagStatePersistent(),
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ITagStatePersistent statePersistent,
        ILogger<GeneralTagStateActor>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(tagStateId))
            throw new ArgumentNullException(nameof(tagStateId));

        _tagStateId = TagStateId.Parse(tagStateId);
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _statePersistent = statePersistent ?? throw new ArgumentNullException(nameof(statePersistent));
        _logger = logger ?? NullLogger<GeneralTagStateActor>.Instance;
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

        // Use ITagStatePayloadTypes for serialization
        var serializeResult = _domainTypes.TagStatePayloadTypes.SerializePayload(tagState.Payload);
        if (!serializeResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to serialize payload: {serializeResult.GetException().Message}");
        }

        return new SerializableTagState(
            serializeResult.GetValue(),
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
        // Always get the latest sortable unique ID from TagConsistentActor
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

        // Check if we have cached state
        var cachedState = await _statePersistent.LoadStateAsync();

        // If cached state exists and is up-to-date, return it
        if (cachedState != null && cachedState.LastSortedUniqueId == currentLatestSortableUniqueId)
        {
            return cachedState;
        }

        // Cache is stale or doesn't exist, compute new state
        // Pass the latest sortable unique ID and cached state to avoid duplicate calls
        var computedState = await ComputeStateFromEventsAsync(currentLatestSortableUniqueId, cachedState);

        // Save the newly computed state to cache
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

    private async Task<TagState> ComputeStateFromEventsAsync(string? latestSortableUniqueId, TagState? cachedState)
    {
        // Get the projector function
        var projectorFuncResult = _domainTypes.TagProjectorTypes.GetProjectorFunction(_tagStateId.TagProjectorName);
        if (!projectorFuncResult.IsSuccess)
        {
            // Return empty state if projector not found
            return TagState.GetEmpty(_tagStateId);
        }

        var projectFunc = projectorFuncResult.GetValue();

        // Get the projector version
        var versionResult = _domainTypes.TagProjectorTypes.GetProjectorVersion(_tagStateId.TagProjectorName);
        var projectorVersion = versionResult.IsSuccess ? versionResult.GetValue() : string.Empty;

        // Check if we can do incremental update
        var canIncrementalUpdate = cachedState != null &&
            cachedState.ProjectorVersion == projectorVersion &&
            !string.IsNullOrEmpty(cachedState.LastSortedUniqueId) &&
            !string.IsNullOrEmpty(latestSortableUniqueId) &&
            string.Compare(latestSortableUniqueId, cachedState.LastSortedUniqueId, StringComparison.Ordinal) > 0;

        // Create the tag to query events
        var tag = CreateTag(_tagStateId.TagGroup, _tagStateId.TagContent);

        // If TagConsistentActor has no LastSortableUniqueId, return empty state
        if (string.IsNullOrEmpty(latestSortableUniqueId))
        {
            return TagState.GetEmpty(_tagStateId) with { ProjectorVersion = projectorVersion };
        }

        ITagStatePayload? currentState = null;
        var version = 0;
        var lastSortedUniqueId = "";

        // Try incremental update if possible
        if (canIncrementalUpdate && cachedState != null)
        {
            // Use cached state as starting point
            currentState = cachedState.Payload;
            version = cachedState.Version;
            lastSortedUniqueId = cachedState.LastSortedUniqueId;

            // Read only new events (after cached state's last sortable unique ID)
            var eventsResult = await _eventStore.ReadEventsByTagAsync(tag);
            if (!eventsResult.IsSuccess)
            {
                // Log the error and throw exception instead of silently returning cached state
                var error = eventsResult.GetException();
                _logger.LogError(
                    error,
                    "[GeneralTagStateActor] Error reading events for tag {Tag}",
                    tag.GetTag());

                // For deserialization errors, we should not use cached state as it may be inconsistent
                // Instead, throw the error so developers can see and fix the issue
                throw new InvalidOperationException(
                    $"Failed to read events for tag {tag.GetTag()}: {error.Message}",
                    error);
            }

            var newEvents = eventsResult
                .GetValue()
                .Where(e =>
                    string.Compare(e.SortableUniqueIdValue, cachedState.LastSortedUniqueId, StringComparison.Ordinal) >
                    0 &&
                    string.Compare(e.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) <= 0)
                .ToList();

            // Project only the new events on top of cached state
            foreach (var evt in newEvents)
            {
                currentState = projectFunc(currentState, evt);
                version++;
                lastSortedUniqueId = evt.SortableUniqueIdValue;
            }
        }
        else
        {
            // Full rebuild: projector version changed or no valid cache
            var eventsResult = await _eventStore.ReadEventsByTagAsync(tag);
            if (!eventsResult.IsSuccess)
            {
                // Log the error for debugging
                var error = eventsResult.GetException();
                _logger.LogError(
                    error,
                    "[GeneralTagStateActor] Error reading events for full rebuild of tag {Tag}",
                    tag.GetTag());

                // For full rebuild, if we can't read events, throw the error
                // This ensures developers see the issue (like missing event type registration)
                throw new InvalidOperationException(
                    $"Failed to read events for tag {tag.GetTag()} during full rebuild: {error.Message}",
                    error);
            }

            var events = eventsResult
                .GetValue()
                .Where(e => string.Compare(e.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) <=
                    0)
                .ToList();

            // Project all events from scratch
            currentState = null;
            version = 0;
            lastSortedUniqueId = "";

            foreach (var evt in events)
            {
                // Initialize state with EmptyTagStatePayload if needed
                currentState ??= new EmptyTagStatePayload();

                // Project the event
                currentState = projectFunc(currentState, evt);
                version++;

                // Keep track of the last sortable unique id
                if (!string.IsNullOrEmpty(evt.SortableUniqueIdValue))
                {
                    lastSortedUniqueId = evt.SortableUniqueIdValue;
                }
            }
        }

        // If no events were processed, ensure we have at least EmptyTagStatePayload
        currentState ??= new EmptyTagStatePayload();

        return new TagState(
            currentState,
            version,
            lastSortedUniqueId,
            _tagStateId.TagGroup,
            _tagStateId.TagContent,
            _tagStateId.TagProjectorName,
            projectorVersion);
    }

    private ITag CreateTag(string tagGroup, string tagContent)
    {
        // Create tag string in "group:content" format
        var tagString = $"{tagGroup}:{tagContent}";
        return _domainTypes.TagTypes.GetTag(tagString);
    }

    /// <summary>
    ///     Clears the cached state, forcing recomputation on next access
    /// </summary>
    public async Task ClearCacheAsync()
    {
        await _statePersistent.ClearStateAsync();
    }
}
